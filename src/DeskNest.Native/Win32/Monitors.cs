using System.Runtime.InteropServices;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

public sealed record MonitorInfo(
    string Key, string DeviceName, bool IsPrimary,
    int X, int Y, int Width, int Height,
    int WorkX, int WorkY, int WorkWidth, int WorkHeight)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int WorkRight => WorkX + WorkWidth;
    public int WorkBottom => WorkY + WorkHeight;

    /// <summary>"2560×1440"这种给人看的写法。</summary>
    public string Resolution => $"{Width}×{Height}";

    /// <summary>判断一个矩形是否与本显示器的工作区有重叠（用于"盒子跑丢了吗"的检测）。</summary>
    public bool IntersectsWorkArea(double left, double top, double width, double height)
        => left < WorkRight && left + width > WorkX && top < WorkBottom && top + height > WorkY;
}

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
                        mi.szDevice, primary, mi.rcMonitor.Left, mi.rcMonitor.Top, w, h,
                        mi.rcWork.Left, mi.rcWork.Top,
                        mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top));
                }
                return true;
            };
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Warn("枚举显示器失败：" + ex.Message); }
        return list;
    }

    public static MonitorInfo? Primary() => All().FirstOrDefault(m => m.IsPrimary) ?? All().FirstOrDefault();

    /// <summary>鼠标当前所在的那块显示器；取不到时回退主屏。</summary>
    public static MonitorInfo? ForCursor()
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var pt)) return Primary();
            return All().FirstOrDefault(m => pt.X >= m.X && pt.X < m.Right && pt.Y >= m.Y && pt.Y < m.Bottom)
                   ?? Primary();
        }
        catch { return Primary(); }
    }
}
