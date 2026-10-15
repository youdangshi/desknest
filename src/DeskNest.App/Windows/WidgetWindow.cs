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

    public WidgetWindow(WidgetRecord widget)
    {
        _widget = widget;
        WindowStyle = WindowStyle.None;
        Icon = AppIcon.WindowIcon;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
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
            Foreground = Theme.Current.SecondaryBrush
        });
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(widget.Name) ? KindName(widget.Kind) : widget.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Current.ForegroundBrush,
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
        title.MouseLeftButtonDown += (_, _) => DragMove();

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
            BorderBrush = Theme.Current.BorderBrush,
            Effect = UiKit.SoftShadow(),
            Child = dock
        };
        Content = _root;

        LocationChanged += (_, _) => Persist();
        SizeChanged += (_, _) => Persist();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (!Native.Win32.DesktopLayer.Attach(hwnd)) Topmost = AppServices.Settings.Get("widget.fallbackTopmost", true);
        };
        Closed += (_, _) => _timer?.Stop();

        ApplyLook();
        BuildContent();

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
            Foreground = Theme.Current.SecondaryBrush,
            Cursor = Cursors.Hand
        };
        b.Click += (_, e) => { action(); e.Handled = true; };
        return b;
    }

    public void ApplyLook()
    {
        var t = Theme.Resolve(_widget.Material, _widget.MaterialIntensity, _widget.Opacity,
            _widget.CornerRadius, _widget.StyleScope == StyleScope.Inherit);
        _root.CornerRadius = new CornerRadius(t.CornerRadius);
        var bg = t.Background;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(t.Opacity, 0.25, 1) * 255), bg.R, bg.G, bg.B));
        brush.Freeze();
        _root.Background = brush;
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
                BuildTodo();
                break;
            case WidgetKind.Glance:
                BuildGlance();
                break;
        }
    }
}
