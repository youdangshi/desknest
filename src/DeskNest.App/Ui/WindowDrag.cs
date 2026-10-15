using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskNest.App.Ui;

/// <summary>
/// 无边框窗口（WindowStyle=None）在 WPF 里没有系统自带的拖动区与缩放边框，必须自己实现：
///  - EnableMove：按住某元素拖动整个窗口（屏幕坐标算增量，兼容挂在桌面层 WorkerW 下的子窗口）
///  - WrapWithResizeGrips：在内容外套一层"透明缩放边"（右下角手柄 + 四条边），并遵守"锁定尺寸"
/// </summary>
public static class WindowDrag
{
    public enum Edge { BottomRight, Right, Left, Bottom, Top }

    /// <summary>
    /// 缩放的纯计算（便于自动化测试）：给定起始几何与鼠标位移，算出新的 宽/高/位置。
    /// 规则：右下角同时改宽高；左右边只改宽（左边缘还要挪 Left）；上下边只改高（上边缘还要挪 Top）；
    /// 并且都遵守最小尺寸。
    /// </summary>
    public static (double Width, double Height, double Left, double Top) ComputeResize(
        Edge edge, double startWidth, double startHeight, double startLeft, double startTop,
        double dx, double dy, double minWidth = 160, double minHeight = 96)
    {
        var width = startWidth;
        var height = startHeight;
        var left = startLeft;
        var top = startTop;

        switch (edge)
        {
            case Edge.BottomRight:
                width = Math.Max(minWidth, startWidth + dx);
                height = Math.Max(minHeight, startHeight + dy);
                break;
            case Edge.Right:
                width = Math.Max(minWidth, startWidth + dx);
                break;
            case Edge.Left:
                width = Math.Max(minWidth, startWidth - dx);
                left = startLeft + (startWidth - width);
                break;
            case Edge.Bottom:
                height = Math.Max(minHeight, startHeight + dy);
                break;
            case Edge.Top:
                height = Math.Max(minHeight, startHeight - dy);
                top = startTop + (startHeight - height);
                break;
        }
        return (width, height, left, top);
    }

