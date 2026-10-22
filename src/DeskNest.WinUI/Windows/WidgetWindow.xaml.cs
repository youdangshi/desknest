using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DeskNest.Core;
using DeskNest.Core.Services;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// 桌面组件窗口（P5）。骨架与盒子窗口同一套：摘系统标题栏、挂桌面层（owner）、
/// 手动增量拖动 + 吸附、收起/展开、位置存库（DIP，与 WPF 版互通）。
/// 内容按 WidgetKind 分支：Glance（日期时间+统计）、Todo（待办）、QuickNote（便签）、
/// SystemMonitor（CPU/内存/网络）。File / Weather / Music 尚未移植（见 docs/28 §11.4）。
/// </summary>
public sealed partial class WidgetWindow : Window
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

    private readonly WidgetRecord _widget;
    private bool _shellReady;
    private double _scale = 1.0;

    private bool _dragging;
    private POINT _dragStartCursor;
    private global::Windows.Graphics.PointInt32 _dragStartPos;

    private bool _collapsed;
    private int _expandedHeight;

    // 内容用到的计时器（秒级刷新 / 性能采样）
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private PerfSampler? _perfGrid;

    public string WidgetId => _widget.Id;

    public WidgetWindow(WidgetRecord widget)
    {
        _widget = widget;
        App.Log($"WidgetWindow ctor: begin（{widget.Name}/{widget.Kind}）");
        InitializeComponent();
        Title = widget.Name;
        TitleText.Text = widget.Name;

        CloseButton.Click += (_, _) => HideWidget();
        CollapseButton.Click += (_, _) => ToggleCollapse();
        WireDrag();
        BuildContent();

        _tick.Tick += (_, _) => TickContent();
        _tick.Start();

        Activated += OnFirstActivated;
        Closed += (_, _) =>
        {
            _tick.Stop();
            _perfGrid?.Dispose();
            App.WidgetWindows.Remove(this);
            App.Log($"WidgetWindow[{_widget.Name}] Closed");
        };
    }

    // ---------- 窗口骨架 ----------

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_shellReady) return;
        _shellReady = true;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            var style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_CAPTION & ~WS_SYSMENU & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            var attached = DesktopLayer.AttachOwner(hwnd);
            _scale = GetDpiForWindow(hwnd) / 96.0;

            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                (int)Math.Round(_widget.X * _scale), (int)Math.Round(_widget.Y * _scale),
                (int)Math.Round(_widget.Width * _scale), (int)Math.Round(_widget.Height * _scale)));
            _expandedHeight = AppWindow.Size.Height;
            if (_widget.IsCollapsed) SetCollapsed(true, persist: false);

            ApplyLook();
            App.Log($"WidgetWindow[{_widget.Name}] shell: owner={attached}, scale={_scale:0.00}, " +
                    $"pos={AppWindow.Position.X},{AppWindow.Position.Y}, size={AppWindow.Size.Width}x{AppWindow.Size.Height}");
        }
        catch (Exception ex) { App.Log($"WidgetWindow[{_widget.Name}] shell FAILED: " + ex); }
    }

    public void ApplyLook()
    {
        try
        {
            Root.RequestedTheme = App.CurrentElementTheme;
            Root.CornerRadius = new CornerRadius(
                _widget.StyleScope == StyleScope.Override && _widget.CornerRadius > 0
                    ? _widget.CornerRadius : App.CurrentCorner);
        }
        catch (Exception ex) { App.Log("组件外观应用失败：" + ex.Message); }
    }

    // ---------- 拖动 ----------

    private void WireDrag()
    {
        TitleBarGrid.PointerPressed += OnDragStart;
        TitleBarGrid.PointerMoved += OnDragMove;
        TitleBarGrid.PointerReleased += OnDragEnd;
        TitleBarGrid.PointerCaptureLost += (_, _) => _dragging = false;
    }

    private void OnDragStart(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TitleBarGrid);
        if (!point.Properties.IsLeftButtonPressed) return;
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
        var snap = WindowSnap.Snap(
            new WindowSnap.Rect(nextX, nextY, AppWindow.Size.Width, AppWindow.Size.Height), SnapTargets());
        AppWindow.Move(new global::Windows.Graphics.PointInt32((int)snap.X, (int)snap.Y));
    }

    private void OnDragEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TitleBarGrid.ReleasePointerCapture(e.Pointer);
        _widget.X = Math.Round(AppWindow.Position.X / _scale, 1);
        _widget.Y = Math.Round(AppWindow.Position.Y / _scale, 1);
        try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("保存组件位置失败：" + ex.Message); }
    }

    private List<WindowSnap.Rect> SnapTargets()
    {
        var list = new List<WindowSnap.Rect>();
        foreach (var box in App.BoxWindows)
            list.Add(new WindowSnap.Rect(box.AppWindow.Position.X, box.AppWindow.Position.Y,
                box.AppWindow.Size.Width, box.AppWindow.Size.Height));
        foreach (var w in App.WidgetWindows)
        {
            if (ReferenceEquals(w, this)) continue;
            list.Add(new WindowSnap.Rect(w.AppWindow.Position.X, w.AppWindow.Position.Y,
                w.AppWindow.Size.Width, w.AppWindow.Size.Height));
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

    // ---------- 收起 / 展开 ----------

    private void ToggleCollapse() => SetCollapsed(!_collapsed, persist: true);

    private void SetCollapsed(bool collapsed, bool persist)
    {
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        var titleHeight = (int)Math.Round(34 * _scale);
        if (collapsed)
        {
            _expandedHeight = AppWindow.Size.Height;
            if (presenter is not null) presenter.PreferredMinimumHeight = 0;
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, titleHeight));
        }
        else
        {
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width,
                Math.Max(_expandedHeight, titleHeight * 3)));
        }
        _collapsed = collapsed;
        _widget.IsCollapsed = collapsed;
        CollapseButton.Content = collapsed ? "▢" : "—";
        if (persist)
        {
            try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("保存收起状态失败：" + ex.Message); }
        }
        App.Log($"WidgetWindow[{_widget.Name}] collapse -> {collapsed}（height={AppWindow.Size.Height}）");
    }

    private void HideWidget()
    {
        _widget.IsVisible = false;
        try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("隐藏组件保存失败：" + ex.Message); }
        App.NotifyWidgetsChanged();
        Close();
    }

    // ---------- 内容 ----------

    private void BuildContent()
    {
        Content.Children.Clear();
        try
        {
            switch (_widget.Kind)
            {
                case WidgetKind.Glance: BuildGlance(); break;
                case WidgetKind.Todo: BuildTodo(); break;
                case WidgetKind.QuickNote: BuildNotes(); break;
                case WidgetKind.SystemMonitor: BuildSystemMonitor(); break;
                default:
                    Content.Children.Add(Text($"{_widget.Kind} 组件还没移植到新界面", 12, secondary: true));
                    Content.Children.Add(Text("当前可用：概览、待办、便签、系统监视", 11.5, secondary: true));
                    break;
            }
            App.Log($"WidgetWindow[{_widget.Name}] content built: {_widget.Kind}");
        }
        catch (Exception ex)
        {
            App.Log($"WidgetWindow[{_widget.Name}] build content FAILED: {ex}");
            Content.Children.Add(Text("内容加载失败：" + ex.Message, 12));
        }
    }

    private void TickContent()
    {
        try
        {
            switch (_widget.Kind)
            {
                case WidgetKind.Glance: UpdateGlanceClock(); break;
                case WidgetKind.SystemMonitor: UpdatePerf(); break;
            }
        }
        catch (Exception ex) { App.Log("组件刷新失败：" + ex.Message); }
    }

    // --- Glance：日期 / 时间 / 统计 ---
    private TextBlock? _clockText;
    private TextBlock? _statText;

    private void BuildGlance()
    {
        Content.Children.Add(Text(DateTime.Now.ToString("M月d日 dddd"), 14, bold: true));
        _clockText = Text(DateTime.Now.ToString("HH:mm:ss"), 30, bold: true);
        Content.Children.Add(_clockText);
        _statText = Text("", 12, secondary: true);
        Content.Children.Add(_statText);
        UpdateGlanceClock();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(SmallButton("收纳盒", () => App.Main?.SelectPage("boxes")));
        row.Children.Add(SmallButton("数据", () => App.Main?.SelectPage("data")));
        Content.Children.Add(row);
    }

    private void UpdateGlanceClock()
    {
        if (_clockText is not null) _clockText.Text = DateTime.Now.ToString("HH:mm:ss");
        if (_statText is not null && App.Repo is not null)
        {
            var boxes = App.Repo.Boxes();
            var open = App.Repo.Todos().Count(t => !t.IsDone);
            _statText.Text = $"待办 {open} 项 · 收纳盒 {boxes.Count} 个（{boxes.Count(b => !b.IsHidden)} 个显示中）";
        }
    }

    // --- Todo：待办清单 ---
    private void BuildTodo()
    {
        var input = new TextBox
        {
            PlaceholderText = "输入待办，回车添加…",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            var title = input.Text.Trim();
            if (title.Length == 0) return;
            App.Repo?.Upsert(new TodoRecord { Title = title, CreatedAt = DateTimeOffset.Now });
            App.Log($"组件：新增待办「{title}」");
            BuildTodo();
        };
        Content.Children.Add(input);

        if (App.Repo is null) { Content.Children.Add(Text("数据库未就绪", 12, secondary: true)); return; }
        var todos = App.Repo.Todos().Where(t => !t.IsDone).OrderByDescending(t => t.Priority).ToList();
        if (todos.Count == 0) Content.Children.Add(Text("没有未完成的待办。", 12, secondary: true));

        foreach (var todo in todos)
        {
            var captured = todo;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 2) };
            var check = new CheckBox { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            check.Checked += (_, _) =>
            {
                captured.IsDone = true;
                captured.CompletedAt = DateTimeOffset.Now;
                App.Repo?.Upsert(captured);
                App.Log($"组件：完成待办「{captured.Title}」");
                BuildTodo();
            };
            var text = new TextBlock
            {
                Text = captured.Title,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = Math.Max(120, _widget.Width - 90)
            };
            if (captured.Priority == 2) text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            row.Children.Add(check);
            row.Children.Add(text);
            Content.Children.Add(row);
        }

        var done = App.Repo.Todos().Count(t => t.IsDone);
        Content.Children.Add(Text($"已完成 {done} 项", 11, secondary: true));
    }

    // --- QuickNote：便签 ---
    private void BuildNotes()
    {
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        var input = new TextBox { PlaceholderText = "新便签标题，回车添加…", FontSize = 12, Width = 180 };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            var title = input.Text.Trim();
            if (title.Length == 0) return;
            App.Repo?.Upsert(new NoteRecord { Title = title, Body = "", UpdatedAt = DateTimeOffset.Now });
            App.Log($"组件：新增便签「{title}」");
            BuildNotes();
        };
        addRow.Children.Add(input);
        Content.Children.Add(addRow);

        if (App.Repo is null) { Content.Children.Add(Text("数据库未就绪", 12, secondary: true)); return; }
        var notes = App.Repo.Notes().Take(20).ToList();
        if (notes.Count == 0) Content.Children.Add(Text("还没有便签。上面输入标题回车即可新建。", 12, secondary: true));

        foreach (var note in notes)
        {
            var captured = note;
            var card = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = string.IsNullOrWhiteSpace(captured.Title) ? "（无标题）" : captured.Title,
                                        FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(captured.Body) ? "（空白便签）" : captured.Body,
                                        FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxLines = 3,
                                        TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            Pages.PageKit.SetThemedBrush(card, "DnSubtleBg", b => card.Background = b);
            Pages.PageKit.SetThemedBrush(card, "DnCardStroke", b => card.BorderBrush = b);
            Content.Children.Add(card);
        }
    }

    // --- SystemMonitor：CPU / 内存 / 网络 ---
    private TextBlock? _cpuText, _memText, _netText;

    private void BuildSystemMonitor()
    {
        _cpuText = Text("CPU —", 13, bold: true);
        _memText = Text("内存 —", 12, secondary: true);
        _netText = Text("网络 —", 12, secondary: true);
        Content.Children.Add(_cpuText);
        Content.Children.Add(_memText);
        Content.Children.Add(_netText);

        _perfGrid = new PerfSampler(SystemMetrics.Read, SystemMetrics.ReadGpu) { IntervalMs = 1500 };
        _perfGrid.Start();
        App.Log("组件：性能采样已启动");
    }

    private void UpdatePerf()
    {
        var s = _perfGrid?.Latest;
        if (s is null) return;
        if (_cpuText is not null) _cpuText.Text = $"CPU {s.CpuPercent:0}%";
        if (_memText is not null)
            _memText.Text = $"内存 {s.MemoryPercent:0}%（{s.MemoryUsedBytes / 1024 / 1024 / 1024.0:0.0} / {s.MemoryTotalBytes / 1024 / 1024 / 1024.0:0.0} GB）";
        if (_netText is not null)
            _netText.Text = $"网络 ↑ {FormatRate(s.NetUpBytesPerSec)}  ↓ {FormatRate(s.NetDownBytesPerSec)}";
    }

    private static string FormatRate(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:0} KB/s";
        return $"{bytesPerSec / 1024 / 1024:0.0} MB/s";
    }

    // ---------- 小工具 ----------

    private static TextBlock Text(string text, double size, bool bold = false, bool secondary = false) => new()
    {
        Text = text,
        FontSize = size,
        Opacity = secondary ? 0.68 : 1.0,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
    };

    private static Button SmallButton(string text, Action onClick)
    {
        var b = new Button { Content = text, FontSize = 11.5, Padding = new Thickness(10, 3, 10, 3), MinWidth = 0 };
        b.Click += (_, _) => onClick();
        return b;
    }
}
