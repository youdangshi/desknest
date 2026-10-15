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
    private System.Windows.Controls.ContextMenu? _menu;
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
        _menu = boxMenu;
        // 键盘也能打开盒子菜单：不用右键、不用先精确定位鼠标，键盘用户与触控板用户都受益
        PreviewKeyDown += OnBoxKeyDown;

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _root.BorderBrush = Theme.Desktop.BorderBrush;
        Drop += OnDrop;

        MouseEnter += (_, _) => { if (_box.AutoHide) _root.Opacity = 1.0; };
        MouseLeave += (_, _) => { if (_box.AutoHide) _root.Opacity = 0.35; };
        MouseEnter += (_, _) => UpdateTitleBarVisibility();
        MouseLeave += (_, _) => UpdateTitleBarVisibility();

        // 标题栏隐藏时没有拖动热区，用 Alt+按住盒子任意位置来移动（菜单里会提示这一点）
        _root.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsTitleBarHidden) return;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (WindowMove.BeginDrag(hwnd)) PersistGeometryNow();
            }
            catch (Exception ex) { Log.Warn("Alt 拖动失败：" + ex.Message); }
            e.Handled = true;
        };

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
        ApplyTitleStyle();
    }

    /// <summary>
    /// 四种标题栏样式：标准（默认 30 高）/ 紧凑（22 高、字号更小）/ 悬浮（鼠标移上去才显形）/
    /// 隐藏（整条收起，靠 Alt+拖动移动）。
    /// </summary>
    private void ApplyTitleStyle()
    {
        switch (TitleStyleKey)
        {
            case "Compact":
                _titleBar.Height = 22;
                _titleText.FontSize = 11.5;
                break;
            case "Floating":
            case "Hidden":
                _titleBar.Height = 30;
                _titleText.FontSize = 13;
                break;
            default:
                _titleBar.Height = 30;
                _titleText.FontSize = 13;
                break;
        }
        UpdateTitleBarVisibility();
    }

    /// <summary>标题栏的显示/隐藏与悬浮透明度。悬浮时仍然保留热区，所以照样能拖。</summary>
    private void UpdateTitleBarVisibility()
    {
        switch (TitleStyleKey)
        {
            case "Hidden":
                _titleBar.Visibility = Visibility.Collapsed;
                _titleBar.Opacity = 0;
                break;
            case "Floating":
                _titleBar.Visibility = Visibility.Visible;
                _titleBar.Opacity = IsMouseOver ? 1.0 : 0.0;
                break;
            default:
                _titleBar.Visibility = Visibility.Visible;
                _titleBar.Opacity = 1.0;
                break;
        }
    }

    public void ChangeIconSize(int delta)
    {
        _box.IconSize = Math.Clamp(_box.IconSize + delta, 18, 72);
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    /// <summary>
    /// 把盒子搬到指定位置（多显示器页的"全部移到此屏"用）。
    /// 走 Left/Top 而不是 Win32 SetWindowPos，是为了让 LocationChanged 统一触发落盘。
    /// </summary>
    public void MoveTo(double left, double top)
    {
        Left = Math.Round(left);
        Top = Math.Round(top);
        PersistGeometryNow();
    }

    /// <summary>Shift+F10 或键盘"菜单键"打开盒子菜单（不依赖右键）。</summary>
    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        var isMenuKey = e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        if (!isMenuKey) return;
        OpenContextMenu();
        e.Handled = true;
    }

    /// <summary>
    /// 打开盒子菜单（键盘路径与截图模式共用）。
    /// expandSubmenu 传父项标题时，顺手把它那一级子菜单也展开（截图/演示用）。
    /// </summary>
    public void OpenContextMenu(string? expandSubmenu = null)
    {
        if (_menu is null) return;
        _menu.PlacementTarget = _root;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        _menu.IsOpen = true;
        // 诊断用：弹出菜单是独立窗口，位置/尺寸只有打开后才知道（截图模式曾拍不到它）
        Log.Info($"盒子菜单已打开：IsOpen={_menu.IsOpen}，项数={_menu.Items.Count}，" +
                 $"尺寸={_menu.ActualWidth:0}x{_menu.ActualHeight:0}，" +
                 $"源={(_menu.Parent is null ? "无" : _menu.Parent.GetType().Name)}");
        LogMenuWindow();
        if (string.IsNullOrWhiteSpace(expandSubmenu)) return;
        // 子菜单必须等菜单自己完成布局之后才肯展开（立刻设会被 WPF 回滚成 false）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var parent = _menu?.Items.OfType<System.Windows.Controls.MenuItem>()
                .FirstOrDefault(m => string.Equals(m.Header as string, expandSubmenu, StringComparison.Ordinal));
            if (parent is null) return;
            parent.IsSubmenuOpen = true;
            Log.Info($"子菜单 {expandSubmenu}：IsSubmenuOpen={parent.IsSubmenuOpen}，子项={parent.Items.Count}");
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>把弹出菜单那个真实 HWND 的位置写进日志（用来确认它到底开在哪儿）。</summary>
    private void LogMenuWindow()
    {
        try
        {
            if (_menu is null) return;
            if (System.Windows.PresentationSource.FromVisual(_menu) is System.Windows.Interop.HwndSource src
                && DeskNest.Native.Win32.WindowShot.Bounds(src.Handle) is { } r)
                Log.Info($"菜单窗口句柄={src.Handle}，位置={r.X},{r.Y}，尺寸={r.W}x{r.H}");
            else
                Log.Warn("菜单窗口句柄取不到（弹出源未建立）");
        }
        catch (Exception ex) { Log.Warn("读取菜单窗口失败：" + ex.Message); }
    }

    /// <summary>收起盒子菜单。</summary>
    public void CloseContextMenu()
    {
        if (_menu is not null) _menu.IsOpen = false;
    }

    /// <summary>
    /// 拍文档截图前的整备：把"自动隐藏"造成的整体变淡恢复成正常不透明度，
    /// 否则截图里盒子是灰蒙蒙的、看起来像渲染坏了。拍完调用 <see cref="ApplyLook"/> 还原。
    /// </summary>
    public void PrepareForShot() => _root.Opacity = 1.0;

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
