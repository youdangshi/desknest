using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.AiBridge;

/// <summary>AI 接口模块：把桌巢的数据以只读接口开放给本机的 Codex / dsh / Claude。</summary>
public sealed class AiBridgeModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.AiBridge,
        "AI 接口（本地服务）",
        "只读接口：盒内文件元数据 / 待办 / 随记 / 使用统计，仅监听 127.0.0.1，需要令牌",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
    {
        var bridge = _context?.GetService<AiBridgeService>();
        if (bridge is null) return;
        if (enabled) bridge.Start();
        else bridge.Stop();
        _context?.Log($"AI 接口模块{(enabled ? "已启用" : "已停用")}");
    }
}
