using System.Windows;
using DeskNest.App.Host;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.App;

public partial class App : Application
{
    private Tray? _tray;
    private HotKeys? _hotKeys;
    private SearchPanel? _searchPanel;
    private MainWindow? _mainWindow;
    private readonly DesktopWindows _desktop = new();
    private bool _layerVisible = true;
    private DateTimeOffset? _autoOrganizePausedUntil;
    private System.Windows.Threading.DispatcherTimer? _autoOrganizeTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppServices.Initialize();

        _tray = new Tray(
            toggleLayer: ToggleLayer,
            showMain: () => ShowMain(),
            organize: () => ShowOrganizeWizard(),
            search: () => ShowSearch(),
            toggleIcons: () => ToggleDesktopIcons(),
            pauseAuto: () =>
            {
                _autoOrganizePausedUntil = DateTimeOffset.Now.AddHours(1);
                Ui.Notifications.Toast("自动整理已暂停 1 小时");
            },
            settings: () => ShowMain("modules"),
            exit: () => Shutdown());

        _hotKeys = new HotKeys();
        _hotKeys.SearchPressed += () => ShowSearch();
        _hotKeys.ToggleLayerPressed += ToggleLayer;
        _hotKeys.Install();

        Commands.OrganizeRequested = _ => ShowOrganizeWizard();
        Commands.ToggleLayerRequested = ToggleLayer;
        Commands.ToggleDesktopIconsRequested = ToggleDesktopIcons;
        Commands.ShowMainWindowRequested = () => ShowMain();

        _desktop.RestoreAll();
        if (AppServices.Repo.Pet().IsVisible && AppServices.Modules.IsEnabled(ModuleIds.Pet))
            _desktop.ShowPet();

        _autoOrganizeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _autoOrganizeTimer.Tick += (_, _) => AutoOrganizeTick();
        _autoOrganizeTimer.Start();

        if (!AppServices.Settings.Get("onboarding.done", false)) ShowMain();
        else Ui.Notifications.Toast("桌巢已在后台运行：Alt+Space 搜索，Alt+` 显示/隐藏桌面");
    }

    private void AutoOrganizeTick()
    {
        try
        {
            if (!AppServices.Settings.Get("organize.auto", false)) return;
            if (_autoOrganizePausedUntil is { } until && DateTimeOffset.Now < until) return;

            var minutes = AppServices.Settings.Get("organize.autoMinutes", 30);
            var last = AppServices.Settings.Get<DateTimeOffset?>("organize.lastRun", null);
            if (last is { } l && DateTimeOffset.Now - l < TimeSpan.FromMinutes(minutes)) return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var plan = AppServices.Organizer.BuildPlan(new OrganizeEngine.ScanOptions(
                desktop,
                IncludeShortcuts: AppServices.Settings.Get("organize.includeShortcuts", false),
                IncludeHidden: false,
                UseFolderMode: AppServices.Settings.Get("organize.folderMode", false)));
            if (plan.Items.Count == 0) return;

            var run = AppServices.Organizer.Execute(plan, "auto");
            AppServices.Settings.Set("organize.lastRun", DateTimeOffset.Now);
            Ui.Notifications.Toast($"自动整理完成：{run.MovedCount} 个文件", 8,
                action: () =>
                {
                    var (restored, failed) = AppServices.Organizer.Undo(run);
                    Ui.Notifications.Toast($"已撤销：还原 {restored} 项，失败 {failed} 项");
                });
        }
        catch (Exception ex) { Log.Warn("自动整理异常：" + ex.Message); }
    }

    public void ToggleLayer()
    {
        _layerVisible = !_layerVisible;
        _desktop.SetLayerVisible(_layerVisible);
        Ui.Notifications.Toast(_layerVisible ? "桌面层已显示" : "桌面层已隐藏（Alt+` 恢复）", 2);
    }

    public void ToggleDesktopIcons()
    {
        var hidden = AppServices.Settings.Get("desktop.iconsHidden", false);
        if (Native.Win32.DesktopIcons.SetVisible(hidden))
        {
            AppServices.Settings.Set("desktop.iconsHidden", !hidden);
            Ui.Notifications.Toast(!hidden ? "桌面图标已隐藏" : "桌面图标已显示", 2);
        }
        else
        {
            Ui.Notifications.Toast("切换桌面图标失败：找不到桌面图标列表");
        }
    }

    public void ShowSearch()
    {
        _searchPanel ??= new SearchPanel();
        _searchPanel.ShowPanel();
    }

    public void ShowMain(string? page = null)
    {
        _mainWindow ??= new MainWindow();
        if (page is not null) _mainWindow.NavigateTo(page);
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowOrganizeWizard()
    {
        ShowMain();
        _mainWindow?.OpenOrganizeWizard();
    }

    public DesktopWindows Desktop => _desktop;

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            _hotKeys?.Dispose();
            _autoOrganizeTimer?.Stop();
            _desktop.CloseAll();
            AppServices.Shutdown();
        }
        catch { }
        base.OnExit(e);
    }
}
