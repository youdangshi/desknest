namespace DeskNest.Core.Modules;

public static class ModuleIds
{
    public const string Pet = "Pet";
    public const string UsageStats = "UsageStats";
    public const string PerfMonitor = "PerfMonitor";
    public const string ApiPanel = "ApiPanel";
    public const string PowerSchedule = "PowerSchedule";
    public const string AiBridge = "AiBridge";
    public const string Capture = "Capture";
    public const string ComfyUi = "ComfyUi";
}

public sealed record ModuleInfo(
    string Id,
    string Title,
    string Description,
    bool DefaultEnabled,
    bool RequiresDownload,
    string? PackageUrl = null);

/// <summary>可选模块契约：每个模块一个独立 dll，放在 modules\ 目录下，实现本接口即可被主程序识别。</summary>
public interface IDeskNestModule
{
    ModuleInfo Info { get; }
    void Initialize(IModuleContext context);
    object? CreateView();
    void OnEnabledChanged(bool enabled);
}

public interface IModuleContext
{
    object Repository { get; }
    object Settings { get; }
    void Log(string message);
    T? GetService<T>() where T : class;
    void InvokeCommand(string command);
}
