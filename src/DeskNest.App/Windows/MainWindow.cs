using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        Title = "桌巢 DeskNest";
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
    }

    private UIElement BuildTopBar()
    {
        var grid = new Grid { Margin = new Thickness(24, 14, 24, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = new TextBox
        {
            FontSize = 12.5,
            Padding = new Thickness(14, 8, 14, 8),
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Theme.Current.CardBrush,
            Foreground = Theme.Current.ForegroundBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Current.BorderBrush,
            ToolTip = "搜索设置项：例如 开机 / 透明度 / 统计 / 热键 / 桌宠 / 出图"
        };
        searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) SearchSettings(searchBox.Text.Trim());
        };
        Grid.SetColumn(searchBox, 0);

        var quick = new StackPanel { Orientation = Orientation.Horizontal };
        quick.Children.Add(UiKit.Btn("快捷搜索", () => (Application.Current as App)?.ShowSearch(), true));
        quick.Children.Add(UiKit.Btn("一键整理", OpenOrganizeWizard));
        Grid.SetColumn(quick, 1);

        grid.Children.Add(searchBox);
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
            (new[] { "comfy", "出图", "生成图片", "画图" }, "comfy")
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
        _pages["overview"] = ("概览", "🏠", OverviewPage.Build);
        _pages["boxes"] = ("收纳盒", "🗂", BoxesPage.Build);
        _pages["organize"] = ("整理与规则", "🧹", OrganizePage.Build);
        _pages["widgets"] = ("桌面组件", "🧩", WidgetsPage.Build);
        _pages["appearance"] = ("外观与主题", "🎨", AppearancePage.Build);
        _pages["modules"] = ("模块", "🔌", ModulesPage.Build);
        _pages["data"] = ("数据与备份", "💾", DataPage.Build);
        _pages["about"] = ("关于", "ℹ", AboutPage.Build);

        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.ComfyUi))
            _pages["comfy"] = ("AI 出图（ComfyUI）", "🖼", ComfyUiPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.Pet))
            _pages["pet"] = ("桌宠", "🐾", PetPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PerfMonitor))
            _pages["perf"] = ("性能监视", "📈", PerfPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.UsageStats))
            _pages["usage"] = ("使用统计", "⏱", UsagePage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.ApiPanel))
            _pages["api"] = ("API 面板", "💰", ApiPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.PowerSchedule))
            _pages["power"] = ("计划关机", "⏻", PowerPage.Build);
        if (AppServices.Modules.Loaded.ContainsKey(ModuleIds.AiBridge))
            _pages["ai"] = ("AI 接口", "🔗", AiPage.Build);
    }

    private void BuildNav()
    {
        _nav.Children.Clear();
        foreach (var (key, value) in _pages)
        {
            var target = key;
            _nav.Children.Add(UiKit.NavButton(value.Glyph, value.Title, key == _current, () => NavigateTo(target)));
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
        try
        {
            _contentHost.Child = UiKit.Scroll(entry.Build(this));
        }
        catch (Exception ex)
        {
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
}
