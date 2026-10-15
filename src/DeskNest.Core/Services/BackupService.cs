using System.IO.Compression;
using System.Text.Json;

namespace DeskNest.Core.Services;

public sealed record BackupInfo(string Path, DateTimeOffset CreatedAt, long SizeBytes);

/// <summary>本地备份：把数据目录打包成 zip；保留 N 份；可回滚。</summary>
public sealed class BackupService
{
    private readonly SettingsService _settings;

    public BackupService(SettingsService settings) => _settings = settings;

    public int RetentionCount => Math.Clamp(_settings.Get("backup.retention", 7), 1, 90);
    public string? CustomDirectory => _settings.Get<string?>("backup.directory", null);

    private string Root => string.IsNullOrWhiteSpace(CustomDirectory) ? AppPaths.BackupRoot : CustomDirectory!;

    public BackupInfo Create(string reason = "manual")
    {
        Directory.CreateDirectory(Root);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(Root, $"desknest-{reason}-{stamp}.zip");

        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var f in EnumerateDataFiles())
            {
                try { zip.CreateEntryFromFile(f, Path.GetRelativePath(AppPaths.DataRoot, f), CompressionLevel.Optimal); }
                catch (Exception ex) { Log.Warn($"备份跳过 {f}：{ex.Message}"); }
            }
        }
        var info = new BackupInfo(file, DateTimeOffset.Now, new FileInfo(file).Length);
        Log.Info($"备份完成：{file}（{info.SizeBytes / 1024.0:0.#} KB）");
        Prune();
        return info;
    }

    private IEnumerable<string> EnumerateDataFiles()
    {
        if (!Directory.Exists(AppPaths.DataRoot)) yield break;
        foreach (var f in Directory.EnumerateFiles(AppPaths.DataRoot, "*", SearchOption.AllDirectories))
        {
            if (f.StartsWith(AppPaths.BackupRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
    }

    public void Prune()
    {
        try
        {
            var files = List().OrderByDescending(b => b.CreatedAt).Skip(RetentionCount).ToList();
            foreach (var f in files)
            {
                File.Delete(f.Path);
                Log.Info($"清理旧备份：{Path.GetFileName(f.Path)}");
            }
        }
        catch (Exception ex) { Log.Warn("清理备份失败：" + ex.Message); }
    }

    public List<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(Root)) return list;
        foreach (var f in Directory.EnumerateFiles(Root, "desknest-*.zip"))
        {
            var fi = new FileInfo(f);
            list.Add(new BackupInfo(f, fi.LastWriteTime, fi.Length));
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>回滚：把备份解压覆盖回数据目录（会先把当前数据另存一份）。</summary>
    public (bool Ok, string Message) Restore(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return (false, "备份文件不存在");
            Create("pre-restore");
            using var zip = ZipFile.OpenRead(backupPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var target = Path.Combine(AppPaths.DataRoot, entry.FullName);
                var full = Path.GetFullPath(target);
                if (!full.StartsWith(Path.GetFullPath(AppPaths.DataRoot), StringComparison.OrdinalIgnoreCase))
                    continue;   // 防目录穿越
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                entry.ExtractToFile(full, overwrite: true);
            }
            return (true, "已回滚，请重启桌巢以加载恢复后的数据");
        }
        catch (Exception ex)
        {
            Log.Error("回滚失败：" + ex.Message);
            return (false, ex.Message);
        }
    }

    public string ExportDiagnostics()
    {
        var dir = Path.Combine(AppPaths.DataRoot, "diagnostics");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        AddIfExists(zip, AppPaths.LogFile, "logs/desknest.log");
        AddIfExists(zip, AppPaths.SettingsFile, "settings.json");
        var env = new
        {
            os = Environment.OSVersion.ToString(),
            arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            created = DateTimeOffset.Now,
            note = "诊断包不含任何文件内容；路径信息可用于排错。"
        };
        var entry = zip.CreateEntry("environment.json");
        using var sw = new StreamWriter(entry.Open());
        sw.Write(JsonSerializer.Serialize(env, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    private static void AddIfExists(ZipArchive zip, string file, string entryName)
    {
        try
        {
            if (File.Exists(file)) zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
        catch { }
    }
}
