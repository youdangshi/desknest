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

    public static void EnableMove(Window window, FrameworkElement handle, Func<bool>? locked = null, Action? onFinished = null)
    {
        // 关键：容器必须有背景（透明也行）才参与命中测试。
        // 背景为 null 的 StackPanel/Border 在空白处"抓空气"——这就是"拖不动"的根因。
        if (handle is System.Windows.Controls.Panel panel && panel.Background is null)
            panel.Background = Brushes.Transparent;
        else if (handle is Border border && border.Background is null)
            border.Background = Brushes.Transparent;

        var dragging = false;
        Point startScreen = default;
        double startLeft = 0, startTop = 0;

        handle.Cursor = Cursors.SizeAll;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (locked?.Invoke() == true) return;
            if (e.ClickCount == 2) return;   // 双击留给调用方（卷起/展开）

            // 首选：系统自己的移动循环（跟手，多显示器/DPI 都不用我们操心）
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (DeskNest.Native.Win32.WindowMove.BeginDrag(hwnd))
                {
                    onFinished?.Invoke();
                    e.Handled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Core.Log.Warn("原生拖动失败，退回手动拖动：" + ex.Message);
            }

            // 兜底：自己算增量（正常走不到这里）
            dragging = true;
            startScreen = handle.PointToScreen(e.GetPosition(handle));
            startLeft = window.Left;
            startTop = window.Top;
            handle.CaptureMouse();
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var now = handle.PointToScreen(e.GetPosition(handle));
            window.Left = startLeft + (now.X - startScreen.X);
            window.Top = startTop + (now.Y - startScreen.Y);
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            handle.ReleaseMouseCapture();
            onFinished?.Invoke();
        };
    }

    public static UIElement WrapWithResizeGrips(UIElement content, Window window, Func<bool>? sizeLocked = null,
        double minWidth = 160, double minHeight = 96, Action? onFinished = null)
    {
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(CreateGrip(window, Edge.BottomRight, Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, 14, 14, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Right, Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Center, 6, 28, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Left, Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Center, 6, 28, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Bottom, Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Bottom, 28, 6, sizeLocked, minWidth, minHeight, onFinished));
        grid.Children.Add(CreateGrip(window, Edge.Top, Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Top, 28, 6, sizeLocked, minWidth, minHeight, onFinished));
        return grid;
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
        Point startScreen = default;
        double startWidth = 0, startHeight = 0, startLeft = 0, startTop = 0;

        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (sizeLocked?.Invoke() == true) return;
            resizing = true;
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
            var dx = now.X - startScreen.X;
            var dy = now.Y - startScreen.Y;

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
