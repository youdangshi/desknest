using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Pages;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Modules;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;

namespace DeskNest.App.Windows;

/// <summary>
/// 主窗口（管理台）：左侧图标导航 + 顶部搜索 + 右侧卡片式设置页。
/// 观感参考 DeskBox 的设置页：每行 = 图标 + 标题 + 说明 + 右侧控件，留白充足。
/// </summary>
public sealed class MainWindow : FluentWindow
{
    private readonly NavigationView _nav;
    private readonly Border _contentHost;
    private readonly Dictionary<string, (string Title, string Glyph, Func<MainWindow, UIElement> Build)> _pages = new();
    private string _current = "overview";

    /// <summary>最近一次页面构建的错误（自动化冒烟测试用；正常为 null）。</summary>
    public string? LastPageError { get; private set; }

    /// <summary>所有已注册页面的键（自动化测试用）。</summary>
    public IReadOnlyCollection<string> PageKeys => _pages.Keys;

    /// <summary>导航里显示的标题（自动化测试用：必须无重复）。</summary>
    public IReadOnlyList<string> PageTitles => _pages.Values.Select(v => v.Title).ToList();

    /// <summary>取某个页面的显示标题（截图命名、调试日志用）。</summary>
    public string? TitleOf(string key) => _pages.TryGetValue(key, out var page) ? page.Title : null;

    public MainWindow()
    {
        Log.Info("MainWindow 构造开始");
        Title = "桌巢 DeskNest";
        Icon = AppIcon.WindowIcon;
        Width = 1080;
        Height = 740;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Current.BackgroundBrush;
        Foreground = Theme.Current.ForegroundBrush;

        // WPF-UI：Fluent 主题 + Mica/亚克力背景 + 系统级标题栏（观感对齐 DeskBox）
        FluentTheme.Apply(Theme.Shell.Scheme, Theme.Shell.Material);
        WindowBackdropType = FluentTheme.BackdropFor(Theme.Shell.Material);
        ExtendsContentIntoTitleBar = true;

        // 点右上角 ✕ 只是收进托盘（托盘应用的标准做法）。真正退出走托盘菜单「退出桌巢」，
        // 否则窗口被销毁后既不能重新显示，也会让托盘里的入口全部报错。
        Closing += (_, e) =>
        {
            if (DeskNest.App.App.IsShuttingDown) return;
            e.Cancel = true;
            Hide();
            Ui.Notifications.Toast("桌巢已收进托盘（托盘右键可以再打开）", 3);
        };

        RegisterPages();

        _contentHost = new Border { Padding = new Thickness(28, 10, 28, 20) };

        var right = new DockPanel();
        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        right.Children.Add(topBar);
        right.Children.Add(_contentHost);

        // WPF-UI 的真 NavigationView：自带汉堡按钮、折叠成图标栏、分组标题与页脚
        _nav = new NavigationView
        {
            // PaneTitle 留空：品牌信息已经由下面的 PaneHeader 承担，两处都写会重复一行"桌巢"
            PaneTitle = "",
            PaneHeader = BuildPaneHeader(),
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneToggleVisible = true,
            OpenPaneLength = 232,
            CompactPaneLength = 48,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            AlwaysShowHeader = false,
            IsTopSeparatorVisible = false,
            IsFooterSeparatorVisible = true,
            Header = null,
            FrameMargin = new Thickness(0)
        };

        // NavigationView 是个纯导航外壳（基类 Control，没有 Content）。它内部用
        // NavigationViewContentPresenter（一个 Frame）承载右侧内容，但该成员是私有的，
        // 所以等模板套用后再从可视化树里取出来挂上我们自己的页面宿主。
        _nav.Loaded += (_, _) =>
        {
            var presenter = FindChild<Wpf.Ui.Controls.NavigationViewContentPresenter>(_nav);
            if (presenter is null)
            {
                Log.Error("找不到 NavigationViewContentPresenter，右侧内容无法挂载");
                return;
            }
            presenter.Background = Brushes.Transparent;   // 让 Mica/亚克力透上来
            presenter.BorderThickness = new Thickness(0);
            presenter.Padding = new Thickness(0);
            presenter.NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden;
            presenter.Content = right;
        };

        // FluentWindow 开了 ExtendsContentIntoTitleBar 之后，标题栏由我们自己放进布局里
        var titleBar = new Wpf.Ui.Controls.TitleBar
        {
            Title = "桌巢 DeskNest",
            ShowMinimize = true,
            ShowMaximize = true,
            ShowClose = true,
            CanMaximize = true
        };
        var shell = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        shell.Children.Add(titleBar);
        shell.Children.Add(_nav);
        Content = shell;

        BuildNav();
        NavigateTo("overview");
        Log.Info("MainWindow 构造完成");

        // 运行时切换主题：外壳与当前页必须一起重画（v1.0.0 只重画了页面内容，才出现"深底深字"）
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) => Theme.Changed -= OnThemeChanged;

        SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                DwmEffects.PreferRoundCorners(hwnd);
                var material = AppServices.Settings.Get("theme.material", "Acrylic");
                if (string.Equals(material, "Mica", StringComparison.OrdinalIgnoreCase))
                {
                    // 让 Mica 透出来：窗口自身底色改成几乎全透明，失败时下面的 Set 会被跳过，界面仍是自绘底色
                    if (DwmEffects.TrySetBackdrop(hwnd, DwmEffects.Backdrop.Mica))
                        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                }
            }
            catch { }
        };
    }

    /// <summary>在可视化树里找第一个指定类型的子孙元素。</summary>
    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindChild<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    /// <summary>导航栏顶部的品牌头（图标 + 名字），折叠时自动隐藏以免只剩半行字。</summary>
    private static UIElement BuildPaneHeader()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 0, 6) };
        row.Children.Add(new System.Windows.Controls.Image
        {
            Source = AppIcon.WindowIcon,
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = "桌巢 DeskNest",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private UIElement BuildTopBar()
    {
        var grid = new Grid { Margin = new Thickness(24, 14, 24, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = UiKit.InputWithPlaceholder("搜索设置…（颜色 / 透明度 / 备份 / 更新）", _ => { }, 320);
        searchBox.HorizontalAlignment = HorizontalAlignment.Center;
        searchBox.TextAlignment = TextAlignment.Left;
        searchBox.Margin = new Thickness(0);
        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) SearchSettings(searchBox.Text.Trim());
        };
        Grid.SetColumn(searchBox, 0);

        // 搜索框右侧放一个放大镜（DeskBox 的搜索框里也有），让"这是搜索"一眼可辨
        var searchWrap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        searchWrap.Children.Add(searchBox);
        var lens = Icons.Vector("search", 16, Theme.Current.TertiaryColor);
        lens.Margin = new Thickness(-28, 0, 0, 0);
        lens.IsHitTestVisible = false;
        searchWrap.Children.Add(lens);
        Grid.SetColumn(searchWrap, 0);

        // v2.0：顶栏只留一个真实动作（搜索/整理已下架，别留死按钮）
        var quick = new StackPanel { Orientation = Orientation.Horizontal };
        quick.Children.Add(UiKit.Btn("＋ 新建收纳盒", () => NavigateTo("boxes"), true));
        Grid.SetColumn(quick, 1);

        grid.Children.Add(searchWrap);
        grid.Children.Add(quick);
        return grid;
    }

    private void SearchSettings(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        foreach (var (key, value) in _pages)
        {
            if (value.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                NavigateTo(key);
                Ui.Notifications.Toast($"已定位到「{value.Title}」", 2);
                return;
            }
        }
        var map = new (string[] Keys, string Page)[]
        {
            (new[] { "开机", "启动", "自启" }, "overview"),
            (new[] { "透明度", "圆角", "材质", "主题", "配色", "背景" }, "appearance"),
            (new[] { "热键", "快捷键", "语言" }, "overview"),
            (new[] { "统计", "时长", "用量" }, "usage"),
            (new[] { "性能", "cpu", "内存", "gpu" }, "perf"),
            (new[] { "桌宠", "宠物" }, "pet"),
            (new[] { "关机", "重启", "睡眠" }, "power"),
            (new[] { "余额", "api", "key", "模型" }, "api"),
            (new[] { "备份", "恢复", "诊断" }, "data"),
            (new[] { "模块", "启用", "关闭" }, "modules"),
        };
        var lower = query.ToLowerInvariant();
        foreach (var (keys, page) in map)
        {
            if (keys.Any(k => lower.Contains(k)) && _pages.ContainsKey(page))
            {
                NavigateTo(page);
                Ui.Notifications.Toast($"已定位到「{_pages[page].Title}」", 2);
                return;
            }
        }
        Ui.Notifications.Toast("没找到对应设置，试试：统计 / 性能 / 桌宠 / 关机 / 备份 / 出图", 3);
    }

    private void RegisterPages()
    {
        // 注册页面：键和标题都去重，避免导航里出现两个同名项
        void AddPage(string key, string title, string icon, Func<MainWindow, UIElement> build)
        {
            if (_pages.ContainsKey(key))
            {
                Log.Warn($"页面键重复，已忽略：{key}");
                return;
            }
            if (_pages.Values.Any(v => v.Title == title))
            {
                Log.Warn($"页面标题重复，已忽略：{title}");
                return;
            }
            _pages[key] = (title, icon, build);
        }

        // ===== v2.0：只做收纳，导航收成 5 页 =====
        // 其余页面（整理 / 组件 / 多显示器 / 模块 / 桌宠 / 性能 / 统计 / API / 关机 / AI）
        // 只是**不注册**，页面类与代码都还在仓库里；补回来时把这些 AddPage 取消注释即可
        // （见 docs/19-后续开发计划-Backlog.md）。
        AddPage("overview", "概览", Icons.Home, OverviewPage.Build);
        AddPage("boxes", "收纳盒", Icons.Folder, BoxesPage.Build);
        AddPage("appearance", "外观与主题", Icons.Appearance, AppearancePage.Build);
        AddPage("data", "数据与备份", Icons.Data, DataPage.Build);
        AddPage("about", "关于", Icons.About, AboutPage.Build);

        // —— 以下为 v2.0 下架项（保留，勿删）——
        // AddPage("organize", "整理与规则", Icons.Organize, OrganizePage.Build);
        // AddPage("widgets", "桌面组件", Icons.Widgets, WidgetsPage.Build);
        // AddPage("displays", "多显示器", Icons.Monitor, DisplaysPage.Build);
        // AddPage("modules", "模块", Icons.Modules, ModulesPage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.Pet))       AddPage("pet", "桌宠", Icons.Pet, PetPage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PerfMonitor)) AddPage("perf", "性能监视", Icons.Performance, PerfPage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.UsageStats)) AddPage("usage", "使用统计", Icons.Usage, UsagePage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.ApiPanel))   AddPage("api", "API 面板", Icons.Api, ApiPage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PowerSchedule)) AddPage("power", "计划关机", Icons.Power, PowerPage.Build);
        // if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.AiBridge))   AddPage("ai", "AI 接口", Icons.Ai, AiPage.Build);
    }

    private void BuildNav()
    {
        _nav.MenuItems.Clear();
        _nav.FooterMenuItems.Clear();
        var groups = new Dictionary<string, string>
        {
            ["overview"] = "通用",
            ["boxes"] = "通用",
            ["organize"] = "通用",
            ["widgets"] = "通用",
            ["appearance"] = "外观",
            ["displays"] = "外观",
            ["modules"] = "功能",
            ["pet"] = "功能",
            ["perf"] = "功能",
            ["usage"] = "功能",
            ["api"] = "功能",
            ["power"] = "功能",
            ["ai"] = "功能",
            ["data"] = "维护",
            ["about"] = "维护"
        };
        // 按固定分组顺序输出（否则"维护"夹在中间会把"功能"拆成两段，出现两个同名分组标签）
        foreach (var group in new[] { "通用", "外观", "功能", "维护" })
        {
            var members = _pages.Where(p => (groups.TryGetValue(p.Key, out var g) ? g : "功能") == group).ToList();
            if (members.Count == 0) continue;

            var target = _nav.MenuItems;
            // 维护组放到页脚：日常几乎不点，不该占主列表的位置
            if (group == "维护") target = _nav.FooterMenuItems;
            else target.Add(UiKit.NavGroup(group, 12));

            foreach (var (key, value) in members)
            {
                var pageKey = key;
                var item = UiKit.NavButton(value.Glyph, value.Title, key == _current, () => NavigateTo(pageKey));
                item.ToolTip = value.Title;   // 折叠成图标栏时靠提示认出是哪个页面
                target.Add(item);
            }
        }
    }

    public void NavigateTo(string page)
    {
        if (!_pages.TryGetValue(page, out var entry))
        {
            Log.Warn("未知页面：" + page);
            return;
        }
        _current = page;
        LastPageError = null;
        // 切页写日志：验收时"访问了哪些页面、有没有失败"可以直接对日志核对
        Log.Info($"切到页面：{page}（{entry.Title}）");
        try
        {
            // 内容限制最大宽度并左对齐：宽屏下不会拉成一整行，阅读更舒服
            var body = new Border
            {
                MaxWidth = Tokens.ContentMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = entry.Build(this)
            };
            _contentHost.Child = UiKit.Scroll(body);
        }
        catch (Exception ex)
        {
            LastPageError = $"{page}: {ex.Message}";
            Log.Error($"页面构建失败 {page}：{ex.Message}");
            _contentHost.Child = UiKit.Col(
                UiKit.Text("这个页面加载失败：" + ex.Message, 13),
                UiKit.Btn("重试", () => NavigateTo(page)));
        }
        // 只同步选中态，不整表重建：重建会让导航丢掉滚动位置、也会闪一下
        SyncNavSelection();
    }

    /// <summary>把导航项的高亮切到当前页（折叠状态下也一致）。</summary>
    private void SyncNavSelection()
    {
        var allItems = _nav.MenuItems.Cast<object>().Concat(_nav.FooterMenuItems.Cast<object>());
        foreach (var item in allItems.OfType<NavigationViewItem>())
        {
            var isCurrent = item.Content is string label
                && _pages.TryGetValue(_current, out var page)
                && label == page.Title;
            if (item.IsActive != isCurrent) item.IsActive = isCurrent;
        }
    }

    public void OpenOrganizeWizard(bool modal = true)
    {
        NavigateTo("organize");
        OrganizePage.ShowWizard(this, modal);
    }

    public void RefreshCurrent() => NavigateTo(_current);

    /// <summary>
    /// 主窗口背景：只有"材质真的能用"时才透明（让 Mica/亚克力透上来），否则退回实色背景。
    /// Windows 10 上亚克力经常不生效，透明窗口会变成"整片发白、文字看不清"
    /// （用户 2026-10-17 反馈的截图就是这种状态）。
    /// </summary>
    public void ApplyShellBackground()
    {
        try
        {
            var opaque = AppServices.Settings.Get("theme.forceOpaqueShell", Environment.OSVersion.Version.Build < 22000);
            WindowBackdropType = opaque ? WindowBackdropType.None : FluentTheme.BackdropFor(Theme.Shell.Material);
            Background = opaque ? Theme.Shell.BackgroundBrush : System.Windows.Media.Brushes.Transparent;
            var bg = Background as System.Windows.Media.SolidColorBrush;
            Log.Info($"主窗口背景：{(opaque ? "不透明（材质未启用）" : "跟随材质 " + Theme.Shell.Material)}" +
                     (bg is null ? "（透明/材质）" : $" #{bg.Color.R:X2}{bg.Color.G:X2}{bg.Color.B:X2}"));
        }
        catch (Exception ex) { Log.Warn("设置主窗口背景失败：" + ex.Message); }
    }

    private Ui.ColorScheme? _appliedScheme;
    private Ui.MaterialKind? _appliedMaterial;

    private void OnThemeChanged()
    {
        try
        {
            // 只有"配色/材质"变化才重建页面。
            // 主题色也重建过一次，但那是每次拖动取色器都重建整页 —— 反而更闪、更卡，
            // 已回退；按钮跟随主题色的问题改用"只重刷按钮样式"解决（见 B10）。
            var appearanceChanged = _appliedScheme != Theme.Shell.Scheme || _appliedMaterial != Theme.Shell.Material;
            _appliedScheme = Theme.Shell.Scheme;
            _appliedMaterial = Theme.Shell.Material;
            FluentTheme.Apply(Theme.Shell.Scheme, Theme.Shell.Material);
            WindowBackdropType = FluentTheme.BackdropFor(Theme.Shell.Material);
            // 用 Mica/亚克力时不要把窗口刷成不透明色，否则会把材质盖住
            ApplyShellBackground();
            Foreground = Theme.Shell.ForegroundBrush;
            // 只有配色/材质真的变了才重建导航与页面。
            // 以前滑杆每动一下都重建整页 —— 这就是"调节格子参数好卡"的根因。
            if (appearanceChanged)
            {
                BuildNav();
                NavigateTo(_current);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("把主题应用到主窗口失败：" + ex.Message);
        }
    }
}
