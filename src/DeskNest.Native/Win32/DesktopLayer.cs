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

    /// <summary>
    /// 找到**承载桌面图标**的那个窗口：桌面图标列表（SHELLDLL_DefView）的父窗口。
    ///
    /// 这里踩过一个很贵的坑：以前找的是"壁纸层 WorkerW"（用 Progman 的 0x052C 消息拆出来的那层），
    /// 结果盒子被挂在**图标层下面**——桌面图标列表铺满整个桌面，会把所有鼠标点击都吃掉，
    /// 于是盒子永远收不到点击：拖不动、改不了大小、连标题栏按钮都点不到，日志里连"开始拖动"都没有。
    /// 正确的做法是把盒子挂到图标所在的父窗口里，并置于该父窗口子窗口的最上层。
    /// </summary>
    public static IntPtr FindDesktopHost()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        try
        {
            IntPtr host = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero) return true;
                // 图标列表的父窗口（通常是一个 WorkerW）就是我们该待的地方
                host = NativeMethods.GetParent(defView);
                if (host == IntPtr.Zero) host = hwnd;
                return false;   // 找到就停
            }, IntPtr.Zero);
            if (host != IntPtr.Zero) return host;

            // 兜底：有些系统里 DefView 直接在 Progman 下
            if (progman != IntPtr.Zero
                && NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return progman;
        }
        catch (Exception ex)
        {
            Log.Warn("查找桌面宿主失败：" + ex.Message);
        }
        return IntPtr.Zero;
    }

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
        // 实测结论（2026-10-16，在真机上逐条验证过）：
        //   1) SetParent 到桌面层**不会生效**——紧接着读回父窗口是 0（旧代码没验证，一直"以为挂上了"）；
        //   2) 桌面盒子是 WS_EX_LAYERED（WPF AllowsTransparency）窗口，对它做 SetParent / 改 z 序之后
        //      会出现"窗口属性看着正常、但一个像素都不画"的情况：既看不见，也点不到；
        //   3) 而且即便挂上，盒子会落在**桌面图标层下面**，点击会被图标列表全部吃掉。
        // 所以现在不挂桌面层：直接当一个"点得到、不抢焦点、也不盖住别人"的普通窗口。
        // 代价是它属于普通窗口（其他程序能盖住它、Win+D 会把它一起收走），换来的是核心交互真的能用。
        return false;
    }

    /// <summary>
    /// P0 探针验证过的桌面层方案（docs/28 §10.5）：把"承载桌面图标的宿主窗口"设为本窗口的
    /// **owner**（不是 parent！），外加 WS_EX_TOOLWINDOW（不进 Alt+Tab / 任务栏）。
    /// owner 关系下窗口仍在桌面层附近、可点可拖，Win+D 会随桌面一起显示。
    /// workerw（SetParent 挂进去）会让窗口变小、bottom 会沉到壁纸后面——都不要用。
    /// 返回 false 时调用方应降级为普通窗口。
    /// </summary>
    public static bool AttachOwner(IntPtr hwnd)
    {
        const int GWLP_HWNDPARENT = -8;
        const int GW_OWNER = 4;
        try
        {
            var host = FindDesktopHost();
            if (host == IntPtr.Zero)
            {
                Log.Warn("AttachOwner：找不到桌面宿主窗口");
                return false;
            }
            var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
            NativeMethods.SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, host);

            // 核实（P0 的教训：不核实等于没做——SetParent 就曾被框架悄悄回滚过）
            var actual = NativeMethods.GetWindow(hwnd, GW_OWNER);
            if (actual != host)
            {
                Log.Warn($"AttachOwner：owner 没设上（期望 0x{host.ToInt64():X}，实际 0x{actual.ToInt64():X}）");
                return false;
            }
            Log.Info($"AttachOwner：已把 owner 设为桌面宿主 0x{host.ToInt64():X}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("AttachOwner 失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>老的桌面层挂载实现，保留下来供以后有更好的办法时参考/回退测试。</summary>
    public static bool AttachToDesktopLayerLegacy(IntPtr hwnd, bool allowFocus = true)
    {
        try
        {
            var host = FindDesktopHost();
            if (host == IntPtr.Zero) return false;
            NativeMethods.SetParent(hwnd, host);

            // 置顶到父窗口子窗口的最上层：这样盒子在桌面图标**之上**，点击才落得到它身上；
            // 同时它仍然是桌面的一部分，普通程序窗口照样能盖住它。
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

            var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            if (allowFocus) ex &= ~WS_EX_NOACTIVATE;
            else ex |= WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            // 显示时不激活：避免一开机就把焦点从别的程序抢过来
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);

            // 验证真的挂上去了：SetParent 有可能被窗口框架回滚，不验证就会"以为挂好了、其实没有"，
            // 前面查"盒子拖不动"时就是被这种"看起来成功"的状态骗了好几次。
            var actualParent = NativeMethods.GetParent(hwnd);
            if (actualParent == host) return true;
            Log.Warn($"SetParent 没生效（期望父窗口 {host}，实际 {actualParent}），改用普通窗口方案");
            return false;
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

    /// <summary>
    /// 回退方案：把窗口设成"可点击但不抢焦点"。
    /// 这样点它不会把别的程序压在后面，也不会自己跳到最前面——接近桌面组件的手感。
    /// </summary>
    public static void MakeNoActivate(IntPtr hwnd, bool alsoHideFromTaskbar = true)
    {
        try
        {
            var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_NOACTIVATE;
            if (alsoHideFromTaskbar) ex |= WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch (Exception ex) { Log.Warn("设置不抢焦点样式失败：" + ex.Message); }
    }
}
