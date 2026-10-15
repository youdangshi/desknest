using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using DeskNest.Native.Win32;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 「系统的按住标题栏拖动」只对顶层窗口有效。
/// 桌面盒子挂在 WorkerW 之下是子窗口，那个调用会静默失效却返回成功，
/// 于是代码提前 return、兜底的手动拖动永远轮不到——用户看到的就是"盒子拖不动"。
/// 这里把这条规则钉住。
/// </summary>
public sealed class WindowDragPolicyTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [Fact]
    public void CanUseNativeDrag_IsFalseForInvalidHandle()
        => Assert.False(WindowMove.CanUseNativeDrag(IntPtr.Zero));

    [Fact]
    public void BeginDrag_RefusesInvalidHandle()
        => Assert.False(WindowMove.BeginDrag(IntPtr.Zero));

    // 说明：这里故意不去断言"普通顶层窗口一定能用系统拖动"。
    // 实测 WPF 的窗口（含 AllowTransparency/ShowInTaskbar=false 这类组合）在系统看来未必是"无父窗口"，
    // 所以 CanUseNativeDrag 只当参考；收纳盒从设计上就不走系统拖动（见 WindowDrag.EnableMove）。
    /// <summary>
    /// 这一条是本轮"盒子拖不动"的回归：盒子走的是**手动拖动**（按屏幕增量改 Left/Top），
    /// 所以要验证"按屏幕增量改 Left/Top 之后，窗口真的按那个距离移动了"。
    /// 用 GetWindowRect 量真实矩形，而不是读 Left/Top（免得自己骗自己）。
    /// </summary>
    [Fact]
    public void ManualDrag_MovesARealWindowByTheRequestedDelta()
    {
        var moved = false;
        var message = "";

        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window { Width = 240, Height = 160, Left = 80, Top = 90, ShowInTaskbar = false };
                window.Show();
                var hwnd = new WindowInteropHelper(window).Handle;
                var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;

                GetWindowRect(hwnd, out var before);

                // 手动拖动的核心就是这两行（WindowDrag.BeginManualDrag 里做的换算）
                window.Left += 60 / scale;
                window.Top += 40 / scale;
                window.UpdateLayout();

                GetWindowRect(hwnd, out var after);
                var movedX = after.Left - before.Left;
                var movedY = after.Top - before.Top;
                moved = Math.Abs(movedX - 60) <= 2 && Math.Abs(movedY - 40) <= 2;
                message = $"期望位移 60,40 实际 {movedX},{movedY}（DPI={scale:0.00}）";

                window.Close();
            }
            catch (Exception ex) { message = "异常：" + ex.Message; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "建窗口超时");

        Assert.True(moved, "手动拖动必须真的把窗口挪动对应的距离：" + message);
    }
}
