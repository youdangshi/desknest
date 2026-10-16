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
        _titleText.Effect = UiKit.LegibleTextShadow();   // 半透明卡片压在亮壁纸上时靠这层描边保 readable

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
        _status.Effect = UiKit.LegibleTextShadow();

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
        _titleBar.ToolTip = "按住标题栏、或盒子任意空白处都能拖动；右下角与四边可调整大小";

        // 右键盒子任意空白处 = 盒子菜单（以前只有标题栏的 ⋯ 按钮能打开，是个交互缺口）
        var boxMenu = new System.Windows.Controls.ContextMenu();
        boxMenu.Opened += (_, _) => BuildBoxContextMenu(boxMenu);
        _root.ContextMenu = boxMenu;
        _menu = boxMenu;
        // 键盘也能打开盒子菜单：不用右键、不用先精确定位鼠标，键盘用户与触控板用户都受益
        PreviewKeyDown += OnBoxKeyDown;

        AllowDrop = true;
        // 点一下就提到普通窗口最上层：桌面盒平时会被别的程序盖住（点不到也看不见），
        // 点击那一刻提上来，用户立刻看得到；之后点别的程序，那个程序自然又盖回来。
        PreviewMouseLeftButtonDown += (_, _) => RaiseToFront();
        PreviewMouseRightButtonDown += (_, _) => RaiseToFront();
        // 盒内（待办盒/随记盒）的输入框也要能打字
        DesktopInput.EnableTyping(this);
        // 双击盒子（标题栏或空白处）＝ 隐藏，和 DeskBox 一致（盒内文件的双击被文件项自己处理掉，不会冲突）
        _root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            OnBoxDoubleClick();
            e.Handled = true;
        };
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _root.BorderBrush = Theme.Desktop.BorderBrush;
        Drop += OnDrop;

        MouseEnter += (_, _) => { if (_box.AutoHide) _root.Opacity = 1.0; };
        MouseLeave += (_, _) => { if (_box.AutoHide) _root.Opacity = 0.35; };
        MouseEnter += (_, _) => UpdateTitleBarVisibility();
        MouseLeave += (_, _) => UpdateTitleBarVisibility();

        // 拖动自己实现：DragMove() 对挂在 WorkerW 下的子窗口不可靠
        // 标题栏只是"顺手"，盒子空白处同样可以拖——新盒子大半是空的，
        // 用户十有八九是抓着中间往下拖的（之前只有 30px 标题栏能拖，看起来就是"拖不动"）
        Action hintLocked = () => Ui.Notifications.Toast("位置已锁定：右键盒子 → 取消「锁定位置」即可拖动", 4);
        // 拖到别的盒子上就提示合并、松手即合并（菜单里的「添加格子 › 并入本组」也保留）
        WindowDrag.EnableMove(this, _titleBar, () => _box.LockPosition, PersistGeometryNow,
            onBlocked: hintLocked, onDragging: OnDraggingForMerge, onDropped: OnDroppedForMerge);
        WindowDrag.EnableMove(this, _root, () => _box.LockPosition, PersistGeometryNow, CanStartBodyDrag,
            onBlocked: hintLocked, onDragging: OnDraggingForMerge, onDropped: OnDroppedForMerge);
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
        // 时机很关键：必须在窗口**第一次渲染之前**做挂载与置顶。
        // 试过等布局完成（DispatcherPriority.Loaded）再挂，结果是窗口被重排到最上层却
        // 一个像素都不画（层叠窗口没有重新上传画面）——看不见也点不到，比不挂更糟。
        _attachedToDesktop = DesktopLayer.Attach(hwnd);
        if (!_attachedToDesktop)
        {
            // 常规路径：不挂桌面层，当**可激活的普通窗口**（不置顶、不进任务栏）。
            // 早先这里用的是"不抢焦点"（WS_EX_NOACTIVATE），但它有个致命副作用：
            // 这类窗口**永远拿不到键盘焦点**，于是待办/随记的输入框点了也打不了字（用户实测反馈）。
            // 改成可激活之后，点一下就置顶、输入框正常能打字，代价只是点它会把键盘焦点拿过来
            // ——这正是普通窗口的常规行为，也和用户要的"点击置顶"一致。
            Topmost = false;
        }

        // 把窗口的真实状态写进日志：拖动/点击出问题时，这几行能直接定位是哪一环不对
        try
        {
            Log.Info($"盒子窗口就绪：hwnd={hwnd}，挂桌面层={_attachedToDesktop}，" +
                     $"可用系统拖动={WindowMove.CanUseNativeDrag(hwnd)}，" +
                     $"DPI={VisualTreeHelper.GetDpi(this).DpiScaleX:0.00}，" +
                     $"位置={Left:0},{Top:0}，尺寸={Width:0}x{Height:0}，" +
                     $"锁定位置={_box.LockPosition}，锁定尺寸={_box.LockSize}");
        }
        catch (Exception ex) { Log.Warn("输出窗口诊断信息失败：" + ex.Message); }
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
                HideBox()));
        return panel;
    }

    private Button HeaderButton(string glyph, string tip, Action action)
    {
        // 标题栏按钮必须"任何壁纸下都看得见"：
        // ① 用完整前景色而不是次要色 ② 加一层描边/阴影 ③ 给一个极淡的自身底色当"芯片"，
        // 这样即使盒子卡片很透明（用户把透明度调到 0.3），按钮依然是清楚的一块。
        var foreground = Theme.Desktop.ForegroundBrush;
        var b = new Button
        {
            Content = glyph,
            Width = 22,
            Height = 22,
            FontSize = 12,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = new SolidColorBrush(Theme.Desktop.Foreground) { Opacity = 0.10 },
            BorderThickness = new Thickness(0),
            Foreground = foreground,
            Effect = UiKit.LegibleTextShadow(),
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
        // 标题栏给一条极淡的底：卡片透明（0.3）时，文字直接压在壁纸上会很糊；
        // 这条底让标题与按钮永远有一致的背景，配合 LegibleTextShadow 就能保证可读。
        _titleBar.Background = new SolidColorBrush(Theme.Desktop.Foreground) { Opacity = 0.08 };
        _status.Foreground = Theme.Desktop.ForegroundBrush;
        ApplyTitleStyle();
    }

    /// <summary>
    /// 四种标题栏样式：标准（默认 30 高）/ 紧凑（22 高、字号更小）/ 悬浮（鼠标移上去才显形）/
    /// 隐藏（整条收起，拖盒子任意处都能移动）。
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

    /// <summary>
    /// 窗口被系统按物理像素搬动之后，让 WPF 侧的 Left/Top（DIP）跟上真实位置。
    /// 不这么做的话，下一次落盘会把旧位置写回去，"拉回屏幕"就白做了。
    /// </summary>
    public void SyncPositionFromWindow()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (Native.Win32.WindowMove.PhysicalBounds(hwnd) is not { } rect) return;
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0) scale = 1;
            _box.MarginX = rect.X / scale;
            _box.MarginY = rect.Y / scale;
            AppServices.Repo.Upsert(_box);
        }
        catch (Exception ex) { Log.Warn("同步窗口位置失败：" + ex.Message); }
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

    /// <summary>
    /// 盒子空白处能不能起拖：点在盒子里的图标、列表项、按钮、标签页、滚动条上时不算——
    /// 那些地方各自有自己的交互（双击打开、右键菜单、点按钮），不能被"移动盒子"抢掉。
    /// </summary>
    private bool CanStartBodyDrag(object? source)
    {
        if (_box.LockPosition) return false;

        var node = source as System.Windows.DependencyObject;
        while (node is not null)
        {
            if (ReferenceEquals(node, _iconPanel) || ReferenceEquals(node, _listPanel) || ReferenceEquals(node, _tabStrip))
                return false;
            if (node is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.ScrollBar)
                return false;
            if (ReferenceEquals(node, _root)) break;
            node = VisualTreeHelper.GetParent(node);
        }
        return true;
    }

    // ---------- 拖拽合并 ----------

    private string? _mergeTargetId;

    /// <summary>把本盒子提到普通窗口最上层（不抢键盘焦点）。</summary>
    private void RaiseToFront()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.Win32.WindowMove.BringToTop(hwnd);
        }
        catch (Exception ex) { Log.Warn("盒子提到最前失败：" + ex.Message); }
    }

    /// <summary>
    /// 合并提示：高亮边框 + 把提示写在底部的状态行上（不新增浮层，小盒子里也不挤）。
    /// 传 null 恢复原样。
    /// </summary>
    public void SetDropHint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _root.BorderThickness = new Thickness(1);
            _root.BorderBrush = Theme.Desktop.BorderBrush;
            _status.Foreground = Theme.Desktop.SecondaryBrush;
            ReloadItems();          // 状态行文案交回给常规刷新
            return;
        }
        _root.BorderThickness = new Thickness(2);
        _root.BorderBrush = Theme.Desktop.AccentBrush;
        _status.Foreground = Theme.Desktop.AccentBrush;
        _status.Text = text;
    }

    /// <summary>拖动过程中找"松手会并到哪个盒子"。</summary>
    private BoxHit? FindMergeTarget(Point screenPoint)
    {
        if (App.Current is not App app) return null;
        var hits = new List<BoxHit>();
        foreach (var (id, win) in app.Desktop.Boxes)
        {
            if (ReferenceEquals(win, this) || !win.IsVisible) continue;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (WindowShot.Bounds(hwnd) is not { } rect) continue;
            hits.Add(new BoxHit(id, win.Box.Name, rect.X, rect.Y, rect.W, rect.H, win.Box.GroupId, true));
        }
        return BoxDropTarget.Pick((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y),
            hits, _box.Id, _box.GroupId);
    }

    private void OnDraggingForMerge(Point screenPoint)
    {
        var target = FindMergeTarget(screenPoint);
        var targetId = target?.Id;
        if (_mergeTargetId == targetId) return;   // 只在"换目标"时动界面，别每帧都刷

        if (_mergeTargetId is not null && (App.Current as App)?.Desktop.Boxes.TryGetValue(_mergeTargetId, out var previous) == true)
            previous!.SetDropHint(null);
        _mergeTargetId = targetId;

        if (target is null)
        {
            SetDropHint(null);
            return;
        }
        if ((App.Current as App)?.Desktop.Boxes.TryGetValue(target.Id, out var win) == true)
            win!.SetDropHint("松开即合并到此盒");
        SetDropHint($"松开合并到「{target.Name}」");
    }

    private void OnDroppedForMerge(Point screenPoint)
    {
        // 松手时再判定一次，避免"最后一次移动没触发"导致并错
        var target = FindMergeTarget(screenPoint);
        var previousId = _mergeTargetId;
        _mergeTargetId = null;
        SetDropHint(null);
        if (previousId is not null && (App.Current as App)?.Desktop.Boxes.TryGetValue(previousId, out var previous) == true)
            previous!.SetDropHint(null);
        if (target is null) return;

        try
        {
            if (App.Current is not App app) return;
            var keep = AppServices.Repo.Box(target.Id);
            if (keep is null) return;
            // 保留被拖到的那个盒子（用户是"往它身上放"），把自己并进它的组
            var group = AppServices.Boxes.MergeInto(keep, _box);
            app.Desktop.ReloadBoxes();
            app.Desktop.SwitchGroupTo(group.Id, group.ActiveMemberId);
            Ui.Notifications.Toast($"已把「{_box.Name}」并进「{group.Name}」，点标题栏标签或滚轮切换", 6);
        }
        catch (Exception ex)
        {
            Log.Error($"拖拽合并失败：{ex}");
            Ui.Notifications.Toast("合并失败：" + ex.Message);
        }
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
            OnBoxDoubleClick();
            e.Handled = true;
            return;
        }
    }

    /// <summary>
    /// 双击盒子的行为。默认**隐藏**（DeskBox 就是这么做的：双击把格子收起来，托盘里再叫回来）。
    /// 习惯"双击卷起"的人可以在右键菜单 →「更多操作 › 双击盒子时」里改。
    /// </summary>
    private void OnBoxDoubleClick()
    {
        var action = AppServices.Settings.Get("box.doubleClickAction", "Hide");
        if (string.Equals(action, "RollUp", StringComparison.OrdinalIgnoreCase))
        {
            _box.RolledUp = !_box.RolledUp;
            AppServices.Repo.Upsert(_box);
            ApplyLook();
            return;
        }
        if (string.Equals(action, "Nothing", StringComparison.OrdinalIgnoreCase)) return;
        HideBox();
    }

    /// <summary>隐藏盒子（可从托盘或撤销按钮叫回来）。</summary>
    public void HideBox()
    {
        _box.IsHidden = true;
        AppServices.Repo.Upsert(_box);
        Hide();
        Ui.Notifications.Toast($"已隐藏「{_box.Name}」，托盘图标里可以再显示", 6, action: () =>
        {
            _box.IsHidden = false;
            AppServices.Repo.Upsert(_box);
            Show();
        }, actionText: "撤销");
    }
}
