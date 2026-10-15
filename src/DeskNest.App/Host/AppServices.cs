using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Modules;
using DeskNest.Core.Services;
using DeskNest.Native.Win32;

namespace DeskNest.App.Host;

/// <summary>组合根：所有服务在这里创建并互相注入（界面层只从这里取）。</summary>
public static class AppServices
{
    public static Db Db { get; private set; } = null!;
    public static Repository Repo { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static FileOps FileOps { get; private set; } = null!;
    public static OrganizeEngine Organizer { get; private set; } = null!;
    public static SearchService Search { get; private set; } = null!;
    public static BackupService Backup { get; private set; } = null!;
    public static UsageStatsService Usage { get; private set; } = null!;
    public static PerfSampler Perf { get; private set; } = null!;
    public static PowerScheduler Power { get; private set; } = null!;
    public static ApiPanelService Api { get; private set; } = null!;
    public static AiBridgeService AiBridge { get; private set; } = null!;
    public static ModuleHost Modules { get; private set; } = null!;
    public static PetEngine Pet { get; private set; } = null!;
    public static KeyboardHook Keyboard { get; private set; } = null!;

    public static event Action? RestartNeeded;

    /// <summary>初始化服务。startBackgroundWork=false 时只建对象、不启动索引/采样（供自动化测试用）。</summary>
    public static void Initialize(bool startBackgroundWork = true)
    {
        AppPaths.EnsureCreated();
        Log.Info("========== 桌巢启动 ==========");

        Settings = new SettingsService();
        Db = new Db();
        Repo = new Repository(Db);

        FileOps = new FileOps(Repo);
        Organizer = new OrganizeEngine(Repo, FileOps);
        Search = new SearchService(Repo);
        Backup = new BackupService(Settings);
        Usage = new UsageStatsService(Repo, Settings, ForegroundWatcher.Sample);
        Perf = new PerfSampler(SystemMetrics.Read, SystemMetrics.ReadGpu);
        Power = new PowerScheduler(Repo, PowerActions.Execute, SystemMetrics.IdleTime);
        Api = new ApiPanelService(Repo, new DpapiSecretBox());
        AiBridge = new AiBridgeService(Repo, Settings);
        Keyboard = new KeyboardHook();

        Pet = new PetEngine();
        ApplyPetSettings();

        Modules = new ModuleHost(Settings, new ModuleContext());
        if (startBackgroundWork) Modules.Scan();

        // 主题令牌从设置恢复
        Ui.Theme.Apply(new Ui.ThemeTokens
        {
            Scheme = Enum.TryParse<Ui.ColorScheme>(Settings.Get("theme.scheme", "FollowSystem"), out var cs) ? cs : Ui.ColorScheme.FollowSystem,
            Material = Enum.TryParse<Ui.MaterialKind>(Settings.Get("theme.material", "Acrylic"), out var mk) ? mk : Ui.MaterialKind.Acrylic,
            MaterialIntensity = Settings.Get("theme.intensity", 0.65),
            CornerRadius = Settings.Get("theme.corner", 12),
            Opacity = Settings.Get("theme.opacity", 0.82),
            ReduceMotion = Settings.Get("theme.reduceMotion", false)
        });

        // 后台任务
        if (startBackgroundWork)
        {
            if (Settings.Get("search.enabled", true)) Search.StartIndexing(SearchRoots());
            if (Settings.Get("usage.track", true)) Usage.Start();
            if (Settings.Get("modules.PerfMonitor", false)) Perf.Start();
            if (Settings.Get("ai.enabled", false)) AiBridge.Start();
            Power.Apply(Power.Plan);
            Power.Event += e => Ui.Notifications.Toast(e.Message);
        }
        if (Repo.Pet().AiAskEnabled) AttachKeyboardFollow();
    }

    public static List<string> SearchRoots()
    {
        var raw = Settings.Get<string?>("search.roots", null);
        if (!string.IsNullOrWhiteSpace(raw))
            return raw!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new List<string>();   // 空 = 所有固定磁盘
    }

    public static void ApplyPetSettings()
    {
        var p = Repo.Pet();
        Pet.KeyboardFollow = p.KeyboardFollow;
        Pet.ClickReaction = p.ClickReaction;
        Pet.SystemReaction = p.SystemReaction;
        Pet.IdleReaction = p.IdleReaction;
        Pet.TodoReminder = p.TodoReminder;
    }

    public static void AttachKeyboardFollow()
    {
        if (Pet.KeyboardFollow && !Keyboard.IsRunning)
        {
            Keyboard.KeyPressed += () => Pet.OnKeyPress();
            Keyboard.Start();
        }
        else if (!Pet.KeyboardFollow && Keyboard.IsRunning)
        {
            Keyboard.Stop();
        }
    }

    public static void Shutdown()
    {
        try
        {
            Usage.Flush();
            Usage.Stop();
            Perf.Stop();
            Search.StopIndexing();
            AiBridge.Stop();
            Keyboard.Dispose();
            Modules.Shutdown();
            if (Settings.Get("backup.onExit", false)) Backup.Create("exit");
            Log.Info("桌巢已退出");
            Db.Dispose();
        }
        catch (Exception ex) { Log.Warn("退出清理异常：" + ex.Message); }
    }

    public static void RequestRestart() => RestartNeeded?.Invoke();
}
