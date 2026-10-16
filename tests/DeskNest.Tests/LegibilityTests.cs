using System.Windows.Media;
using DeskNest.App.Ui;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 「任何颜色下都看得清」的回归：强调色是用户可选的，不能假设它是深色；
/// 盒子卡片是半透明的，也不能假设壁纸是暗的。
/// </summary>
public sealed class LegibilityTests
{
    public static TheoryData<Color> Accents => new()
    {
        Color.FromRgb(0x4C, 0x8B, 0xF5),   // 默认蓝
        Color.FromRgb(0xFF, 0xD4, 0x00),   // 浅黄：最容易白底白字
        Color.FromRgb(0x8E, 0xE0, 0x8E),   // 浅绿
        Color.FromRgb(0xF5, 0xB0, 0xC8),   // 浅粉
        Color.FromRgb(0x1A, 0x1A, 0x1A),   // 近黑
        Color.FromRgb(0xFF, 0xFF, 0xFF)    // 纯白
    };

    [Theory]
    [MemberData(nameof(Accents))]
    public void TextOnAccentFill_AlwaysMeetsAa(Color accent)
    {
        var foreground = Contrast.BestForegroundOn(accent);
        var ratio = Contrast.Ratio(foreground, accent);
        Assert.True(ratio >= 4.5, $"强调色 #{accent.R:X2}{accent.G:X2}{accent.B:X2} 上的文字只有 {ratio:0.0}:1");
    }

    [Fact]
    public void TranslucentCard_FlagsWhenTextNeedsAnOutline()
    {
        var card = Color.FromRgb(0x28, 0x28, 0x2E);      // 深色卡片
        var text = Color.FromRgb(0xF5, 0xF5, 0xF5);      // 浅色文字
        var white = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var black = Color.FromRgb(0x00, 0x00, 0x00);

        // 30% 深卡片压在亮壁纸上 → 浅字看不清，必须加描边（界面里由 LegibleTextShadow 兜底）
        Assert.True(Contrast.NeedsTextBackdrop(text, card, 0.30, white));
        // 卡片足够不透明，或壁纸本来就暗 → 不需要描边
        Assert.False(Contrast.NeedsTextBackdrop(text, card, 0.95, white));
        Assert.False(Contrast.NeedsTextBackdrop(text, card, 0.30, black));
    }

    [Fact]
    public void Composite_BlendsTowardsTheBackdrop()
    {
        var card = Color.FromRgb(0x00, 0x00, 0x00);
        var white = Color.FromRgb(0xFF, 0xFF, 0xFF);
        Assert.Equal(0, Contrast.Composite(card, 1.0, white).R);      // 全不透明＝卡片本色
        Assert.Equal(255, Contrast.Composite(card, 0.0, white).R);    // 全透明＝壁纸本色
        Assert.InRange(Contrast.Composite(card, 0.5, white).R, 120, 136);
    }
}
