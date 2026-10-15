using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Pages;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Modules;

namespace DeskNest.App.Windows;

/// <summary>
/// 主窗口（管理台）：左侧图标导航 + 顶部搜索 + 右侧卡片式设置页。
/// 观感参考 DeskBox 的设置页：每行 = 图标 + 标题 + 说明 + 右侧控件，留白充足。
/// </summary>
public sealed class MainWindow : Window
{
    private readonly StackPanel _nav;
    private readonly Border _contentHost;
    private readonly Dictionary<string, (string Title, string Glyph, Func<MainWindow, UIElement> Build)> _pages = new();
    private string _current = "overview";

    /// <summary>最近一次页面构建的错误（自动化冒烟测试用；正常为 null）。</summary>
    public string? LastPageError { get; private set; }

    /// <summary>所有已注册页面的键（自动化测试用）。</summary>
    public IReadOnlyCollection<string> PageKeys => _pages.Keys;

    /// <summary>导航里显示的标题（自动化测试用：必须无重复）。</summary>
    public IReadOnlyList<string> PageTitles => _pages.Values.Select(v => v.Title).ToList();

    public MainWindow()
    {
        Title = "桌巢 DeskNest";
        Icon = AppIcon.WindowIcon;
        Width = 1080;
        Height = 740;
        MinWidth = 860;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Current.BackgroundBrush;
        Foreground = Theme.Current.ForegroundBrush;

        RegisterPages();

        _nav = new StackPanel { Margin = new Thickness(8, 4, 8, 12) };
        _contentHost = new Border { Padding = new Thickness(28, 10, 28, 20) };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navRoot = new DockPanel { Background = Theme.Current.CardBrush };
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 16, 14) };
        logo.Children.Add(new TextBlock
        {
            Text = "▦",
            FontSize = 18,
            Foreground = Theme.Current.AccentBrush,
            Margin = new Thickness(0, 0, 8, 0)
        });
        logo.Children.Add(new TextBlock { Text = "桌巢", FontSize = 14, Foreground = Theme.Current.ForegroundBrush });
        DockPanel.SetDock(logo, Dock.Top);
        navRoot.Children.Add(logo);
        navRoot.Children.Add(new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var divider = new Border { Width = 1, Background = Theme.Current.BorderBrush };
        var right = new DockPanel();
        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        right.Children.Add(topBar);
        right.Children.Add(_contentHost);

        Grid.SetColumn(navRoot, 0);
        Grid.SetColumn(divider, 0);
        Grid.SetColumn(right, 1);
        root.Children.Add(navRoot);
        root.Children.Add(divider);
        root.Children.Add(right);
        Content = root;

        BuildNav();
        NavigateTo("overview");

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

    private UIElement BuildTopBar()
    {
        var grid = new Grid { Margin = new Thickness(24, 14, 24, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = UiKit.InputWithPlaceholder("搜索设置…（开机 / 透明度 / 统计 / 热键）", _ => { }, 320);
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

        var quick = new StackPanel { Orientation = Orientation.Horizontal };
        quick.Children.Add(UiKit.Btn("快捷搜索", () => (Application.Current as App)?.ShowSearch(), true));
        quick.Children.Add(UiKit.Btn("一键整理", OpenOrganizeWizard));
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

        AddPage("overview", "概览", Icons.Home, OverviewPage.Build);
        AddPage("boxes", "收纳盒", Icons.Folder, BoxesPage.Build);
        AddPage("organize", "整理与规则", Icons.Organize, OrganizePage.Build);
        AddPage("widgets", "桌面组件", Icons.Widgets, WidgetsPage.Build);
        AddPage("appearance", "外观与主题", Icons.Appearance, AppearancePage.Build);
        AddPage("modules", "模块", Icons.Modules, ModulesPage.Build);
        AddPage("data", "数据与备份", Icons.Data, DataPage.Build);
        AddPage("about", "关于", Icons.About, AboutPage.Build);

        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.Pet))
            AddPage("pet", "桌宠", Icons.Pet, PetPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PerfMonitor))
            AddPage("perf", "性能监视", Icons.Performance, PerfPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.UsageStats))
            AddPage("usage", "使用统计", Icons.Usage, UsagePage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.ApiPanel))
            AddPage("api", "API 面板", Icons.Api, ApiPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PowerSchedule))
            AddPage("power", "计划关机", Icons.Power, PowerPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.AiBridge))
            AddPage("ai", "AI 接口", Icons.Ai, AiPage.Build);
    }

    private void BuildNav()
    {
        _nav.Children.Clear();
        string? lastGroup = null;
        var groups = new Dictionary<string, string>
        {
            ["overview"] = "通用",
            ["boxes"] = "通用",
            ["organize"] = "通用",
            ["widgets"] = "通用",
            ["appearance"] = "外观",
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

            _nav.Children.Add(UiKit.NavGroup(group, lastGroup is null ? 4 : 16));
            lastGroup = group;
            foreach (var (key, value) in members)
            {
                var target = key;
                _nav.Children.Add(UiKit.NavButton(value.Glyph, value.Title, key == _current, () => NavigateTo(target)));
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
        BuildNav();
    }

    public void OpenOrganizeWizard()
    {
        NavigateTo("organize");
        OrganizePage.ShowWizard(this);
    }

    public void RefreshCurrent() => NavigateTo(_current);

    private void OnThemeChanged()
    {
        try
        {
            Theme.ApplyToShell(this);
            BuildNav();
            NavigateTo(_current);
        }
        catch (Exception ex)
        {
            Log.Warn("把主题应用到主窗口失败：" + ex.Message);
        }
    }
}
