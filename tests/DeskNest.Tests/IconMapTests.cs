using DeskNest.App.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 图标映射回归：v1.0.1 出过一个"左侧导航每一项都是同一个齿轮"的缺陷——
/// 导航传的是 <see cref="Icons"/> 里的 MDL2 码位，而 SymbolFor 只认 emoji/语义键，
/// 于是全部落进默认值。这里把所有会出现在导航里的图标钉住。
/// </summary>
public sealed class IconMapTests
{
    private static readonly (string Name, string Code)[] NavIcons =
    {
        ("Home", Icons.Home),
        ("Folder", Icons.Folder),
        ("Organize", Icons.Organize),
        ("Widgets", Icons.Widgets),
        ("Appearance", Icons.Appearance),
        ("Modules", Icons.Modules),
        ("Data", Icons.Data),
        ("About", Icons.About),
        ("Pet", Icons.Pet),
        ("Image", Icons.Image),
        ("Performance", Icons.Performance),
        ("Usage", Icons.Usage),
        ("Api", Icons.Api),
        ("Power", Icons.Power),
        ("Ai", Icons.Ai),
        ("Monitor", Icons.Monitor),
        ("Search", Icons.Search),
        ("Settings", Icons.Settings),
        ("Refresh", Icons.Refresh),
        ("Delete", Icons.Delete),
    };

    [Fact]
    public void EveryNavIcon_ResolvesToItsOwnSymbol()
    {
        var offenders = new List<string>();
        foreach (var (name, code) in NavIcons)
        {
            var symbol = Icons.SymbolFor(code);
            // 只有"设置"本身才允许是齿轮；其余落到 Settings24 说明映射漏了
            if (symbol == SymbolRegular.Settings24 && name != "Settings")
                offenders.Add($"{name}（{code}）落回了默认齿轮图标");
        }
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    [Fact]
    public void NavIcons_AreDistinct()
    {
        // 同上：如果整张表都映射成同一个图标，看起来仍然是"全是齿轮"
        var distinct = NavIcons.Select(i => Icons.SymbolFor(i.Code)).Distinct().Count();
        Assert.True(distinct >= NavIcons.Length - 2,
            $"导航图标只解析出 {distinct} 种，而输入有 {NavIcons.Length} 个，说明映射退化了");
    }

    [Fact]
    public void EmojiIcons_StillMap()
    {
        // 双向兼容：页面里写成 emoji 的老代码不能因为加了码位分支而失效
        Assert.Equal(SymbolRegular.Folder24, Icons.SymbolFor("📁"));
        Assert.Equal(SymbolRegular.Gauge24, Icons.SymbolFor("📈"));
        Assert.Equal(SymbolRegular.Search24, Icons.SymbolFor("🔍"));
    }
}
