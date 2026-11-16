using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;

namespace DeskNest.WinUI;

public partial class App : Application
{
    internal static readonly string LogPath = BuildLogPath();

    /// <summary>与 WPF 版共用的数据库（WAL 模式，两进程同时读写没问题）。</summary>
    internal static DeskNest.Core.Data.Repository? Repo;

    /// <summary>底层库句柄（统计数据页的搜索索引行数等用）。</summary>
    internal static DeskNest.Core.Data.Db? Db;

    /// <summary>文件搬进/搬出盒子存储目录（与 WPF 版同一个实现，数据互通）。</summary>
    internal static DeskNest.Core.Services.FileOps? Files;

    /// <summary>盒子生命周期（删除盒子会先把文件还原回原位置）。</summary>
    internal static DeskNest.Core.Services.BoxService? Boxes;

    /// <summary>设置（与 WPF 版共用 settings.json：theme.desktop.* 等键互通）。</summary>
    internal static DeskNest.Core.Services.SettingsService? Settings;

    /// <summary>备份服务（数据页用）。</summary>
    internal static DeskNest.Core.Services.BackupService? Backup;

    /// <summary>主题键：配色方案（FollowSystem/Dark/Light/PureBlack/EyeCare）。</summary>
    internal const string KeyScheme = "theme.desktop.scheme";
    /// <summary>主题键：圆角（DIP）。</summary>
    internal const string KeyCorner = "theme.desktop.corner";

    /// <summary>读配色 → 对应的 XAML ElementTheme。</summary>
    internal static Microsoft.UI.Xaml.ElementTheme CurrentElementTheme
    {
        get
        {
            var scheme = Settings?.Get(KeyScheme, "FollowSystem") ?? "FollowSystem";
            return scheme switch
            {
                "Dark" or "PureBlack" => Microsoft.UI.Xaml.ElementTheme.Dark,
                "Light" or "EyeCare" => Microsoft.UI.Xaml.ElementTheme.Light,
                _ => Microsoft.UI.Xaml.ElementTheme.Default
            };
        }
    }

    internal static int CurrentCorner => Settings?.Get(KeyCorner, 12) ?? 12;

    /// <summary>主题键：材质（Acrylic / Mica / Solid）。</summary>
    internal const string KeyMaterial = "theme.desktop.material";
    /// <summary>主题键：窗口不透明度（0.35–1）。</summary>
    internal const string KeyOpacity = "theme.desktop.opacity";

    internal static string CurrentMaterial => Settings?.Get(KeyMaterial, Windows.WindowLook.MaterialGlass) ?? Windows.WindowLook.MaterialGlass;

    internal static double CurrentOpacity
    {
        get
        {
            var v = Settings?.Get(KeyOpacity, 0.95) ?? 0.95;
            return Math.Clamp(v, 0.35, 1.0);
        }
    }

    /// <summary>把当前外观应用到所有盒子窗口（配色 + 圆角）与主窗口。</summary>
    internal static void ApplyLookToAllBoxes()
    {
        try { Main?.ApplyLook(); } catch (Exception ex) { Log("应用主窗口外观失败：" + ex.Message); }
        foreach (var w in BoxWindows.ToList())
        {
            try { w.ApplyLook(); } catch (Exception ex) { Log("应用外观失败：" + ex.Message); }
        }
        foreach (var w in WidgetWindows.ToList())
        {
            try { w.ApplyLook(); } catch (Exception ex) { Log("应用组件外观失败：" + ex.Message); }
        }
    }

    /// <summary>当前开着的盒子窗口（拖动时互相吸附要用）。</summary>
    internal static readonly List<Windows.BoxWindow> BoxWindows = new();

    /// <summary>当前开着的桌面组件窗口。</summary>
    internal static readonly List<Windows.WidgetWindow> WidgetWindows = new();

    /// <summary>打开（或聚焦）一个桌面组件。同一个组件只开一个窗口；隐藏的先取消隐藏。</summary>
    internal static Windows.WidgetWindow OpenWidget(DeskNest.Core.WidgetRecord widget)
    {
        var existing = WidgetWindows.FirstOrDefault(w => w.WidgetId == widget.Id);
        if (existing is not null)
        {
            existing.Activate();
            return existing;
        }
        if (!widget.IsVisible)
        {
            widget.IsVisible = true;
            try { Repo?.Upsert(widget); } catch (Exception ex) { Log("组件取消隐藏失败：" + ex.Message); }
        }
        var win = new Windows.WidgetWindow(widget);
        WidgetWindows.Add(win);
        win.Activate();
        return win;
    }

