using System;
using System.Runtime.InteropServices;

namespace DeskNest.Native.Win32;

/// <summary>
/// 窗口"非客户区"尺寸测量。
///
/// 为什么需要：`AppWindow.Size` 是**窗口**尺寸，不是客户区。Win10 的窗口即便摘了标题栏，
/// 仍带一圈不可见的拉伸边框（WS_THICKFRAME），客户区比窗口小好几像素。
/// 收起时如果按"标题栏高度 × DPI"设窗口高度，客户区就会比标题栏还矮，最后一行字被裁——
/// 用户反馈「收起后标题还是显示不全」根因就在这里。
/// </summary>
public static class WindowMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    /// <summary>返回（横向, 纵向）非客户区大小，单位物理像素。</summary>
    public static (int dx, int dy) FrameInsets(IntPtr hwnd)
    {
        try
        {
            if (GetWindowRect(hwnd, out var w) && GetClientRect(hwnd, out var c))
                return ((w.Right - w.Left) - (c.Right - c.Left),
                        (w.Bottom - w.Top) - (c.Bottom - c.Top));
        }
        catch { }
        return (0, 0);
    }
}
