using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Windows;

/// <summary>桌面组件窗口：六种组件共用外壳（标题栏 + 内容区），可拖拽、缩放、折叠为胶囊。</summary>
public sealed partial class WidgetWindow : Window
{
    private readonly WidgetRecord _widget;
    private readonly Border _root;
    private readonly StackPanel _content = new();
    private readonly TextBlock _status = UiKit.Text("", 10.5, secondary: true);
    private readonly DispatcherTimer _timer;

    public string WidgetId => _widget.Id;

    /// <summary>本组件的数据记录（合并/多显示器统计等外部逻辑要用）。</summary>
    public WidgetRecord Record => _widget;

    public WidgetWindow(WidgetRecord widget)
    {
        _widget = widget;
        WindowStyle = WindowStyle.None;
        Icon = AppIcon.WindowIcon;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;   // 同上：缩放交给 WindowDrag 的手柄
        Width = widget.Width;
        Height = widget.Height;
        Left = widget.X;
        Top = widget.Y;
        MinWidth = 140;
        MinHeight = 90;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Height = 28, Cursor = Cursors.SizeAll };
        title.Children.Add(new TextBlock
        {
            Text = "▢",
            FontSize = 11,
            Margin = new Thickness(8, 5, 6, 0),
            Foreground = Theme.Desktop.SecondaryBrush
        });
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(widget.Name) ? KindName(widget.Kind) : widget.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Desktop.ForegroundBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var spacer = new Border { Width = 8 };
        title.Children.Add(spacer);
        title.Children.Add(HeaderButton("⋯", ShowMenu));
        title.Children.Add(HeaderButton("⌄", ToggleCollapse));
        title.Children.Add(HeaderButton("✕", () =>
        {
            _widget.IsVisible = false;
            AppServices.Repo.Upsert(_widget);
            Hide();
        }));
        WindowDrag.EnableMove(this, title, () => false, Persist);