    internal static void CloseWidget(string id)
    {
        var win = WidgetWindows.FirstOrDefault(w => w.WidgetId == id);
        win?.Close();
    }

    /// <summary>组件数据变了（新建/删除/显示隐藏）后通知页面刷新。</summary>
    internal static event Action? WidgetsChanged;

    internal static void NotifyWidgetsChanged()
    {
        try { WidgetsChanged?.Invoke(); } catch (Exception ex) { Log("WidgetsChanged 通知失败：" + ex.Message); }
    }

    /// <summary>
    /// 打开（或聚焦）一个盒子窗口。同一个盒子只开一个窗口；隐藏的盒子打开前先取消隐藏。
    /// </summary>
    internal static Windows.BoxWindow OpenBox(DeskNest.Core.BoxRecord box)
    {
        var existing = BoxWindows.FirstOrDefault(w => w.BoxId == box.Id);
        if (existing is not null)
        {
            existing.Activate();
            return existing;
        }
        if (box.IsHidden)
        {
            box.IsHidden = false;
            try { Repo?.Upsert(box); } catch (Exception ex) { Log("取消隐藏失败：" + ex.Message); }
        }
        var win = new Windows.BoxWindow(box);
        BoxWindows.Add(win);
        win.Activate();
        return win;
    }

    internal static void CloseBox(string boxId)
    {
        var win = BoxWindows.FirstOrDefault(w => w.BoxId == boxId);
        win?.Close();
    }

    /// <summary>盒子数据变了（新建/删除/改名/显示隐藏）后，让主窗口收纳盒页刷新。</summary>
    internal static event Action? BoxesChanged;

    internal static void NotifyBoxesChanged()
    {
        try { BoxesChanged?.Invoke(); } catch (Exception ex) { Log("BoxesChanged 通知失败：" + ex.Message); }
    }

    /// <summary>主窗口（外观变更时要一起刷新）。</summary>
    internal static MainWindow? Main;

    /// <summary>系统托盘（关窗隐藏后靠它把窗口叫回来）。</summary>
    private static DeskNest.Native.Win32.TrayIcon? _tray;

    /// <summary>真退出流程中：主窗口的关窗拦截据此放行，不再隐藏。</summary>
    internal static bool IsExiting;

    /// <summary>桌面盒/组件是否处于临时隐藏（托盘菜单切换，不改数据库）。</summary>
    private static bool _layerHidden;

    /// <summary>托盘要放在 UI 线程之外回调，统一从这里切回来。</summary>
    private static Microsoft.UI.Dispatching.DispatcherQueue? _uiQueue;

    private Window? _window;

