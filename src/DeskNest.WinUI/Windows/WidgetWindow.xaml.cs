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
        WireMenu();
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

    // ---------- 右键菜单 ----------

    private void WireMenu()
    {
        // 「绑定盒子」只对文件组件有意义，其它类型藏起来（别给不存在的功能留入口）
        MenuBind.Visibility = _widget.Kind == WidgetKind.File ? Visibility.Visible : Visibility.Collapsed;

        MenuBind.Click += async (_, _) => await BindBoxAsync();
        MenuResetPos.Click += (_, _) => ResetPosition();
        MenuRename.Click += async (_, _) => await RenameAsync();
        MenuClose.Click += (_, _) =>
        {
            App.Log($"组件[{_widget.Name}] 右键关闭");
            HideWidget();
        };

        var itemCount = (Root.ContextFlyout as MenuFlyout)?.Items.Count ?? 0;
        App.Log($"组件[{_widget.Name}] 菜单就绪：{itemCount} 项，绑定={MenuBind.Visibility}");
    }

    /// <summary>绑定到某个收纳盒（文件组件用）。绑定信息写库，重启还在。</summary>
    private async Task BindBoxAsync()
    {
        if (App.Repo is null) return;
        var boxes = App.Repo.Boxes();
        if (boxes.Count == 0)
        {
            await new ContentDialog
            {
                Title = "还没有收纳盒",
                Content = "先去「收纳盒」页建一个，再回来绑定。",
                CloseButtonText = "知道了",
                XamlRoot = Root.XamlRoot
            }.ShowAsync();
            return;
        }

        var list = new ListView { Height = 220, SelectionMode = ListViewSelectionMode.Single };
        foreach (var box in boxes)
        {
            var count = 0;
            try { count = App.Repo.Items(box.Id, box.Kind == BoxKind.Mapped).Count; } catch { }
            var item = new ListViewItem
            {
                Content = $"{box.Name}    （{count} 项）",
                Tag = box,
                IsSelected = box.Id == _widget.BoxId
            };
            list.Items.Add(item);
        }
        if (list.SelectedItem is null && list.Items.Count > 0) list.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            Title = "绑定盒子",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "这个组件要显示哪个盒子里的文件？", FontSize = 12.5, Opacity = 0.75 },
                    list
                }
            },
            PrimaryButtonText = "绑定",
            CloseButtonText = "取消",
            XamlRoot = Root.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (list.SelectedItem is not ListViewItem selected || selected.Tag is not BoxRecord box2) return;

        _widget.BoxId = box2.Id;
        _widget.Name = box2.Name;
        TitleText.Text = box2.Name;
        Title = box2.Name;
        try { App.Repo.Upsert(_widget); } catch (Exception ex) { App.Log("保存绑定的盒子失败：" + ex.Message); }
        App.Log($"组件[{_widget.Name}] 绑定盒子 -> {box2.Name}#{box2.Id}");
        BuildFileWidget();
        App.NotifyWidgetsChanged();
    }

    /// <summary>回到默认位置：主屏工作区水平居中、靠上 80 DIP。位置写库。</summary>
    private void ResetPosition()
    {
        try
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            if (scale <= 0) scale = 1.0;
            var width = AppWindow.Size.Width;
            var height = AppWindow.Size.Height;

            var x = 200.0;
            var y = 140.0;
            if (Monitors.Primary() is { } screen)
            {
                x = screen.WorkX + (screen.WorkWidth - width) / 2.0;
                y = screen.WorkY + 80 * scale;
            }
            AppWindow.Move(new global::Windows.Graphics.PointInt32((int)x, (int)y));

            _widget.X = Math.Round(AppWindow.Position.X / scale, 1);
            _widget.Y = Math.Round(AppWindow.Position.Y / scale, 1);
            try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("保存重置位置失败：" + ex.Message); }
            App.Log($"组件[{_widget.Name}] 重置位置 -> {_widget.X},{_widget.Y} DIP");
        }
        catch (Exception ex) { App.Log("重置位置失败：" + ex); }
    }

    private async Task RenameAsync()
    {
        var input = new TextBox { Text = _widget.Name, PlaceholderText = "组件名称" };
        var dialog = new ContentDialog
        {
            Title = "重命名组件",
            Content = input,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = Root.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = input.Text.Trim();
        if (name.Length == 0 || name == _widget.Name) return;
        _widget.Name = name;
        Title = name;
        TitleText.Text = name;
        try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("保存组件名失败：" + ex.Message); }
        App.NotifyWidgetsChanged();
        App.Log($"组件重命名 -> {name}");
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
        if (CollapseButton.Content is FontIcon glyph) glyph.Glyph = collapsed ? "\uE70E" : "\uE70D";
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
                case WidgetKind.File: BuildFileWidget(); break;
                case WidgetKind.Weather: BuildWeather(); break;
                case WidgetKind.Music: BuildMusic(); break;
                default:
                    Content.Children.Add(Text($"{_widget.Kind} 组件还没移植到新界面", 12, secondary: true));
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
        Content.Children.Clear();
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
        Content.Children.Clear();
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
        Content.Children.Clear();
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
        Content.Children.Clear();
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

    // --- File：绑定某个收纳盒的文件列表 ---
    private void BuildFileWidget()
    {
        Content.Children.Clear();
        if (App.Repo is null) { Content.Children.Add(Text("数据库未就绪", 12, secondary: true)); return; }

        // 没绑盒子：列出盒子让用户选一个（WPF 版是拖进去绑的，这里点选更直接）
        var box = _widget.BoxId is { } id ? App.Repo.Box(id) : null;
        if (box is null)
        {
            Content.Children.Add(Text("选一个盒子来显示：", 12, secondary: true));
            foreach (var candidate in App.Repo.Boxes().Take(8))
            {
                var captured = candidate;
                var b = SmallButton(captured.Name, () =>
                {
                    _widget.BoxId = captured.Id;
                    _widget.Name = captured.Name;
                    TitleText.Text = captured.Name;
                    try { App.Repo?.Upsert(_widget); } catch (Exception ex) { App.Log("组件绑定盒子失败：" + ex.Message); }
                    BuildFileWidget();
                });
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Content = captured.Name;
                Content.Children.Add(b);
            }
            return;
        }

        var head = new Grid();
        head.Children.Add(Text(box.Name, 12.5, bold: true));
        var refresh = SmallButton("刷新", BuildFileWidget);
        refresh.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(refresh);
        Content.Children.Add(head);

        var items = App.Repo.Items(box.Id, box.Kind == BoxKind.Mapped).Take(40).ToList();
        if (items.Count == 0) Content.Children.Add(Text("这个盒子还是空的", 11.5, secondary: true));

        foreach (var item in items)
        {
            var captured = item;
            var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var path = captured.StoredPath ?? captured.SourcePath ?? "";
            var icon = string.IsNullOrEmpty(path) ? null : IconLoader.Get(path, 24);
            if (icon is not null)
                row.Children.Add(new Image { Source = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
            else
                row.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 14, Opacity = 0.7 });

            var name = Text(captured.DisplayName, 12);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxLines = 1;
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            // 双击打开
            row.DoubleTapped += (_, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { App.Log("组件打开文件失败：" + ex.Message); }
                e.Handled = true;
            };
            row.Tag = path;
            Content.Children.Add(row);
        }
        Content.Children.Add(Text($"共 {items.Count} 项 · 双击打开", 10.5, secondary: true));
    }

    // --- Weather：Open-Meteo（免费、无需 Key） ---
    private void BuildWeather()
    {
        Content.Children.Clear();
        var city = App.Settings?.Get("weather.city", "深圳") ?? "深圳";
        Content.Children.Add(Text("天气", 15, bold: true));
        Content.Children.Add(Text($"城市：{city}", 11.5, secondary: true));

        var summary = App.Settings?.Get("weather.summary", "") ?? "";
        Content.Children.Add(Text(string.IsNullOrWhiteSpace(summary) ? "还没有数据，点「刷新」获取" : summary, 12));

        var updated = App.Settings?.Get("weather.updated", "") ?? "";
        if (!string.IsNullOrWhiteSpace(updated)) Content.Children.Add(Text(updated, 10.5, secondary: true));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(SmallButton("刷新", async () => await RefreshWeatherAsync()));
        row.Children.Add(SmallButton("设置城市", async () => await SetWeatherCityAsync()));
        Content.Children.Add(row);
    }

    private async Task SetWeatherCityAsync()
    {
        var input = new TextBox { Text = App.Settings?.Get("weather.city", "深圳") ?? "深圳", PlaceholderText = "城市名称（中文即可）" };
        var dialog = new ContentDialog
        {
            Title = "天气城市",
            Content = input,
            PrimaryButtonText = "保存并刷新",
            CloseButtonText = "取消",
            XamlRoot = Root.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var city = input.Text.Trim();
        if (city.Length == 0) return;
        App.Settings?.Set("weather.city", city);
        App.Log($"组件：天气城市 -> {city}");
        BuildWeather();
        await RefreshWeatherAsync();
    }

    /// <summary>刷新天气（点「刷新」、设完城市、或自验脚本会调）。</summary>
    public async Task RefreshWeatherAsync()
    {
        try
        {
            var city = App.Settings?.Get("weather.city", "深圳") ?? "深圳";
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var geo = await http.GetStringAsync(
                "https://geocoding-api.open-meteo.com/v1/search?count=1&language=zh&name=" + Uri.EscapeDataString(city));
            using var doc = System.Text.Json.JsonDocument.Parse(geo);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                App.Settings?.Set("weather.summary", "找不到这个城市，换个名字试试");
                BuildWeather();
                return;
            }
            var lat = results[0].GetProperty("latitude").GetDouble();
            var lon = results[0].GetProperty("longitude").GetDouble();
            var forecast = await http.GetStringAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                "&current=temperature_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min" +
                "&timezone=auto&forecast_days=3");
            using var fdoc = System.Text.Json.JsonDocument.Parse(forecast);
            var cur = fdoc.RootElement.GetProperty("current");
            var temp = cur.GetProperty("temperature_2m").GetDouble();
            var wind = cur.GetProperty("wind_speed_10m").GetDouble();
            var daily = fdoc.RootElement.GetProperty("daily");
            var maxs = daily.GetProperty("temperature_2m_max");
            var mins = daily.GetProperty("temperature_2m_min");
            var times = daily.GetProperty("time");

            var summary = $"{temp:0.#}°C · 风速 {wind:0.#} km/h\n";
            for (var i = 0; i < Math.Min(3, times.GetArrayLength()); i++)
            {
                var day = times[i].GetString() ?? "";
                summary += $"{day}  {mins[i].GetDouble():0.#}~{maxs[i].GetDouble():0.#}°C\n";
            }
            App.Settings?.Set("weather.summary", summary.TrimEnd());
            App.Settings?.Set("weather.updated", "更新于 " + DateTime.Now.ToString("MM-dd HH:mm"));
            App.Log($"组件：天气已更新（{city} {temp:0.#}°C）");
            BuildWeather();
        }
        catch (Exception ex)
        {
            App.Settings?.Set("weather.summary", "更新失败：" + ex.Message);
            App.Log("天气更新失败：" + ex);
            BuildWeather();
        }
    }

    // --- Music：媒体键 ---
    private void BuildMusic()
    {
        Content.Children.Clear();
        Content.Children.Add(Text("媒体控制", 15, bold: true));
        Content.Children.Add(Text("控制当前播放器，不需要任何权限", 11, secondary: true));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
        row.Children.Add(SmallButton("⏮", () => SendMediaKey(0xB1)));
        row.Children.Add(SmallButton("⏯", () => SendMediaKey(0xB3)));
        row.Children.Add(SmallButton("⏭", () => SendMediaKey(0xB0)));
        Content.Children.Add(row);
        Content.Children.Add(Text("提示：显示封面/进度要接系统媒体会话（SMTC），后续再做。", 10.5, secondary: true));
    }

    private static void SendMediaKey(int vk)
    {
        try
        {
            MediaKeys.Send(vk);
            App.Log($"组件：媒体键 0x{vk:X}");
        }
        catch (Exception ex) { App.Log("媒体键失败：" + ex.Message); }
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
