using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>读写窗口扩展样式（桌面层要用它临时开/关"不抢焦点"）。</summary>
public static class WindowStyles
{
    private const int GWL_EXSTYLE = -20;

    public static int GetExStyle(IntPtr hwnd)
    {
        try { return NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE); }
        catch { return 0; }
    }

    public static bool SetExStyle(IntPtr hwnd, int value)
    {
        try { NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, value); return true; }
        catch (Exception ex) { Log.Warn("设置窗口扩展样式失败：" + ex.Message); return false; }
    }

    public static bool SetForeground(IntPtr hwnd)
    {
        try { return NativeMethods.SetForegroundWindow(hwnd); }
        catch { return false; }
    }
}
