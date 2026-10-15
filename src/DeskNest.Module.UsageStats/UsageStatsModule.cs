using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.UsageStats;

/// <summary>应用使用统计模块：启用后才开始 1 秒采样，停用立即停止。</summary>
public sealed class UsageStatsModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.UsageStats,
        "应用使用统计",
        "类似 ManicTime：记录每个应用用了多久（默认只记进程名，数据全在本机）",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
    {
        var usage = _context?.GetService<UsageStatsService>();
        if (enabled) usage?.Start();
        else usage?.Stop();
        _context?.Log($"使用统计模块{(enabled ? "已启用" : "已停用")}");
    }
}
