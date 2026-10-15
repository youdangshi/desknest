using System.Diagnostics;
using System.Text;
using DeskNest.Core.Services;

namespace DeskNest.Native.Win32;

/// <summary>前台窗口采样（使用统计用）：默认只取进程名，窗口标题由用户显式允许后才有意义。</summary>
public static class ForegroundWatcher
{
    public static ForegroundInfo? Sample()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            string processName;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
            }
            catch { processName = "unknown"; }

            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return new ForegroundInfo(processName, sb.ToString());
        }
        catch { return null; }
    }
}
