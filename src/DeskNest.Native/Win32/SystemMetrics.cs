using System.Diagnostics;
using System.Net.NetworkInformation;

namespace DeskNest.Native.Win32;

/// <summary>系统指标读取：CPU（GetSystemTimes 差值）、内存、网络；GPU 走可选的 nvidia-smi。</summary>
public static class SystemMetrics
{
    private static readonly object Gate = new();
    private static ulong _prevIdle, _prevKernel, _prevUser;
    private static long _prevUp, _prevDown;
    private static DateTime _prevNetSample = DateTime.MinValue;
    private static (int? load, double? temp) _gpuCache = (null, null);
    private static DateTime _gpuCacheAt = DateTime.MinValue;
    private static string? _nvidiaSmi;
    private static bool _nvidiaChecked;

    public static (double cpu, double memPercent, long used, long total, double up, double down) Read()
    {
        lock (Gate)
        {
            var cpu = ReadCpu();
            var (memPercent, used, total) = ReadMemory();
            var (up, down) = ReadNetwork();
            return (cpu, memPercent, used, total, up, down);
        }
    }

    private static double ReadCpu()
    {
        try
        {
            if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var idleT = idle.ToUlong();
            var kernelT = kernel.ToUlong();
            var userT = user.ToUlong();
            if (_prevKernel == 0 && _prevUser == 0)
            {
                _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT;
                return 0;
            }
            var idleDelta = idleT - _prevIdle;
            var kernelDelta = kernelT - _prevKernel;
            var userDelta = userT - _prevUser;
            _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT;

            var total = kernelDelta + userDelta;
            if (total == 0) return 0;
            var busy = total - idleDelta;
            return Math.Clamp(busy * 100.0 / total, 0, 100);
        }
        catch { return 0; }
    }

    private static (double percent, long used, long total) ReadMemory()
    {
        try
        {
            var status = new NativeMethods.MEMORYSTATUSEX
            {
                dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
            };
            if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return (0, 0, 0);
            var used = (long)(status.ullTotalPhys - status.ullAvailPhys);
            return (status.dwMemoryLoad, used, (long)status.ullTotalPhys);
        }
        catch { return (0, 0, 0); }
    }

    private static (double up, double down) ReadNetwork()
    {
        try
        {
            long totalUp = 0, totalDown = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var st = ni.GetIPv4Statistics();
                totalUp += st.BytesSent;
                totalDown += st.BytesReceived;
            }
            var now = DateTime.UtcNow;
            if (_prevNetSample == DateTime.MinValue)
            {
                _prevNetSample = now; _prevUp = totalUp; _prevDown = totalDown;
                return (0, 0);
            }
            var seconds = Math.Max(0.001, (now - _prevNetSample).TotalSeconds);
            var up = Math.Max(0, (totalUp - _prevUp) / seconds);
            var down = Math.Max(0, (totalDown - _prevDown) / seconds);
            _prevNetSample = now; _prevUp = totalUp; _prevDown = totalDown;
            return (up, down);
        }
        catch { return (0, 0); }
    }

    /// <summary>GPU 使用率/温度：只有装了 NVIDIA 驱动（nvidia-smi）时可用，否则返回 null。</summary>
    public static (int? load, double? temp) ReadGpu()
    {
        if (DateTime.Now - _gpuCacheAt < TimeSpan.FromSeconds(2)) return _gpuCache;
        try
        {
            if (!_nvidiaChecked)
            {
                _nvidiaChecked = true;
                var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                _nvidiaSmi = File.Exists(candidate) ? candidate : null;
            }
            if (_nvidiaSmi is null) return (null, null);

            var psi = new ProcessStartInfo(_nvidiaSmi,
                "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (null, null);
            var output = p.StandardOutput.ReadLine();
            p.WaitForExit(1500);
            if (string.IsNullOrWhiteSpace(output)) return (null, null);
            var parts = output.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return (null, null);
            var load = int.TryParse(parts[0], out var l) ? l : (int?)null;
            var temp = double.TryParse(parts[1], out var t) ? t : (double?)null;
            _gpuCache = (load, temp);
            _gpuCacheAt = DateTime.Now;
            return _gpuCache;
        }
        catch { return (null, null); }
    }

    /// <summary>系统空闲时长（用户多久没操作键盘鼠标）。</summary>
    public static TimeSpan IdleTime()
    {
        try
        {
            var info = new NativeMethods.LASTINPUTINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
            };
            if (!NativeMethods.GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64() - info.dwTime);
        }
        catch { return TimeSpan.Zero; }
    }
}
