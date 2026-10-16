using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// 窗口拖动：走系统自己的移动循环（等价于按住标题栏拖）。
/// 比自己算坐标可靠——多显示器、DPI 缩放、贴边吸附都由系统处理。
///
/// **但它只对顶层窗口有效**：桌面盒子挂在 explorer 的 WorkerW 之下，是**子窗口**，
/// 而系统不会移动子窗口（子窗口的位置由父窗口说了算），所以 WM_NCLBUTTONDOWN 会静默失效
/// ——调用返回"成功"，窗口却纹丝不动。用户看到的就是"盒子拖不动"。
/// 所以调用方必须先问 <see cref="CanUseNativeDrag"/>。
///
/// 另外实测：WPF 的窗口（包括 `AllowsTransparency` / `ShowInTaskbar=false` 这些组合）在系统看来
/// 未必是"无父窗口"，而且桌面盒子实测是"被 WorkerW 拥有"的窗口。所以
/// <see cref="CanUseNativeDrag"/> 只能当参考——收纳盒从设计上就不走这条路（见 WindowDrag）。
/// </summary>
public static class WindowMove
{
    /// <summary>该窗口能不能用系统的移动循环（只有顶层窗口可以）。</summary>
    public static bool CanUseNativeDrag(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try { return NativeMethods.GetParent(hwnd) == IntPtr.Zero; }
        catch { return false; }
    }

    public static bool BeginDrag(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        // 子窗口一律拒绝：让调用方走手动拖动，而不是"以为成功、其实没动"
        if (!CanUseNativeDrag(hwnd)) return false;
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

    /// <summary>
    /// 按**物理像素**移动窗口。WPF 的 Left/Top 是 DIP，而显示器工作区是物理像素，
    /// 125% 缩放下两者差 1.25 倍——"把跑出屏幕的窗口拉回来"这类判断必须统一用物理像素，
    /// 否则会算出一个"看着在屏幕内、其实在外面"的位置。
    /// </summary>
    public static bool MoveToPhysical(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            return NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Warn("按物理像素移动窗口失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>窗口当前的**物理像素**矩形（屏幕坐标）。WPF 的 Left/Top 是 DIP，别混用。</summary>
    public static (int X, int Y, int Width, int Height)? PhysicalBounds(IntPtr hwnd) => WindowShot.Bounds(hwnd);

    /// <summary>
    /// 把窗口提到**普通窗口的最上层**（不改激活状态、不动位置尺寸）。
    /// 桌面盒平时待在普通窗口层，被别的程序盖住时就点不到、也看不见；
    /// 点它的那一刻把它提上来，用户立刻看得到；随后点别的程序，那个程序自然又盖回来。
    /// 不激活是刻意的：不抢键盘焦点，也不把用户的编辑器顶到后面去。
    /// </summary>
    public static bool BringToTop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOACTIVATE = 0x0010;
            return NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Log.Warn("把窗口提到最前失败：" + ex.Message);
            return false;
        }
    }
}
