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
    public static BoxService Boxes { get; private set; } = null!;
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
    /// <param name="startBackgroundWork">是否启动后台任务（模块扫描、索引、采样等）。</param>
    /// <param name="startIndexing">
    /// 是否开始全盘索引。截图模式会关掉它：索引跑到一半就被关掉，会把一份残缺的索引写进库。
    /// </param>
    public static void Initialize(bool startBackgroundWork = true, bool startIndexing = true)
    {
        AppPaths.EnsureCreated();
        Log.Info("========== 桌巢启动 ==========");

        Settings = new SettingsService();
        Db = new Db();
        Repo = new Repository(Db);

        FileOps = new FileOps(Repo);
        Organizer = new OrganizeEngine(Repo, FileOps);
        Boxes = new BoxService(Repo, FileOps);
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

        // 主题令牌从设置恢复：设计窗口（主窗口/设置页）与收纳窗口（桌面盒/组件）**各自独立**。
        // 旧版本只有一个 theme.* 键，作为两者的兜底默认值。
        Ui.Theme.ApplyShell(ReadTokens("theme.shell"));
        Ui.Theme.ApplyDesktop(ReadTokens("theme.desktop"));

        // 后台任务
        if (startBackgroundWork)
        {
            if (startIndexing && Settings.Get("search.enabled", true)) Search.StartIndexing(SearchRoots());
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

    /// <summary>按前缀读取一套主题令牌；缺项时回落到旧的单一 theme.* 键，再回落到内置默认。</summary>
    private static Ui.ThemeTokens ReadTokens(string prefix)
    {
        string Legacy(string key) => Settings.Get(key, "");
        var scheme = Settings.Get($"{prefix}.scheme", Legacy("theme.scheme"));
        if (string.IsNullOrWhiteSpace(scheme)) scheme = "FollowSystem";
        var material = Settings.Get($"{prefix}.material", Legacy("theme.material"));
        if (string.IsNullOrWhiteSpace(material)) material = "Acrylic";

        return new Ui.ThemeTokens
        {
            Scheme = Enum.TryParse<Ui.ColorScheme>(scheme, out var cs) ? cs : Ui.ColorScheme.FollowSystem,
            Material = Enum.TryParse<Ui.MaterialKind>(material, out var mk) ? mk : Ui.MaterialKind.Acrylic,
            MaterialIntensity = Settings.Get($"{prefix}.intensity", Settings.Get("theme.intensity", 0.65)),
            CornerRadius = Settings.Get($"{prefix}.corner", Settings.Get("theme.corner", 12)),
            Opacity = Settings.Get($"{prefix}.opacity", Settings.Get("theme.opacity", 0.82)),
            ReduceMotion = Settings.Get("theme.reduceMotion", false),
            CardColorOverride = ParseCardColor(Settings.Get($"{prefix}.cardColor", Settings.Get("theme.cardColor", "")))
        };
    }

    /// <summary>"#RRGGBB" → 颜色；空着或写坏了都当作"跟随配色方案"（null）。</summary>
    private static System.Windows.Media.Color? ParseCardColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(raw)!; }
        catch { return null; }
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
            // 索引线程还在写盘时释放连接会抛 "Cannot access a disposed object"；
            // 那种情况就让它自己写完，连接随进程结束一起回收（WAL 下不会损坏数据）。
            if (Search.IsIdle)
            {
                Log.Info("桌巢已退出");
                Db.Dispose();
            }
            else
            {
                Log.Warn("索引线程仍在收尾，跳过数据库释放（进程退出时统一回收）");
            }
        }
        catch (Exception ex) { Log.Warn("退出清理异常：" + ex.Message); }
    }

    public static void RequestRestart() => RestartNeeded?.Invoke();
}
