namespace DeskNest.Native.Win32;

/// <summary>
/// 给"生成文档截图"用的窗口几何/置顶助手。放在原生层是因为要拿 GetWindowRect 与 SetForegroundWindow，
/// 而这些都是 user32 的东西；真正抓像素由 App 层用 GDI 完成（原生层不引绘图库）。
/// </summary>
public static class WindowShot
{
    /// <summary>窗口在屏幕上的矩形（含边框）。拿不到时返回 null。</summary>
    public static (int X, int Y, int W, int H)? Bounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return null;
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return null;
            return (r.Left, r.Top, w, h);
        }
        catch { return null; }
    }

    /// <summary>把窗口提到最前（保证按屏幕抓图时它不会被别的窗口盖住）。</summary>
    public static bool BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            NativeMethods.ShowWindow(hwnd, 9);              // SW_RESTORE
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);
            return true;
        }
        catch { return false; }
    }
}
