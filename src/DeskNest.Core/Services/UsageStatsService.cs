using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

public sealed record ForegroundInfo(string ProcessName, string WindowTitle);

public sealed record AppUsage(string ProcessName, long Seconds, int Sessions, double Share);

/// <summary>
/// 应用使用统计（ManicTime 风格）：低频采样前台窗口 → 会话归并 → 日/周/月报表。
/// 隐私：默认只记录进程名，窗口标题可选且必须显式开启；全部本地。
/// </summary>
public sealed class UsageStatsService : IDisposable
{
    private readonly Repository _repo;
    private readonly SettingsService _settings;
    private readonly Func<ForegroundInfo?> _sample;
    private readonly System.Threading.Timer _timer;
    private string? _currentProcess;
    private string? _currentTitle;
    private DateTimeOffset _sessionStart;
    private DateTimeOffset _lastSeen;
    private readonly object _gate = new();

    public UsageStatsService(Repository repo, SettingsService settings, Func<ForegroundInfo?> sample)
    {
        _repo = repo;
        _settings = settings;
        _sample = sample;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool Enabled => _settings.Get("usage.enabled", false);
    public bool RecordTitles => _settings.Get("usage.recordTitles", false);
    public bool Tracked => Enabled && _settings.Get("modules.UsageStats", false);

    public void Start()
    {
        if (!Tracked) { Stop(); return; }
        _timer.Change(0, 1000);
        Log.Info("使用统计已启动（1 秒采样）");
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Flush();
    }

    private void Tick()
    {
        try
        {
            if (!Tracked) { Flush(); return; }
            var fg = _sample();
            var now = DateTimeOffset.Now;
            if (fg is null) { FlushIfIdle(now); return; }

            lock (_gate)
            {
                if (_currentProcess is null)
                {
                    _currentProcess = fg.ProcessName;
                    _currentTitle = RecordTitles ? fg.WindowTitle : null;
                    _sessionStart = now;
                    _lastSeen = now;
                    return;
                }

                // 同一应用连续使用：延长会话
                if (string.Equals(_currentProcess, fg.ProcessName, StringComparison.OrdinalIgnoreCase)
                    && now - _lastSeen < TimeSpan.FromSeconds(10))
                {
                    _lastSeen = now;
                    return;
                }

                FlushLocked(now);
                _currentProcess = fg.ProcessName;
                _currentTitle = RecordTitles ? fg.WindowTitle : null;
                _sessionStart = now;
                _lastSeen = now;
            }
        }
        catch (Exception ex) { Log.Warn("使用统计采样异常：" + ex.Message); }
    }

    private void FlushIfIdle(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_currentProcess is not null && now - _lastSeen > TimeSpan.FromSeconds(30))
                FlushLocked(now);
        }
    }

    private void FlushLocked(DateTimeOffset now)
    {
        if (_currentProcess is null) return;
        var seconds = (long)Math.Max(1, (now - _sessionStart).TotalSeconds);
        if (seconds >= 3)
        {
            _repo.InsertUsage(new UsageSessionRecord
            {
                ProcessName = _currentProcess,
                WindowTitle = _currentTitle,
                StartAt = _sessionStart,
                EndAt = now,
                DurationSeconds = seconds
            });
        }
        _currentProcess = null;
        _currentTitle = null;
    }

    /// <summary>把进行中的会话落盘（关程序时调用）。</summary>
    public void Flush()
    {
        lock (_gate) FlushLocked(DateTimeOffset.Now);
    }

    public List<AppUsage> Aggregate(DateTimeOffset from, DateTimeOffset to)
    {
        var sessions = _repo.UsageSince(from).Where(s => s.StartAt <= to).ToList();
        var total = Math.Max(1, sessions.Sum(s => s.DurationSeconds));
        return sessions
            .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppUsage(g.Key, g.Sum(s => s.DurationSeconds), g.Count(), g.Sum(s => s.DurationSeconds) * 100.0 / total))
            .OrderByDescending(a => a.Seconds)
            .ToList();
    }

    public List<UsageSessionRecord> Timeline(DateTimeOffset from, DateTimeOffset to)
        => _repo.UsageSince(from).Where(s => s.StartAt <= to).OrderBy(s => s.StartAt).ToList();

    public void Clear() => _repo.ClearUsage();

    public void Dispose() => _timer.Dispose();
}
