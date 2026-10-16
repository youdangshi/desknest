using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 会碰真实桌面的测试（建窗口、发鼠标事件、用 AppServices 单例）必须串行。
/// 并行跑的话：两边都会重建 AppServices、抢窗口焦点、互相把对方正在拖的窗口挪走。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopUiCollection
{
    public const string Name = "桌面层 UI（串行）";
}
