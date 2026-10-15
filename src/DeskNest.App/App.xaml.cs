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

        // 首次运行或用户没勾"静默启动"时，启动就把主窗口显示出来；
        // 之前写死了"引导完成后永不显示"，与设置里的「静默启动」开关自相矛盾（用户会以为程序没启动）。
        var onboardingDone = AppServices.Settings.Get("onboarding.done", false);
        var silentStart = AppServices.Settings.Get("app.silentStart", false);
        if (!onboardingDone || !silentStart) ShowMain();
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
        try
        {
            Log.Info($"ShowMain 开始（page={page ?? "-"}，已有窗口={_mainWindow is not null}）");
            _mainWindow ??= new MainWindow();
            Log.Info("MainWindow 已创建");
            if (page is not null) _mainWindow.NavigateTo(page);
            _mainWindow.Show();
            Log.Info($"MainWindow.Show 完成（IsVisible={_mainWindow.IsVisible}，句柄={new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle}）");
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("打开主窗口失败：" + ex);
            Ui.Notifications.Toast("打开主窗口失败：" + ex.Message, 8);
        }
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
