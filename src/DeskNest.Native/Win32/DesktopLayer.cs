using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// 桌面层宿主：把窗口挂到桌面的 WorkerW 之下（壁纸之上、图标之下）。
/// 不注入、不挂钩任何系统进程；失败时调用方应降级为普通置顶窗口。
/// </summary>
public static class DesktopLayer
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    /// <summary>找到桌面 WorkerW（用 Progman 的 0x052C 消息让系统把桌面拆成 WorkerW 层）。</summary>
    public static IntPtr FindDesktopWorkerW()
    {
        try
        {
            var progman = NativeMethods.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            NativeMethods.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

            IntPtr workerW = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var shellView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView == IntPtr.Zero) return true;
                // 找到 DefView 后，下一个 WorkerW 就是可以挂窗口的那层
                workerW = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                return false;
            }, IntPtr.Zero);

            if (workerW == IntPtr.Zero)
            {
                // 有些系统下 DefView 直接在 Progman 里，此时直接挂 Progman
                var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) workerW = progman;
            }
            return workerW;
        }
        catch (Exception ex)
        {
            Log.Warn("查找桌面层失败：" + ex.Message);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 把窗口挂到桌面层。
    /// allowFocus=true：窗口可以正常被点击、聚焦（输入框/右键菜单/按钮才有反应）；
    /// allowFocus=false：传统"不抢焦点"覆盖层（只展示、不交互）。
    /// 两种情况下都只保留"不进任务栏"的样式，并且**显示时不抢焦点**。
    /// </summary>
    public static bool Attach(IntPtr hwnd, bool allowFocus = true)
    {
        try
        {
            var workerW = FindDesktopWorkerW();
            if (workerW == IntPtr.Zero) return false;
            NativeMethods.SetParent(hwnd, workerW);

            var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            if (allowFocus) ex &= ~WS_EX_NOACTIVATE;
            else ex |= WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOZORDER);
            // 显示时不激活：避免一开机就把焦点从别的程序抢过来
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("挂载桌面层失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>脱离桌面层（回退为普通窗口）。</summary>
    public static void Detach(IntPtr hwnd)
    {
        try
        {
            var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            ex &= ~WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            NativeMethods.SetParent(hwnd, IntPtr.Zero);
        }
        catch { }
    }
}
