using DeskNest.Core.Modules;

namespace DeskNest.Module.ApiPanel;

/// <summary>API 面板模块：各模型余额、调用次数、成功率、平均延迟（Key 用 DPAPI 加密）。</summary>
public sealed class ApiPanelModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.ApiPanel,
        "API 面板",
        "各模型余额 / 调用次数 / 成功率 / 平均延迟；也可以给桌宠与智能分类提供模型",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
        => _context?.Log($"API 面板模块{(enabled ? "已启用" : "已停用")}");
}
