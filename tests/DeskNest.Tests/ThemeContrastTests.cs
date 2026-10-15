using System.Windows.Media;
using DeskNest.App.Ui;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 主题可读性回归：每个配色方案下，正文/次要文字对背景与卡片都要满足 WCAG 对比度。
/// 这一条是为了防住 v1.0.0 那个"深底 + 深字，完全看不清"的缺陷再回来。
/// </summary>
public sealed class ThemeContrastTests
{
    public static TheoryData<ColorScheme> Schemes => new()
    {
        ColorScheme.Dark, ColorScheme.Light, ColorScheme.PureBlack, ColorScheme.EyeCare
    };

    [Theory]
    [MemberData(nameof(Schemes))]
    public void BodyText_MeetsWcagAa_OnBackgroundAndCard(ColorScheme scheme)
    {
        var theme = new ThemeTokens { Scheme = scheme };

        var onBackground = Contrast.Ratio(theme.Foreground, theme.Background);
        var onCard = Contrast.Ratio(theme.Foreground, theme.CardColor);

        Assert.True(onBackground >= 4.5, $"{scheme}: 正文对背景对比度只有 {onBackground:0.0}:1（需 ≥ 4.5:1）");
        Assert.True(onCard >= 4.5, $"{scheme}: 正文对卡片对比度只有 {onCard:0.0}:1（需 ≥ 4.5:1）");
    }

    [Theory]
    [MemberData(nameof(Schemes))]
    public void SecondaryText_MeetsMinimumContrast(ColorScheme scheme)
    {
        var theme = new ThemeTokens { Scheme = scheme };

        var secondaryOnBackground = Contrast.Ratio(theme.SecondaryColor, theme.Background);
        var tertiaryOnBackground = Contrast.Ratio(theme.TertiaryColor, theme.Background);

        Assert.True(secondaryOnBackground >= 3.0, $"{scheme}: 次要文字对背景只有 {secondaryOnBackground:0.0}:1（需 ≥ 3:1）");
        Assert.True(tertiaryOnBackground >= 2.0, $"{scheme}: 辅助文字对背景只有 {tertiaryOnBackground:0.0}:1（需 ≥ 2:1）");
    }

    [Fact]
    public void IsDark_FollowsScheme_NotClock()
    {
        // 护眼与浅色永远是"浅色族"，深色与纯黑永远是"深色族"（与当前时间无关）
        Assert.False(new ThemeTokens { Scheme = ColorScheme.EyeCare }.IsDark);
        Assert.False(new ThemeTokens { Scheme = ColorScheme.Light }.IsDark);
        Assert.True(new ThemeTokens { Scheme = ColorScheme.Dark }.IsDark);
        Assert.True(new ThemeTokens { Scheme = ColorScheme.PureBlack }.IsDark);
    }
}
