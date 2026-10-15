using System.Text.Json;
using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>
/// 整理引擎：先"扫描 + 生成计划"（给用户预览），确认后才"执行"；
/// 执行过程记录完整移动清单，任何一次整理都能整体撤销（幂等）。
/// </summary>
public sealed class OrganizeEngine
{
    private readonly Repository _repo;
    private readonly FileOps _ops;

    public OrganizeEngine(Repository repo, FileOps ops) { _repo = repo; _ops = ops; }

    public sealed record ScanOptions(
        string SourceDirectory,
        bool IncludeShortcuts = false,
        bool IncludeHidden = false,
        bool UseFolderMode = false,
        bool PreferExistingBoxes = true,
        string? ManagedRoot = null);

    public sealed record PlanItem(string SourcePath, string TargetDirectory, string? BoxId, string Category, string Reason);
    public sealed record Plan(List<PlanItem> Items, List<string> Skipped);

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public Plan BuildPlan(ScanOptions opt)
    {
        var items = new List<PlanItem>();
        var skipped = new List<string>();
        var boxes = _repo.Boxes();
        var managedRoot = opt.ManagedRoot ?? AppPaths.DefaultManagedRoot;
        var rules = _repo.Rules().Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();

        foreach (var path in SafeEnumerate(opt.SourceDirectory))
        {
            var name = Path.GetFileName(path);
            FileAttributes attrs;
            try { attrs = File.GetAttributes(path); }
            catch { skipped.Add(name + "（无法读取属性）"); continue; }

            if (!opt.IncludeHidden && attrs.HasFlag(FileAttributes.Hidden)) { skipped.Add(name + "（隐藏）"); continue; }

            var cat = RulesEngine.Classify(path);
            if (cat == FileCategory.Shortcut && !opt.IncludeShortcuts) { skipped.Add(name + "（快捷方式，按设置保留）"); continue; }

            var ruleHit = rules.FirstOrDefault(r => RulesEngine.Matches(RulesEngine.ParseConditions(r.ConditionsJson), path, DateTimeOffset.Now));
            if (ruleHit is not null)
            {
                var act = RulesEngine.ParseActions(ruleHit.ActionsJson);
                var box = act.BoxId is null ? null : boxes.FirstOrDefault(b => b.Id == act.BoxId);
                var dir = box?.StorageRoot ?? Path.Combine(managedRoot, box?.Name ?? "未分类");
                if (!string.IsNullOrWhiteSpace(act.SubFolder)) dir = Path.Combine(dir, act.SubFolder!);
                if (act.CreateDateSubFolder) dir = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM"));
                items.Add(new PlanItem(path, dir, box?.Id, RulesEngine.CategoryName(cat), $"规则「{ruleHit.Name}」"));
                continue;
            }

            if (opt.UseFolderMode)
            {
                var folder = Path.Combine(opt.SourceDirectory, RulesEngine.CategoryName(cat));
                items.Add(new PlanItem(path, folder, null, RulesEngine.CategoryName(cat), "文件夹归集"));
                continue;
            }

            BoxRecord? target = null;
            if (opt.PreferExistingBoxes && cat != FileCategory.Other)
            {
                var catName = RulesEngine.CategoryName(cat);
                target = boxes.FirstOrDefault(b => b.Kind is BoxKind.Normal or BoxKind.Drawer
                    && b.Name.Contains(catName, StringComparison.OrdinalIgnoreCase));
            }
            var targetDir = target?.StorageRoot ?? Path.Combine(managedRoot, RulesEngine.CategoryName(cat));
            items.Add(new PlanItem(path, targetDir, target?.Id, RulesEngine.CategoryName(cat),
                target is null ? $"按类型「{RulesEngine.CategoryName(cat)}」" : $"匹配已有盒「{target.Name}」"));
        }
        return new Plan(items, skipped);
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch (Exception ex) { Log.Warn($"枚举失败 {dir}: {ex.Message}"); yield break; }
        foreach (var e in entries)
        {
            var name = Path.GetFileName(e);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            yield return e;
        }
    }

    /// <summary>执行计划，返回本次整理记录（含完整移动清单）。</summary>
    public OrganizeRunRecord Execute(Plan plan, string trigger = "Manual", Action<string>? progress = null)
    {
        var run = new OrganizeRunRecord { Trigger = trigger };
        var moves = new List<OrganizeMove>();
        var done = 0;

        foreach (var it in plan.Items)
        {
            progress?.Invoke($"正在整理：{Path.GetFileName(it.SourcePath)}");
            var res = _ops.MoveTo(it.SourcePath, it.TargetDirectory);
            if (!res.Ok) { Log.Warn($"整理失败 {it.SourcePath}：{res.Error}"); continue; }

            var storedPath = res.StoredPath!;
            moves.Add(new OrganizeMove { From = it.SourcePath, To = storedPath, BoxId = it.BoxId, Reason = it.Reason });

            if (it.BoxId is { } boxId && _repo.Box(boxId) is not null)
            {
                _repo.Upsert(new ItemRecord
                {
                    BoxId = boxId,
                    Kind = Directory.Exists(storedPath) ? ItemKind.Folder : ItemKind.File,
                    DisplayName = Path.GetFileName(storedPath),
                    SourcePath = it.SourcePath,
                    StoredPath = storedPath,
                    Ext = Path.GetExtension(storedPath).ToLowerInvariant(),
                    SizeBytes = SafeSize(storedPath),
                    AddedAt = DateTimeOffset.Now
                });
            }
            done++;
        }

        run.MovedCount = done;
        run.FinishedAt = DateTimeOffset.Now;
        run.MovesJson = JsonSerializer.Serialize(moves, J);
        _repo.InsertRun(run);
        Log.Info($"整理完成：{done} 项（{trigger}）");
        return run;
    }

    public static long SafeSize(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path))
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { }
        return 0;
    }

    /// <summary>撤销一次整理：按清单反向移动（目标不存在就跳过，幂等）。</summary>
    public (int Restored, int Failed) Undo(OrganizeRunRecord run)
    {
        List<OrganizeMove>? moves;
        try { moves = JsonSerializer.Deserialize<List<OrganizeMove>>(run.MovesJson, J); }
        catch { moves = null; }
        if (moves is null) return (0, 0);

        var restored = 0;
        var failed = 0;
        foreach (var m in Enumerable.Reverse(moves))
        {
            try
            {
                if (!FileOps.Exists(m.To)) continue;
                var dir = Path.GetDirectoryName(m.From);
                if (string.IsNullOrEmpty(dir)) { failed++; continue; }
                Directory.CreateDirectory(dir);
                var target = FileOps.UniquePath(dir, Path.GetFileName(m.From));
                if (Directory.Exists(m.To)) Directory.Move(m.To, target);
                else File.Move(m.To, target);
                restored++;

                if (m.BoxId is { } boxId)
                {
                    var item = _repo.Items(boxId, false).FirstOrDefault(i => i.StoredPath == m.To);
                    if (item is not null) _repo.DeleteItem(item.Id);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log.Warn($"撤销失败 {m.To}：{ex.Message}");
            }
        }
        run.Undone = true;
        _repo.InsertRun(run);
        Log.Info($"撤销完成：还原 {restored}，失败 {failed}");
        return (restored, failed);
    }
}
