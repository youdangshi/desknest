namespace DeskNest.Core.Services;

public sealed record PerfSample(
    double CpuPercent,
    double MemoryPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double NetUpBytesPerSec,
    double NetDownBytesPerSec,
    int? GpuPercent,
    double? CpuTempC,
    double? GpuTempC,
    DateTimeOffset At);

/// <summary>性能采样：CPU/内存/网络由系统接口直接读取，GPU 由可选的 nvidia-smi 提供。</summary>
public sealed class PerfSampler : IDisposable
{
    private readonly Func<(double cpu, double memPercent, long used, long total, double up, double down)> _system;
    private readonly Func<(int? load, double? temp)>? _gpu;
    private readonly System.Threading.Timer _timer;
    private readonly List<PerfSample> _history = new();
    private readonly object _gate = new();

    public PerfSampler(
        Func<(double cpu, double memPercent, long used, long total, double up, double down)> systemReader,
        Func<(int? load, double? temp)>? gpuReader = null)
    {
        _system = systemReader;
        _gpu = gpuReader;
        _timer = new System.Threading.Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public PerfSample? Latest { get; private set; }
    public bool Running { get; private set; }
    public int IntervalMs { get; set; } = 1000;
    public event Action<PerfSample>? Sampled;

    public void Start()
    {
        Running = true;
        _timer.Change(0, IntervalMs);
    }

    public void Stop()
    {
        Running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void Sample()
    {
        try
        {
            var (cpu, memPercent, used, total, up, down) = _system();
            int? gpu = null;
            double? gpuTemp = null;
            if (_gpu is not null)
            {
                var (g, t) = _gpu();
                gpu = g;
                gpuTemp = t;
            }
            var s = new PerfSample(cpu, memPercent, used, total, up, down, gpu, null, gpuTemp, DateTimeOffset.Now);
            Latest = s;
            lock (_gate)
            {
                _history.Add(s);
                if (_history.Count > 600) _history.RemoveRange(0, _history.Count - 600);
            }
            Sampled?.Invoke(s);
        }
        catch (Exception ex) { Log.Warn("性能采样异常：" + ex.Message); }
    }

    public List<PerfSample> History()
    {
        lock (_gate) return _history.ToList();
    }

    public void Dispose() => _timer.Dispose();
}
