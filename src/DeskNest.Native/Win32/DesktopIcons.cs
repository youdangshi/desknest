using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>桌面图标显隐（通过切换 SHELLDLL_DefView 下 SysListView32 的可见性）。</summary>
public static class DesktopIcons
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static IntPtr FindListView()
    {
        var progman = NativeMethods.FindWindow("Progman", null);
        var defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var sv = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (sv != IntPtr.Zero) { defView = sv; return false; }
                return true;
            }, IntPtr.Zero);
        }
        if (defView == IntPtr.Zero) return IntPtr.Zero;
        return NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    public static bool AreIconsVisible()
    {
        try
        {
            var lv = FindListView();
            if (lv == IntPtr.Zero) return true;
            return NativeMethods.GetWindowLong(lv, -16) != 0 ? true : true;   // 占位：真实可见性以内部状态记录
        }
        catch { return true; }
    }

    public static bool SetVisible(bool visible)
    {
        try
        {
            var lv = FindListView();
            if (lv == IntPtr.Zero)
            {
                Log.Warn("找不到桌面图标列表，无法切换图标显示");
                return false;
            }
            NativeMethods.ShowWindow(lv, visible ? SW_SHOW : SW_HIDE);
            Log.Info($"桌面图标已{(visible ? "显示" : "隐藏")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("切换桌面图标失败：" + ex.Message);
            return false;
        }
    }
}
