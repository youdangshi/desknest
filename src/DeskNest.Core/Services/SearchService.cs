using System.Collections.Concurrent;
using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

public enum SearchItemKind { App, File, Folder, InBox, Command, Setting }

public sealed record SearchHit(SearchItemKind Kind, string Title, string Subtitle, string Path, int Score);

/// <summary>
/// 搜索索引：默认全盘（可限定盘符/目录），后台低优先级线程扫描并限速；
/// 查询走内存子串匹配（10 万条规模下 &lt; 30 ms）。只索引名称/路径/大小/时间，不读文件内容。
/// </summary>
public sealed class SearchService : IDisposable
{
    private readonly ConcurrentDictionary<string, IndexEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SearchHit> _commands = new();
    private CancellationTokenSource? _cts;

    public sealed record IndexEntry(string Path, bool IsDirectory, long Size, DateTime Modified);

    public int IndexedCount => _index.Count;
    public bool IsIndexing { get; private set; }
    public DateTimeOffset? LastIndexedAt { get; private set; }
    public event Action? IndexChanged;

    private static readonly string[] ExcludeFolderNames =
    {
        "$Recycle.Bin", "System Volume Information", "WinSxS", "node_modules", ".git",
        "Package Cache", "Temp", "tmp", ".vs", "obj"
    };

    private readonly Repository? _repo;
    private volatile bool _dirty;

    public SearchService(Repository? repo = null)
    {
        _repo = repo;
        _commands.Add(new SearchHit(SearchItemKind.Command, "整理桌面", "一键整理并预览", "cmd://organize", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "新建收纳盒", "创建一个盒子", "cmd://newbox", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "隐藏/显示桌面图标", "切换桌面图标", "cmd://toggleicons", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "显示/隐藏桌面层", "一键开关所有盒与组件", "cmd://togglelayer", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "打开设置", "设置中心", "cmd://settings", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "使用统计", "今天各应用用了多久", "cmd://usage", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "性能监视", "CPU/内存/GPU/网络", "cmd://perf", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "计划关机", "定时/倒计时/空闲", "cmd://power", 100));
        _commands.Add(new SearchHit(SearchItemKind.Command, "API 面板", "余额与命中率", "cmd://api", 100));
    }

    /// <summary>开始索引。roots 为空＝所有固定磁盘。</summary>
    public void StartIndexing(IEnumerable<string>? roots = null)
    {
        StopIndexing();
        LoadFromStore();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        // 传 null 或空列表都表示"索引所有固定磁盘"（这是默认行为）
        var configured = roots?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? new List<string>();
        var include = configured.Count > 0
            ? configured
            : DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        if (include.Count == 0) Log.Warn("没有找到可索引的固定磁盘");

        IsIndexing = true;
        var worker = new Thread(() =>
        {
            try
            {
                foreach (var root in include)
                {
                    if (token.IsCancellationRequested) break;
                    // 排除规则只针对"被索引根目录下的相对路径"，避免索引范围指到含 Temp 字样的目录时被整体跳过
                    Walk(root, root, token, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
                LastIndexedAt = DateTimeOffset.Now;
                Log.Info($"索引完成：{_index.Count} 条");
                SaveToStore();
            }
            catch (Exception ex) { Log.Warn("索引异常：" + ex.Message); }
            finally
            {
                IsIndexing = false;
                IndexChanged?.Invoke();
            }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "DeskNest.Indexer" };
        worker.Start();
    }

    private void Walk(string root, string dir, CancellationToken token, HashSet<string> visited)
    {
        if (token.IsCancellationRequested) return;
        if (!visited.Add(dir)) return;
        var relative = Path.GetRelativePath(root, dir);
        if (!relative.Equals(".", StringComparison.Ordinal) && IsExcluded(relative)) return;

        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch { return; }

        var counter = 0;
        foreach (var e in entries)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                var isDir = Directory.Exists(e);
                long size = 0;
                var modified = DateTime.MinValue;
                if (!isDir)
                {
                    var info = new FileInfo(e);
                    size = info.Length;
                    modified = info.LastWriteTime;
                }
                _index[e] = new IndexEntry(e, isDir, size, modified);
                _dirty = true;
            }
            catch { }

            if (++counter % 3000 == 0) Thread.Sleep(2);   // 限速，避免影响前台

            try
            {
                if (Directory.Exists(e) && !File.GetAttributes(e).HasFlag(FileAttributes.ReparsePoint))
                    Walk(root, e, token, visited);
            }
            catch { }
        }
    }

    private static bool IsExcluded(string relativePath)
    {
        var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => ExcludeFolderNames.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    public void StopIndexing()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        SaveToStore();
    }

    /// <summary>从数据库加载上次的索引，启动即可搜索（不必等重扫）。</summary>
    public int LoadFromStore()
    {
        if (_repo is null) return 0;
        try
        {
            var rows = _repo.LoadSearchIndex();
            foreach (var e in rows) _index[e.Path] = e;
            if (rows.Count > 0)
            {
                LastIndexedAt = DateTimeOffset.Now;
                Log.Info($"已从本地缓存加载搜索索引：{rows.Count} 条");
                IndexChanged?.Invoke();
            }
            return rows.Count;
        }
        catch (Exception ex)
        {
            Log.Warn("加载搜索索引失败：" + ex.Message);
            return 0;
        }
    }

    private void SaveToStore()
    {
        if (_repo is null || _index.IsEmpty || !_dirty) return;
        try
        {
            _repo.SaveSearchIndex(_index.Values);
            _dirty = false;
        }
        catch (Exception ex) { Log.Warn("保存搜索索引失败：" + ex.Message); }
    }

    public void Clear()
    {
        _index.Clear();
        IndexChanged?.Invoke();
    }

    public List<SearchHit> Query(string text, int limit = 60)
    {
        var q = (text ?? "").Trim();
        var results = new List<SearchHit>();
        if (q.Length == 0) return _commands.Take(limit).ToList();

        if (q.StartsWith('/'))
        {
            var cmdText = q[1..];
            return _commands
                .Where(c => c.Title.Contains(cmdText, StringComparison.OrdinalIgnoreCase)
                         || c.Subtitle.Contains(cmdText, StringComparison.OrdinalIgnoreCase))
                .Take(limit).ToList();
        }

        foreach (var c in _commands.Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)))
            results.Add(c);

        var needle = q.ToLowerInvariant();
        foreach (var (path, entry) in _index)
        {
            var name = Path.GetFileName(path);
            var idx = name.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
            {
                idx = path.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal);
                if (idx < 0) continue;
            }
            var score = name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 300
                      : name.Contains(q, StringComparison.OrdinalIgnoreCase) ? 200 : 60;
            score -= Math.Min(60, idx / 3);
            results.Add(new SearchHit(entry.IsDirectory ? SearchItemKind.Folder : SearchItemKind.File,
                name, entry.IsDirectory ? "文件夹" : FormatSize(entry.Size), path, score));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Path.Length).Take(limit).ToList();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    public void Dispose() => StopIndexing();
}
