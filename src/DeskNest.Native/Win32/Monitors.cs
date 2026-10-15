using System.Runtime.InteropServices;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

public sealed record MonitorInfo(string Key, string DeviceName, bool IsPrimary, int X, int Y, int Width, int Height);

/// <summary>显示器枚举：用"设备名 + 工作区"生成稳定 Key，避免存绝对坐标。</summary>
public static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        try
        {
            NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var mi = new NativeMethods.MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                    szDevice = ""
                };
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    var w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    var h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    var primary = (mi.dwFlags & 1) != 0;
                    list.Add(new MonitorInfo(
                        $"{mi.szDevice}:{mi.rcMonitor.Left}:{mi.rcMonitor.Top}:{w}:{h}",
                        mi.szDevice, primary, mi.rcMonitor.Left, mi.rcMonitor.Top, w, h));
                }
                return true;
            };
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Warn("枚举显示器失败：" + ex.Message); }
        return list;
    }

    public static MonitorInfo? Primary() => All().FirstOrDefault(m => m.IsPrimary) ?? All().FirstOrDefault();
}
