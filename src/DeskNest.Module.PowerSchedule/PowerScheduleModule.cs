using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.PowerSchedule;

/// <summary>计划关机模块：定时 / 倒计时 / 空闲触发，执行前有提醒，可顺延或取消。</summary>
public sealed class PowerScheduleModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.PowerSchedule,
        "计划关机",
        "定时 / 倒计时 / 空闲触发；提前弹窗提醒，可再等 10 分钟或直接取消",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
    {
        var power = _context?.GetService<PowerScheduler>();
        if (power is null) return;
        if (enabled) power.Apply(power.Plan);
        else power.Cancel("模块已停用");
        _context?.Log($"计划关机模块{(enabled ? "已启用" : "已停用")}");
    }
}
