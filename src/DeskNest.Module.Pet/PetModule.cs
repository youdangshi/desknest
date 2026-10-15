using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.Pet;

/// <summary>
/// 桌宠模块（独立 dll）：只负责声明能力、跟随启用状态启停键盘跟随。
/// 界面在主程序里（主窗口 → 桌宠），因为桌宠窗口本身属于桌面层的一部分。
/// </summary>
public sealed class PetModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.Pet,
        "桌宠",
        "工作伙伴型桌宠：待办提醒、番茄钟、休息提醒、系统状态联动、AI 问答（默认关闭）",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
    {
        if (enabled)
        {
            _context?.Log("桌宠模块已启用：待办提醒 / 番茄钟 / 系统联动就绪");
            _context?.GetService<PetEngine>()?.GetType();
        }
        else
        {
            _context?.Log("桌宠模块已停用：不再占用任何资源");
        }
    }
}
