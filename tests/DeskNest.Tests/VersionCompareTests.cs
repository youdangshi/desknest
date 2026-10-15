using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

/// <summary>版本比较是「检查更新」的判定核心，写错了会把老版本当成新版本推给用户。</summary>
public sealed class VersionCompareTests
{
    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.0", true)]      // 数字段比较，不是字符串比较
    [InlineData("1.2", "1.1.9", true)]         // 段数不一样也要能比
    [InlineData("1.1.0", "1.1.0.0", false)]
    [InlineData("1.0.0", "1.1.0", false)]
    [InlineData("V1.1.0", "1.1.0", false)]     // 大小写 v 前缀不算差异
    [InlineData("v1.1.0", "1.1.0", false)]
    [InlineData("1.1.0", "1.1.0", false)]
    public void IsNewer_ComparesNumericSegments(string candidate, string current, bool expected)
        => Assert.Equal(expected, VersionCompare.IsNewer(candidate, current));

    [Theory]
    [InlineData("1.1.0", "1.1.0-beta.1", true)]   // 正式版比预发布新
    [InlineData("1.1.0-beta.1", "1.1.0", false)]
    [InlineData("1.1.0-beta.2", "1.1.0-beta.1", true)]
    public void IsNewer_HandlesPreRelease(string candidate, string current, bool expected)
        => Assert.Equal(expected, VersionCompare.IsNewer(candidate, current));

    [Theory]
    [InlineData("", "1.0.0", false)]        // 读不到候选版本＝没有更新
    [InlineData("abc", "1.0.0", false)]
    [InlineData("1.0.0", "", true)]         // 当前版本读不到时，保守地认为"有更新可看"
    public void IsNewer_IsSafeOnGarbage(string candidate, string current, bool expected)
        => Assert.Equal(expected, VersionCompare.IsNewer(candidate, current));

    [Fact]
    public void Normalize_StripsPrefixAndWhitespace()
    {
        Assert.Equal("1.1.0", VersionCompare.Normalize("  v1.1.0 "));
        Assert.Equal("1.1.0", VersionCompare.Normalize("V1.1.0"));
        Assert.Equal("", VersionCompare.Normalize(null));
    }
}
