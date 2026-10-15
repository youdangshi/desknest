using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// 窗口拖动：走系统自己的移动循环（等价于按住标题栏拖）。
/// 比自己算坐标可靠——多显示器、DPI 缩放、贴边吸附都由系统处理；
/// 对挂在桌面层 WorkerW 之下的**子窗口**同样有效。
/// </summary>
public static class WindowMove
{
    public static bool BeginDrag(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("开始窗口拖动失败：" + ex.Message);
            return false;
        }
    }
}
