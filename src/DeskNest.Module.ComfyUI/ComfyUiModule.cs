using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.Module.ComfyUI;

/// <summary>
/// 本地 AI 出图模块：连接你自己电脑上的 ComfyUI（默认 127.0.0.1:8188），
/// 用你的模型出图，图片直接存到本地目录。不联网、不上传。
/// </summary>
public sealed class ComfyUiModule : IDeskNestModule
{
    private IModuleContext? _context;

    public ModuleInfo Info => new(
        ModuleIds.ComfyUi,
        "AI 出图（本地 ComfyUI）",
        "调用你本机运行的 ComfyUI 生成图片：选模型、写提示词、一键出图并保存到本地",
        DefaultEnabled: false,
        RequiresDownload: false);

    public void Initialize(IModuleContext context) => _context = context;

    public object? CreateView() => null;

    public void OnEnabledChanged(bool enabled)
        => _context?.Log($"AI 出图模块{(enabled ? "已启用" : "已停用")}");
}
