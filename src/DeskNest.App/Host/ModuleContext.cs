using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.App.Host;

/// <summary>提供给可选模块的宿主上下文（模块只能拿到这里暴露的东西）。</summary>
public sealed class ModuleContext : IModuleContext
{
    public object Repository => AppServices.Repo;
    public object Settings => AppServices.Settings;

    public void Log(string message) => Core.Log.Info("[module] " + message);

    public T? GetService<T>() where T : class
    {
        object? candidate = null;
        if (typeof(T) == typeof(Core.Data.Repository)) candidate = AppServices.Repo;
        else if (typeof(T) == typeof(SettingsService)) candidate = AppServices.Settings;
        else if (typeof(T) == typeof(PerfSampler)) candidate = AppServices.Perf;
        else if (typeof(T) == typeof(UsageStatsService)) candidate = AppServices.Usage;
        else if (typeof(T) == typeof(ApiPanelService)) candidate = AppServices.Api;
        else if (typeof(T) == typeof(PowerScheduler)) candidate = AppServices.Power;
        else if (typeof(T) == typeof(BackupService)) candidate = AppServices.Backup;
        else if (typeof(T) == typeof(OrganizeEngine)) candidate = AppServices.Organizer;
        else if (typeof(T) == typeof(PetEngine)) candidate = AppServices.Pet;
        else if (typeof(T) == typeof(AiBridgeService)) candidate = AppServices.AiBridge;
        else if (typeof(T) == typeof(Core.Services.SearchService)) candidate = AppServices.Search;
        return candidate as T;
    }

    public void InvokeCommand(string command) => Commands.Execute(command);
}