    private static string BuildLogPath()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskNest");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "winui.log");
        }
        catch { return Path.Combine(AppContext.BaseDirectory, "winui.log"); }
    }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n"); } catch { }
    }

    // ---------- 系统托盘 ----------
    private const int TrayShowMain = 1001;
    private const int TraySearch = 1002;
    private const int TrayToggleLayer = 1003;
    private const int TrayExit = 1004;

    /// <summary>托盘菜单项 ID，供 MainWindow 关闭拦截等复用。</summary>
    internal static int TrayCommandExit => TrayExit;

    private static void InitTray()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "desknest.ico");
            var items = new[]
            {
                new DeskNest.Native.Win32.TrayMenuItem(TrayShowMain, "打开主窗口"),
                new DeskNest.Native.Win32.TrayMenuItem(TraySearch, "搜索…"),
                new DeskNest.Native.Win32.TrayMenuItem(TrayToggleLayer, "显示 / 隐藏桌面盒与组件"),
                new DeskNest.Native.Win32.TrayMenuItem(0, "", IsSeparator: true),
                new DeskNest.Native.Win32.TrayMenuItem(TrayExit, "退出桌巢")
            };
            _tray = new DeskNest.Native.Win32.TrayIcon(
                iconPath, "桌巢 DeskNest", items,
                id => _uiQueue?.TryEnqueue(() => OnTrayCommand(id)),
                () => _uiQueue?.TryEnqueue(ShowMainWindow));
            Log("托盘已初始化");
        }
        catch (Exception ex) { Log("托盘初始化失败：" + ex); }
    }

    private static void OnTrayCommand(int id)
    {
        switch (id)
        {
            case TrayShowMain:
                ShowMainWindow();
                break;
            case TraySearch:
                ShowMainWindow();
                Main?.SelectPage("search");
                break;
            case TrayToggleLayer:
                ToggleDesktopLayer();
                break;
            case TrayExit:
                ExitApp();
                break;
        }
    }

    /// <summary>显示并激活主窗口（含被关窗隐藏 / 最小化两种情况）。</summary>
    internal static void ShowMainWindow()
    {
        try
        {
            if (Main is null) { Log("ShowMainWindow: Main 为空"); return; }
            Main.AppWindow.Show();
            Main.Activate();
            Log("托盘：显示主窗口");
        }
        catch (Exception ex) { Log("显示主窗口失败：" + ex.Message); }
    }

    /// <summary>临时显示/隐藏所有桌面盒与组件（不动数据库里的 IsHidden/IsVisible）。</summary>
    private static void ToggleDesktopLayer()
    {
        _layerHidden = !_layerHidden;
        try
        {
            if (_layerHidden)
            {
                foreach (var w in BoxWindows.ToList()) w.AppWindow.Hide();
                foreach (var w in WidgetWindows.ToList()) w.AppWindow.Hide();
            }
            else
            {
                foreach (var w in BoxWindows.ToList()) w.AppWindow.Show();
                foreach (var w in WidgetWindows.ToList()) w.AppWindow.Show();
            }
            Log("托盘：" + (_layerHidden ? "隐藏" : "显示") + "桌面盒与组件");
        }
        catch (Exception ex) { Log("切换桌面层失败：" + ex.Message); }
    }

    /// <summary>真正退出：先收托盘，再关所有窗口，最后结束进程。</summary>
    internal static void ExitApp()
    {
        Log("ExitApp: begin");
        IsExiting = true;
        try { _tray?.Dispose(); _tray = null; } catch (Exception ex) { Log("托盘清理失败：" + ex.Message); }
        try
        {
            foreach (var w in BoxWindows.ToList()) w.Close();
            BoxWindows.Clear();
            foreach (var w in WidgetWindows.ToList()) w.Close();
            WidgetWindows.Clear();
        }
        catch (Exception ex) { Log("关窗口失败：" + ex.Message); }
        try { Current.Exit(); } catch (Exception ex) { Log("Application.Exit 失败：" + ex.Message); }
    }

    public App()
    {
        Log("App ctor: begin");
        UnhandledException += (_, e) => Log("UnhandledException: " + e.Exception);
        try
        {
            InitializeComponent();
            Log("InitializeComponent: ok");
        }
        catch (Exception ex)
        {
            Log("InitializeComponent FAILED: " + ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched: begin");
        try
        {
            // 先接数据库/设置，再建主窗口：概览页在 ctor 里就导航并读统计，
            // 顺序反了第一屏会显示 0 个盒子（P4 自验踩到）。
            try
            {
                Db = new DeskNest.Core.Data.Db();
                Repo = new DeskNest.Core.Data.Repository(Db);
                Files = new DeskNest.Core.Services.FileOps(Repo);
                Boxes = new DeskNest.Core.Services.BoxService(Repo, Files);
                Settings = new DeskNest.Core.Services.SettingsService();
                Backup = new DeskNest.Core.Services.BackupService(Settings);
                Log("services ready");
            }
            catch (Exception ex)
            {
                Log("init services FAILED: " + ex);
            }

            _window = new MainWindow();
            Main = _window as MainWindow;
            Log("MainWindow created");
            _window.Activate();
            Log("Activate called");

            _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            InitTray();

            // P2：从数据库恢复所有"显示中"的盒子（与 WPF 版同库，位置/内容互通）；
            // 库里一个都没有时放一个演示盒子，保证有东西可看。
            try
            {
                var boxes = Repo?.Boxes().Where(b => !b.IsHidden).ToList() ?? new();
                Log($"boxes in db: {boxes.Count}");
                foreach (var box in boxes)
                {
                    var win = new Windows.BoxWindow(box);
                    BoxWindows.Add(win);
                    win.Activate();
                }
                if (boxes.Count == 0)
                {
                    var demo = new Windows.BoxWindow(new DeskNest.Core.BoxRecord { Name = "演示收纳盒" });
                    BoxWindows.Add(demo);
                    demo.Activate();
                    Log("no boxes, demo BoxWindow activated");
                }

                // P5：恢复显示中的桌面组件
                var widgets = Repo?.Widgets().Where(w => w.IsVisible).ToList() ?? new();
                Log($"widgets in db: {widgets.Count}");

                foreach (var widget in widgets)
                {
                    var win = new Windows.WidgetWindow(widget);
                    WidgetWindows.Add(win);
                    win.Activate();
                }

            }
            catch (Exception ex)
            {
                Log("load boxes FAILED: " + ex);
            }
        }
        catch (Exception ex)
        {
            Log("OnLaunched FAILED: " + ex);
            throw;
        }
    }
}
