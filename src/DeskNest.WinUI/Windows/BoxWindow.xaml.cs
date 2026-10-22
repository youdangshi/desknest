using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DeskNest.Core;
using DeskNest.Core.Services;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// 桌面盒窗口。窗口骨架与桌面层挂载是 P2-1；本文件里还有：
///  - 手动增量拖动 + 吸附对齐（P2-2。移植 WPF 版 WindowDrag 的思路：不发 WM_NCLBUTTONDOWN——
///    它对这类 owner 挂接的窗口静默失效；全程物理像素：GetCursorPos 与 AppWindow 都是物理像素，
///    天然避开 WPF 版踩过的 DPI 换算坑；落点按 DPI 换回 DIP 存库，与 WPF 版数据互通）
///  - 卷起/展开（P2-3。先放开 OverlappedPresenter 的最小高度再改高度，否则高度被夹回去——
///    和 WPF 版 2026-10-17 修的是同一个坑）
/// </summary>
public sealed partial class BoxWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private readonly BoxRecord _box;
    private bool _shellReady;
    private double _scale = 1.0;            // 物理像素 / DIP

    // 拖动状态
    private bool _dragging;
    private POINT _dragStartCursor;         // 物理像素
    private global::Windows.Graphics.PointInt32 _dragStartPos;

    // 卷起状态
    private bool _rolledUp;
    private int _expandedHeight;            // 物理像素

    public BoxWindow(BoxRecord box)
    {
        _box = box;
        App.Log($"BoxWindow ctor: begin（{box.Name}#{box.Id}）");
        InitializeComponent();
        Title = box.Name;   // 进程枚举/截图工具靠它认出这个盒子
        TitleText.Text = box.Name;

        ReloadItems();

        CloseButton.Click += (_, _) => { App.Log($"BoxWindow[{_box.Name}] 点击隐藏"); Close(); };
        RollupButton.Click += (_, _) => ToggleRollup();
        WireDrag();

        Activated += OnFirstActivated;
        Closed += (_, _) =>
        {
            App.BoxWindows.Remove(this);
            App.Log($"BoxWindow[{_box.Name}] Closed");
        };
    }

    /// <summary>从数据库读盒内文件项（Core 与原 WPF 版共用，数据互通）。</summary>
    public void ReloadItems()
    {
        try
        {
            var items = App.Repo?.Items(_box.Id, _box.Kind == BoxKind.Mapped) ?? new List<ItemRecord>();
            ItemList.ItemsSource = items;
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Log($"BoxWindow[{_box.Name}] items = {items.Count}");
        }
        catch (Exception ex)
        {
            App.Log($"BoxWindow[{_box.Name}] load items FAILED: {ex.Message}");
        }
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_shellReady) return;
        _shellReady = true;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // 1) 摘掉系统标题栏（盒子的标题栏是自绘的 TitleBarGrid）
            var style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE,
                style & ~WS_CAPTION & ~WS_SYSMENU & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // 2) 挂桌面层：owner = 桌面图标宿主（P0 探针验证过；AttachOwner 内部会核实）
            var attached = DesktopLayer.AttachOwner(hwnd);
            App.Log($"BoxWindow[{_box.Name}] shell: AttachOwner -> {attached}");

            // 3) 位置与尺寸：库里存的是 DIP（与 WPF 版一致），乘 DPI 缩放换物理像素
            _scale = GetDpiForWindow(hwnd) / 96.0;
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                (int)Math.Round(_box.MarginX * _scale), (int)Math.Round(_box.MarginY * _scale),
                (int)Math.Round(_box.Width * _scale), (int)Math.Round(_box.Height * _scale)));
            _expandedHeight = AppWindow.Size.Height;

            // 4) 库里记着"卷起"的话，开局就卷起
            if (_box.RolledUp) SetRolledUp(true, persist: false);

            App.Log($"BoxWindow[{_box.Name}] shell ready: scale={_scale:0.00}, pos={AppWindow.Position.X},{AppWindow.Position.Y}, size={AppWindow.Size.Width}x{AppWindow.Size.Height}");
        }
        catch (Exception ex)
        {
            App.Log($"BoxWindow[{_box.Name}] shell setup FAILED: " + ex);
        }
    }

    // ---------- 拖动（手动增量 + 吸附） ----------

    private void WireDrag()
    {
        TitleBarGrid.PointerPressed += OnDragStart;
        TitleBarGrid.PointerMoved += OnDragMove;
        TitleBarGrid.PointerReleased += OnDragEnd;
        TitleBarGrid.PointerCaptureLost += (_, _) => _dragging = false;
    }

    private void OnDragStart(object sender, PointerRoutedEventArgs e)
    {
        if (_box.LockPosition) return;
        var point = e.GetCurrentPoint(TitleBarGrid);
        if (!point.Properties.IsLeftButtonPressed) return;
        // 点在标题栏按钮上不拖（按钮要吃这次点击）
        if (e.OriginalSource is DependencyObject d && IsInside<Button>(d)) return;

        GetCursorPos(out _dragStartCursor);
        _dragStartPos = AppWindow.Position;
        _dragging = true;
        TitleBarGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnDragMove(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        GetCursorPos(out var now);
        var nextX = _dragStartPos.X + now.X - _dragStartCursor.X;
        var nextY = _dragStartPos.Y + now.Y - _dragStartCursor.Y;

        // 吸附：其它盒子 + 主屏工作区（都是物理像素，与 GetCursorPos 一致）
        var snap = WindowSnap.Snap(
            new WindowSnap.Rect(nextX, nextY, AppWindow.Size.Width, AppWindow.Size.Height),
            SnapTargets());
        AppWindow.Move(new global::Windows.Graphics.PointInt32((int)snap.X, (int)snap.Y));
    }

    private void OnDragEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TitleBarGrid.ReleasePointerCapture(e.Pointer);

        // 落点存库（换回 DIP，WPF 版读出来就是同一位置）
        _box.MarginX = Math.Round(AppWindow.Position.X / _scale, 1);
        _box.MarginY = Math.Round(AppWindow.Position.Y / _scale, 1);
        try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("保存盒子位置失败：" + ex.Message); }
        App.Log($"BoxWindow[{_box.Name}] drag end: {_box.MarginX},{_box.MarginY} DIP");
    }

    private List<WindowSnap.Rect> SnapTargets()
    {
        var list = new List<WindowSnap.Rect>();
        foreach (var other in App.BoxWindows)
        {
            if (ReferenceEquals(other, this)) continue;
            list.Add(new WindowSnap.Rect(other.AppWindow.Position.X, other.AppWindow.Position.Y,
                other.AppWindow.Size.Width, other.AppWindow.Size.Height));
        }
        if (Monitors.Primary() is { } screen)
            list.Add(new WindowSnap.Rect(screen.WorkX, screen.WorkY, screen.WorkWidth, screen.WorkHeight));
        return list;
    }

    private static bool IsInside<T>(DependencyObject node) where T : DependencyObject
    {
        for (var cur = node; cur is not null; cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur))
            if (cur is T) return true;
        return false;
    }

    // ---------- 卷起 / 展开 ----------

    private void ToggleRollup() => SetRolledUp(!_rolledUp, persist: true);

    private void SetRolledUp(bool rolled, bool persist)
    {
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        var titleHeight = (int)Math.Round(36 * _scale);
        if (rolled)
        {
            _expandedHeight = AppWindow.Size.Height;
            // 关键坑（WPF 版踩过）：先放开最小高度，否则设高度会被夹回去、卷起不生效
            if (presenter is not null) presenter.PreferredMinimumHeight = 0;
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, titleHeight));
        }
        else
        {
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width,
                Math.Max(_expandedHeight, titleHeight * 4)));
        }
        _rolledUp = rolled;
        _box.RolledUp = rolled;
        if (persist)
        {
            try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("保存卷起状态失败：" + ex.Message); }
        }
        App.Log($"BoxWindow[{_box.Name}] rollup -> {rolled}（height={AppWindow.Size.Height}）");
    }
}
