using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.PerfMonitor;

/// <summary>性能监视模块：CPU/内存/网络/GPU 采样（GPU 需要 NVIDIA 驱动）。</summary>
public sealed class PerfMonitorModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.PerfMonitor,
        "性能监视面板",
        "CPU / 内存 / 网络 / GPU：显示项与布局都能自己选，也可以完全不开",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
    {
        var perf = _context?.GetService<PerfSampler>();
        if (enabled) perf?.Start();
        else perf?.Stop();
        _context?.Log($"性能监视模块{(enabled ? "已启用" : "已停用")}");
    }
}
