using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using DeskNest.App.Ui;
using DeskNest.Core;

namespace DeskNest.App.Host;

/// <summary>
/// 让桌面层里的**输入框真的能打字**。
///
/// 背景：盒子与组件是"点它不抢焦点"的普通窗口（WS_EX_NOACTIVATE），这样点它们不会把你的
/// 编辑器顶到后面去。代价是这类窗口**永远拿不到键盘焦点**——于是待办、随记的输入框看着能点，
/// 打字却全打给了别的程序（用户反馈"待办不能真的添加"就是这个原因）。
///
/// 做法：鼠标按到输入框上时**临时**允许激活并把焦点给它；输入框失去焦点后再设回"不抢焦点"。
/// 这样既保住了平时不打扰，又让输入框能用。
/// </summary>
public static class DesktopInput
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public static void EnableTyping(Window window)
    {
        window.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, e) =>
            {
                if (e.OriginalSource is not TextBoxBase box) return;
                AllowActivation(window);
                box.Focus();
                Keyboard.Focus(box);
            }), handledEventsToo: true);

        window.AddHandler(UIElement.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, _) => RestoreNoActivate(window)), handledEventsToo: true);
    }

    /// <summary>临时允许激活（输入期间），并把它拉到前台。</summary>
    private static void AllowActivation(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var ex = Native.Win32.WindowStyles.GetExStyle(hwnd);
            Native.Win32.WindowStyles.SetExStyle(hwnd, ex & ~WS_EX_NOACTIVATE);
            Native.Win32.WindowStyles.SetForeground(hwnd);
        }
        catch (Exception err) { Log.Warn("允许输入激活失败：" + err.Message); }
    }

    /// <summary>输入结束，恢复"不抢焦点"。</summary>
    public static void RestoreNoActivate(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var ex = Native.Win32.WindowStyles.GetExStyle(hwnd);
            Native.Win32.WindowStyles.SetExStyle(hwnd, ex | WS_EX_NOACTIVATE);
        }
        catch (Exception err) { Log.Warn("恢复不抢焦点失败：" + err.Message); }
    }
}