        var dock = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        dock.Children.Add(title);
        _status.Margin = new Thickness(8, 0, 8, 4);
        dock.Children.Add(_status);
        var scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(8, 0, 6, 0)
        };
        dock.Children.Add(scroll);

        _root = new Border
        {
            CornerRadius = new CornerRadius(widget.CornerRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Desktop.BorderBrush,
            Effect = UiKit.SoftShadow(),
            Child = dock
        };
        Content = WindowDrag.WrapWithResizeGrips(_root, this, () => false, MinWidth, MinHeight, Persist);

        LocationChanged += (_, _) => Persist();
        SizeChanged += (_, _) => Persist();
        // 点一下就提到最上层（和收纳盒一致）：组件平时会被别的程序盖住
        PreviewMouseLeftButtonDown += (_, _) => RaiseToFront();
        PreviewMouseRightButtonDown += (_, _) => RaiseToFront();
        // 让组件里的输入框真的能打字（不抢焦点的窗口默认收不到键盘，见 DesktopInput 的说明）
        DesktopInput.EnableTyping(this);
        // 拖到另一个组件上就合并（和收纳盒一致）；组内用滚轮切换成员
        WindowDrag.EnableMove(this, _root, () => false, Persist,
            onDragging: OnDraggingForMerge, onDropped: OnDroppedForMerge);
        MouseWheel += (_, e) => SwitchGroup(-Math.Sign(e.Delta));
        SourceInitialized += (_, _) =>
        {
            // 和收纳盒一样：必须在第一次渲染之前挂载（挂晚了窗口会"看不见"）
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (!Native.Win32.DesktopLayer.Attach(hwnd))
            {
                // 与收纳盒一致：可激活的普通窗口（"不抢焦点"会让里面所有输入框都打不了字）
                Topmost = false;
            }
        };
        Closed += (_, _) => _timer?.Stop();

        ApplyLook();
        BuildContent();

        Theme.Changed += ApplyLook;
        Closed += (_, _) => Theme.Changed -= ApplyLook;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(widget.Kind == WidgetKind.SystemMonitor ? 1 : 5) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private static string KindName(WidgetKind kind) => kind switch
    {
        WidgetKind.Todo => "待办",
        WidgetKind.QuickNote => "随记",
        WidgetKind.Weather => "天气",
        WidgetKind.Music => "音乐",
        WidgetKind.SystemMonitor => "系统监视",
        WidgetKind.Glance => "概览",
        _ => "文件"
    };

    private Button HeaderButton(string glyph, Action action)
    {
        var b = new Button
        {
            Content = glyph,
            Width = 20,
            Height = 20,
            FontSize = 11,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 3, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Desktop.SecondaryBrush,
            Cursor = Cursors.Hand
        };
        b.Style = ControlStyles.IconButton();   // 同上：避免默认按钮外观
        b.Click += (_, e) => { action(); e.Handled = true; };
        return b;
    }

    public void ApplyLook()
    {
        var t = Theme.ResolveDesktop(_widget.Material, _widget.MaterialIntensity, _widget.Opacity,
            _widget.CornerRadius, _widget.StyleScope == StyleScope.Inherit);
        _root.CornerRadius = new CornerRadius(t.CornerRadius);
        var bg = t.Background;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(t.Opacity, 0.25, 1) * 255), bg.R, bg.G, bg.B));
        brush.Freeze();
        _root.Background = brush;
    }

    /// <summary>把本组件提到普通窗口最上层（不抢键盘焦点）。</summary>
    private void RaiseToFront()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.Win32.WindowMove.BringToTop(hwnd);
        }
        catch (Exception ex) { Core.Log.Warn("组件提到最前失败：" + ex.Message); }
    }

    // ---------- 组件合并（和收纳盒同一套规则） ----------

    private string? _mergeTargetId;

    private void SetDropHint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _root.BorderThickness = new Thickness(1);
            _root.BorderBrush = Theme.Desktop.BorderBrush;
            return;
        }
        _root.BorderThickness = new Thickness(2);
        _root.BorderBrush = Theme.Desktop.AccentBrush;
    }

    private BoxHit? FindMergeTarget(Point screenPoint)
    {
        if (App.Current is not App app) return null;
        var hits = new List<BoxHit>();
        foreach (var (id, win) in app.Desktop.Widgets)
        {
            if (ReferenceEquals(win, this) || !win.IsVisible) continue;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (WindowShot.Bounds(hwnd) is not { } rect) continue;
            hits.Add(new BoxHit(id, win.Record.Name, rect.X, rect.Y, rect.W, rect.H, win.Record.GroupId, true));
        }
        return BoxDropTarget.Pick((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y),
            hits, _widget.Id, _widget.GroupId);
    }

    private void OnDraggingForMerge(Point screenPoint)
    {
        var target = FindMergeTarget(screenPoint);
        var targetId = target?.Id;
        if (_mergeTargetId == targetId) return;
        if (_mergeTargetId is not null && (App.Current as App)?.Desktop.Widgets.TryGetValue(_mergeTargetId, out var previous) == true)
            previous!.SetDropHint(null);
        _mergeTargetId = targetId;
        if (target is null) { SetDropHint(null); return; }
        if ((App.Current as App)?.Desktop.Widgets.TryGetValue(target.Id, out var win) == true)
            win!.SetDropHint("松开即合并到此组件");
        SetDropHint($"松开合并到「{target.Name}」");
    }

    private void OnDroppedForMerge(Point screenPoint)
    {
        var target = FindMergeTarget(screenPoint);
        var previousId = _mergeTargetId;
        _mergeTargetId = null;
        SetDropHint(null);
        if (previousId is not null && (App.Current as App)?.Desktop.Widgets.TryGetValue(previousId, out var previous) == true)
            previous!.SetDropHint(null);
        if (target is null) return;

        try
        {
            if (App.Current is not App app) return;
            var keep = AppServices.Repo.Widgets().FirstOrDefault(w => w.Id == target.Id);
            if (keep is null) return;
            var group = AppServices.Boxes.MergeWidgets(keep, _widget);
            app.Desktop.ReloadBoxes();
            app.Desktop.SwitchGroupTo(group.Id, group.ActiveMemberId);
            Ui.Notifications.Toast($"已把「{_widget.Name}」并进「{group.Name}」，滚轮切换", 6);
        }
        catch (Exception ex)
        {
            Core.Log.Error($"组件合并失败：{ex}");
            Ui.Notifications.Toast("合并失败：" + ex.Message);
        }
    }

    /// <summary>组内滚轮切换成员。</summary>
    private void SwitchGroup(int direction)
    {
        if (_widget.GroupId is null) return;
        var group = AppServices.Repo.Groups().FirstOrDefault(g => g.Id == _widget.GroupId);
        if (group is null) return;
        var ids = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (ids.Count <= 1) return;
        var index = ids.IndexOf(_widget.Id);
        if (index < 0) return;
        var next = ids[(index + direction + ids.Count) % ids.Count];
        (App.Current as App)?.Desktop.SwitchGroupTo(group.Id, next);
    }

    private void Persist()
    {
        try
        {
            _widget.X = Left;
            _widget.Y = Top;
            _widget.Width = Width;
            _widget.Height = Height;
            AppServices.Repo.Upsert(_widget);
        }
        catch { }
    }

    private void ToggleCollapse()
    {
        _widget.IsCollapsed = !_widget.IsCollapsed;
        AppServices.Repo.Upsert(_widget);
        Height = _widget.IsCollapsed ? 32 : Math.Max(120, _widget.Height);
        _status.Visibility = _widget.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuAction("刷新", Refresh));
        menu.Items.Add(MenuAction(_widget.StyleScope == StyleScope.Inherit ? "单独设置样式" : "样式跟随全局", () =>
        {
            _widget.StyleScope = _widget.StyleScope == StyleScope.Inherit ? StyleScope.Override : StyleScope.Inherit;
            AppServices.Repo.Upsert(_widget);
            ApplyLook();
            Ui.Notifications.Toast(_widget.StyleScope == StyleScope.Inherit ? "已跟随全局样式" : "已改为单独样式", 3);
        }));
        menu.Items.Add(MenuAction("透明度 +", () => { _widget.Opacity = Math.Min(1, _widget.Opacity + 0.1); AppServices.Repo.Upsert(_widget); ApplyLook(); }));
        menu.Items.Add(MenuAction("透明度 -", () => { _widget.Opacity = Math.Max(0.3, _widget.Opacity - 0.1); AppServices.Repo.Upsert(_widget); ApplyLook(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("从桌面移除组件", () =>
        {
            AppServices.Repo.DeleteWidget(_widget.Id);
            Close();
            if (App.Current is App app) app.Desktop.NotifyWidgetClosed(_widget.Id);
        }));
        menu.IsOpen = true;
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    public void Refresh()
    {
        switch (_widget.Kind)
        {
            case WidgetKind.SystemMonitor:
                UpdateSystemMonitor();
                break;
            case WidgetKind.Todo:
                BuildTodoPage();
                break;
            case WidgetKind.Glance:
                BuildGlance();
                break;
        }
    }
}