    /// <summary>
    /// 让某个元素可以拖动窗口。
    ///
    /// **收纳盒一律走手动增量拖动**。原因：盒子被挂在桌面层下（实测是"被 WorkerW 拥有"的
    /// 顶层窗口），"发 WM_NCLBUTTONDOWN/HTCAPTION 让系统去拖"这条路在这种窗口上并不可靠——
    /// 它可能悄无声息地什么都不做，而调用方却以为成功、直接 return，用户看到的就是"拖不动"。
    /// 手动拖动每一步都在我们手里，而且能写测试。
    /// </summary>
    /// <param name="canStart">返回 false 时这次按下不触发拖动（比如点在盒子里的图标/按钮上）。</param>
    /// <param name="preferNative">非收纳盒的普通窗口可以传 true，交给系统拖动（更跟手）。</param>
    /// <param name="onBlocked">被"锁定位置"挡住时的回调，用来提示用户，而不是默默没反应。</param>
    public static void EnableMove(Window window, FrameworkElement handle, Func<bool>? locked = null,
        Action? onFinished = null, Func<object?, bool>? canStart = null, bool preferNative = false,
        Action? onBlocked = null)
    {
        // 关键：容器必须有背景（透明也行）才参与命中测试。
        // 背景为 null 的 StackPanel/Border 在空白处"抓空气"——这就是"拖不动"的根因。
        if (handle is System.Windows.Controls.Panel panel && panel.Background is null)
            panel.Background = Brushes.Transparent;
        else if (handle is Border border && border.Background is null)
            border.Background = Brushes.Transparent;

        if (handle.Cursor is null) handle.Cursor = Cursors.SizeAll;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (locked?.Invoke() == true)
            {
                onBlocked?.Invoke();
                return;
            }
            if (e.ClickCount == 2) return;   // 双击留给调用方（卷起/展开）
            if (canStart is not null && !canStart(e.OriginalSource)) return;

            if (preferNative)
            {
                try
                {
                    var hwndNative = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (DeskNest.Native.Win32.WindowMove.CanUseNativeDrag(hwndNative)
                        && DeskNest.Native.Win32.WindowMove.BeginDrag(hwndNative))
                    {
                        onFinished?.Invoke();
                        e.Handled = true;
                        return;
                    }
                }
                catch (Exception ex) { Core.Log.Warn("原生拖动失败，改用手动拖动：" + ex.Message); }
            }

            // 桌面盒走这里：自己算屏幕增量。用 PointToScreen 而不是元素内坐标，
            // 这样即使拖动过程中窗口被改大小、元素重排，位移依然准。
            BeginManualDrag(window, handle, e, onFinished);
            e.Handled = true;
        };
    }

    /// <summary>手动拖动：按下时记住屏幕坐标，移动时按增量挪窗口，松开时落盘。</summary>
    private static void BeginManualDrag(Window window, FrameworkElement handle, MouseButtonEventArgs e, Action? onFinished)
    {
        // PointToScreen 给的是**物理像素**，而 Window.Left/Top 是**与 DPI 无关的单位(DIP)**。
        // 125% 缩放下直接相减会让盒子按鼠标的 1.25 倍跑——必须换算。
        var scale = DpiScaleOf(handle);
        var startScreen = handle.PointToScreen(e.GetPosition(handle));
        var startLeft = window.Left;
        var startTop = window.Top;
        var dragging = true;
        var moved = 0.0;

        Core.Log.Info($"开始拖动窗口（手动）：起点 {startLeft:0},{startTop:0}，DPI={scale:0.00}");

        void OnMove(object _, MouseEventArgs args)
        {
            if (!dragging) return;
            var now = handle.PointToScreen(args.GetPosition(handle));
            var dx = (now.X - startScreen.X) / scale;
            var dy = (now.Y - startScreen.Y) / scale;
            window.Left = startLeft + dx;
            window.Top = startTop + dy;
            moved = Math.Max(moved, Math.Abs(dx) + Math.Abs(dy));
        }

        void OnUp(object _, MouseButtonEventArgs args)
        {
            if (!dragging) return;
            dragging = false;
            handle.ReleaseMouseCapture();
            handle.MouseMove -= OnMove;
            handle.MouseLeftButtonUp -= OnUp;
            Core.Log.Info($"结束拖动窗口：位移约 {moved:0} DIP，落点 {window.Left:0},{window.Top:0}");
            onFinished?.Invoke();
        }

        handle.MouseMove += OnMove;
        handle.MouseLeftButtonUp += OnUp;
        handle.CaptureMouse();
    }

    /// <summary>取元素所在显示器的缩放比例（1.0 表示 100%）。</summary>
    internal static double DpiScaleOf(Visual visual)
    {
        try { return VisualTreeHelper.GetDpi(visual).DpiScaleX; }
        catch { return 1.0; }
    }

    public static UIElement WrapWithResizeGrips(UIElement content, Window window, Func<bool>? sizeLocked = null,
        double minWidth = 160, double minHeight = 96, Action? onFinished = null)
    {
        // 手柄尺寸：以前右/左/上/下只有 6px，用户基本点不中（"没法改大小"的一大原因）。
        // 现在边 10px、右下角 20px，并且右下角画一个可见的小三角做提示。
        const double edge = 10;
        const double corner = 20;

        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(CreateGrip(window, Edge.BottomRight, Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, corner, corner, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Right, Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Center, edge, 40, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Left, Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Center, edge, 40, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Bottom, Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Bottom, 40, edge, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Top, Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Top, 40, edge, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateCornerHint(corner));
        return grid;
    }

    /// <summary>右下角的三点提示：让人一眼看出"这里能拉大小"。它不吃鼠标事件。</summary>
    private static FrameworkElement CreateCornerHint(double size)
    {
        var hint = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 4),
            IsHitTestVisible = false,
            Opacity = 0.45
        };
        foreach (var width in new[] { 9.0, 6.0, 3.0 })
        {
            hint.Children.Add(new Rectangle
            {
                Width = width,
                Height = 1.5,
                Margin = new Thickness(0, 1.5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Fill = Theme.Desktop.SecondaryBrush
            });
        }
        return hint;
    }

    private static FrameworkElement CreateGrip(Window window, Edge edge, Cursor cursor,
        HorizontalAlignment h, VerticalAlignment v, double width, double height,
        Func<bool>? sizeLocked, double minWidth, double minHeight, Action? onFinished)
    {
        var grip = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = Brushes.Transparent,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Cursor = cursor,
            ToolTip = edge == Edge.BottomRight ? "拖动调整大小" : null
        };

        var resizing = false;
        var scale = 1.0;
        Point startScreen = default;
        double startWidth = 0, startHeight = 0, startLeft = 0, startTop = 0;

        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (sizeLocked?.Invoke() == true)
            {
                Ui.Notifications.Toast("尺寸已锁定：右键盒子 → 「锁定大小」取消勾选即可拉伸", 4);
                return;
            }
            resizing = true;
            scale = DpiScaleOf(grip);
            startScreen = grip.PointToScreen(e.GetPosition(grip));
            startWidth = window.Width;
            startHeight = window.Height;
            startLeft = window.Left;
            startTop = window.Top;
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (!resizing) return;
            var now = grip.PointToScreen(e.GetPosition(grip));
            // 同样是物理像素 → DIP 的换算（见 BeginManualDrag 里的说明）
            var dx = (now.X - startScreen.X) / scale;
            var dy = (now.Y - startScreen.Y) / scale;

            var next = ComputeResize(edge, startWidth, startHeight, startLeft, startTop, dx, dy, minWidth, minHeight);
            window.Width = next.Width;
            window.Height = next.Height;
            if (edge is Edge.Left) window.Left = next.Left;    // 左边缘缩放需要同时挪窗口
            if (edge is Edge.Top) window.Top = next.Top;       // 上边缘同理
        };
        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!resizing) return;
            resizing = false;
            grip.ReleaseMouseCapture();
            onFinished?.Invoke();
        };
        return grip;
    }
}
