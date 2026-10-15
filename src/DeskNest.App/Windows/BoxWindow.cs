using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Services;
using DeskNest.Native.Win32;

namespace DeskNest.App.Windows;

/// <summary>
/// 桌面收纳盒窗口：挂在桌面层（壁纸之上、图标之下），支持拖入、缩放、卷起、自动隐藏、
/// 组内标签切换、单独样式覆盖（两级样式）。
/// </summary>
public sealed partial class BoxWindow : Window
{
    private readonly Border _root;
    private readonly StackPanel _titleBar;
    private readonly WrapPanel _iconPanel;
    private readonly StackPanel _listPanel;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status;
    private readonly StackPanel _tabStrip;
    private readonly TextBlock _titleText;
    private BoxRecord _box;
    private bool _attachedToDesktop;
    private bool _persisting;
    private DateTime _lastPersist = DateTime.MinValue;

    public string BoxId => _box.Id;
    public BoxRecord Box => _box;

    public BoxWindow(BoxRecord box)
    {
        _box = box;

        WindowStyle = WindowStyle.None;
        Icon = AppIcon.WindowIcon;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;   // 无边框窗口没有系统缩放边框，缩放交给 WindowDrag 的手柄
        MinWidth = 160;
        MinHeight = 96;
        Width = Math.Max(160, box.Width);
        Height = Math.Max(96, box.Height);
        Left = box.MarginX;
        Top = box.MarginY;
        Title = box.Name;

        _tabStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 6, 0) };
        _titleText = UiKit.Text(box.Name, 13, bold: true);

        _titleBar = new StackPanel { Orientation = Orientation.Horizontal, Height = 30, Cursor = Cursors.SizeAll };
        _titleBar.Children.Add(new TextBlock
        {
            Text = "▦",
            FontSize = 12,
            Margin = new Thickness(8, 6, 6, 0),
            Foreground = Theme.Desktop.SecondaryBrush
        });
        _titleBar.Children.Add(_titleText);
        _titleBar.Children.Add(_tabStrip);
        _titleBar.Children.Add(BuildHeaderButtons());

        _iconPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 6, 6) };
        _listPanel = new StackPanel { Margin = new Thickness(4, 0, 4, 6) };
        _status = UiKit.Text("", 11, secondary: true);

        var content = new StackPanel();
        content.Children.Add(_iconPanel);
        content.Children.Add(_listPanel);
        _scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var bottom = new Border { Padding = new Thickness(8, 2, 8, 4), Child = _status };

        var dock = new DockPanel();
        DockPanel.SetDock(_titleBar, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(_titleBar);
        dock.Children.Add(bottom);
        dock.Children.Add(_scroll);

        _root = new Border
        {
            CornerRadius = new CornerRadius(box.CornerRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Desktop.BorderBrush,
            Effect = UiKit.SoftShadow(),
            Child = dock
        };
        Content = WindowDrag.WrapWithResizeGrips(_root, this, () => _box.LockSize, MinWidth, MinHeight, PersistGeometryNow);
        _titleBar.ToolTip = "按住标题栏拖动盒子；右下角或任意边拖动可调整大小";

        // 右键盒子任意空白处 = 盒子菜单（以前只有标题栏的 ⋯ 按钮能打开，是个交互缺口）
        var boxMenu = new System.Windows.Controls.ContextMenu();
        boxMenu.Opened += (_, _) => BuildBoxContextMenu(boxMenu);
        _root.ContextMenu = boxMenu;

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _root.BorderBrush = Theme.Desktop.BorderBrush;
        Drop += OnDrop;

        MouseEnter += (_, _) => { if (_box.AutoHide) _root.Opacity = 1.0; };
        MouseLeave += (_, _) => { if (_box.AutoHide) _root.Opacity = 0.35; };

        // 拖动自己实现：DragMove() 对挂在 WorkerW 下的子窗口不可靠
        WindowDrag.EnableMove(this, _titleBar, () => _box.LockPosition, PersistGeometryNow);
        _titleBar.MouseLeftButtonDown += TitleBar_MouseDown;
        MouseWheel += (_, e) =>
        {
            if (_box.GroupId is not null && AppServices.Settings.Get("group.wheelSwitch", true))
                SwitchGroup(-Math.Sign(e.Delta));
            else if (Keyboard.Modifiers == ModifierKeys.Control)
                ChangeIconSize(Math.Sign(e.Delta) * 2);
        };

        LocationChanged += (_, _) => PersistGeometry();
        SizeChanged += (_, _) => PersistGeometry();
        SourceInitialized += (_, _) => OnSourceInitialized();

        ApplyLook();
        ReloadItems();
        RefreshGroupTabsPublic();

        Theme.Changed += ApplyLook;
        Closed += (_, _) => Theme.Changed -= ApplyLook;
    }

    private void OnSourceInitialized()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _attachedToDesktop = DesktopLayer.Attach(hwnd);
        if (!_attachedToDesktop)
        {
            Topmost = AppServices.Settings.Get("box.fallbackTopmost", true);
            Log.Warn("未能挂到桌面层，已降级为普通窗口");
        }
    }

    private static Brush MakeBackground(BoxRecord box)
    {
        var t = Theme.ResolveDesktop(box.Material, box.MaterialIntensity, box.Opacity, box.CornerRadius,
            box.StyleScope == StyleScope.Inherit);
        var bg = t.Background;
        var alpha = (byte)(Math.Clamp(t.Opacity, 0.25, 1.0) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, bg.R, bg.G, bg.B));
        brush.Freeze();
        return brush;
    }

    private StackPanel BuildHeaderButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 2, 0) };
        panel.Children.Add(HeaderButton("＋", "添加文件（也可以直接把文件拖进来）", AddFilesByDialog));
        panel.Children.Add(HeaderButton("⋯", "更多", ShowBoxMenu));
        panel.Children.Add(HeaderButton("⌄", "卷起 / 展开", () =>
        {
            _box.RolledUp = !_box.RolledUp;
            AppServices.Repo.Upsert(_box);
            ApplyLook();
        }));
        panel.Children.Add(HeaderButton("✕", "隐藏（托盘里可再显示）", () =>
        {
            _box.IsHidden = true;
            AppServices.Repo.Upsert(_box);
            Hide();
        }));
        return panel;
    }

    private Button HeaderButton(string glyph, string tip, Action action)
    {
        var b = new Button
        {
            Content = glyph,
            Width = 22,
            Height = 22,
            FontSize = 12,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Theme.Desktop.SecondaryBrush,
            Cursor = Cursors.Hand,
            ToolTip = tip
        };
        b.Style = ControlStyles.IconButton();   // 不套样式会露出 WPF 默认按钮的悬停底色
        b.Click += (_, e) => { action(); e.Handled = true; };
        return b;
    }

    public void ApplyLook()
    {
        var t = Theme.ResolveDesktop(_box.Material, _box.MaterialIntensity, _box.Opacity, _box.CornerRadius,
            _box.StyleScope == StyleScope.Inherit);
        _root.CornerRadius = new CornerRadius(t.CornerRadius);
        _root.Background = MakeBackground(_box);
        _root.BorderBrush = Theme.Desktop.BorderBrush;
        _root.Opacity = _box.AutoHide ? 0.35 : 1.0;
        _scroll.Visibility = _box.RolledUp ? Visibility.Collapsed : Visibility.Visible;
        _iconPanel.Visibility = _box.ViewMode == "Icon" ? Visibility.Visible : Visibility.Collapsed;
        _listPanel.Visibility = _box.ViewMode == "Icon" ? Visibility.Collapsed : Visibility.Visible;
        _titleText.Text = _box.Name;
    }

    public void ChangeIconSize(int delta)
    {
        _box.IconSize = Math.Clamp(_box.IconSize + delta, 18, 72);
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    private void PersistGeometry()
    {
        if (_persisting) return;
        // 拖动/缩放时 LocationChanged 高频触发，这里限流（每 250ms 最多落盘一次），避免每移动 1px 就写一次数据库
        if (DateTime.UtcNow - _lastPersist < TimeSpan.FromMilliseconds(250)) return;
        _persisting = true;
        try
        {
            if (_box.LockPosition)
            {
                Left = _box.MarginX;
                Top = _box.MarginY;
            }
            else
            {
                _box.MarginX = Left;
                _box.MarginY = Top;
            }
            if (!_box.LockSize)
            {
                _box.Width = Width;
                _box.Height = Height;
            }
            AppServices.Repo.Upsert(_box);
        }
        catch { }
        finally { _persisting = false; }
    }

    /// <summary>拖动/缩放结束后强制落盘一次（配合限流）。
    public void PersistGeometryNow()
    {
        _lastPersist = DateTime.MinValue;
        PersistGeometry();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _box.RolledUp = !_box.RolledUp;
            AppServices.Repo.Upsert(_box);
            ApplyLook();
            e.Handled = true;
            return;
        }
    }
}
