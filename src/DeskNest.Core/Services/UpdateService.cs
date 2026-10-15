using System.Net;
using System.Text.Json;

namespace DeskNest.Core.Services;

/// <summary>一个可用的新版本（来自 GitHub Releases）。</summary>
public sealed record UpdateInfo(
    string Version,
    string Title,
    string? Notes,
    string Url,
    DateTimeOffset? PublishedAt);

/// <summary>
/// 版本号比较（纯函数，便于测试）。
/// 认得出 `v1.2.3`、`1.2`、`1.2.3-beta.1` 这些写法：数字段逐段比，预发布版本永远小于正式版。
/// </summary>
public static class VersionCompare
{
    /// <summary>去掉前缀 v/V 与首尾空白。</summary>
    public static string Normalize(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return text;
    }

    /// <summary>把版本号拆成（数字段, 预发布标记）。解析不出来时返回空列表。</summary>
    private static (int[] Numbers, string PreRelease) Split(string? raw)
    {
        var text = Normalize(raw);
        if (text.Length == 0) return (Array.Empty<int>(), "");

        var dash = text.IndexOf('-');
        var plus = text.IndexOf('+');
        var cut = new[] { dash, plus }.Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        var numericPart = cut >= 0 ? text[..cut] : text;
        var pre = dash >= 0 ? text[(dash + 1)..].Split('+')[0] : "";

        var numbers = new List<int>();
        foreach (var piece in numericPart.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            // "1b"、"2rc" 这类就取前面的数字
            var digits = new string(piece.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0) break;
            numbers.Add(int.TryParse(digits, out var n) ? n : 0);
        }
        return (numbers.ToArray(), pre);
    }

    /// <summary>candidate 是否比 current 新。</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var (a, aPre) = Split(candidate);
        var (b, bPre) = Split(current);
        if (a.Length == 0) return false;      // 候选版本号解析不出来，当作"没有更新"
        if (b.Length == 0) return true;

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }

        // 数字段一样：正式版 > 预发布版（1.2.0 比 1.2.0-beta 新）
        if (aPre.Length == 0 && bPre.Length > 0) return true;
        if (aPre.Length > 0 && bPre.Length == 0) return false;
        return string.CompareOrdinal(aPre, bPre) > 0;
    }
}

/// <summary>
/// 检查更新：直接读 GitHub Releases 的公开接口。
/// 只在用户点「检查更新」或用户打开「启动时自动检查」时才发请求；
/// 请求只带一个 User-Agent，不带任何本机信息，也不需要令牌。
/// </summary>
public sealed class UpdateService
{
    public const string DefaultOwner = "youdangshi";
    public const string DefaultRepo = "desknest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public string Owner { get; }
    public string Repo { get; }
    public string ProjectUrl => $"https://github.com/{Owner}/{Repo}";
    public string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases";
    public string ChangelogUrl => $"https://github.com/{Owner}/{Repo}/blob/main/CHANGELOG.md";

    public UpdateService(string? owner = null, string? repo = null)
    {
        Owner = string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner!;
        Repo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo!;
    }

    public sealed record CheckResult(UpdateInfo? Update, string Message, bool Failed = false);

    /// <summary>
    /// 查最新发布版。返回的 Message 是可以直接显示给用户的中文说明，
    /// 所以网络不通、仓库还没发版本、被限流这几种情况都不会抛异常出去。
    /// </summary>
    public async Task<CheckResult> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            request.Headers.UserAgent.ParseAdd("DeskNest-UpdateCheck");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new CheckResult(null, "仓库还没有发布版本（等你打第一个 tag 之后这里就能用了）");

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new CheckResult(null, "GitHub 暂时限制了查询频率，过一会儿再试", true);

            if (!response.IsSuccessStatusCode)
                return new CheckResult(null, $"查询失败：GitHub 返回 {(int)response.StatusCode}", true);

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
                return new CheckResult(null, "没有读到版本号，可能这次发布是草稿", true);

            var version = VersionCompare.Normalize(tag);
            var title = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            DateTimeOffset? published = null;
            if (root.TryGetProperty("published_at", out var publishedElement)
                && publishedElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed))
                published = parsed;

            var info = new UpdateInfo(version, string.IsNullOrWhiteSpace(title) ? "v" + version : title!,
                notes, url ?? ReleasesUrl, published);

            return VersionCompare.IsNewer(version, currentVersion)
                ? new CheckResult(info, $"发现新版本 {info.Version}")
                : new CheckResult(info, $"已经是最新版本（{VersionCompare.Normalize(currentVersion)}）");
        }
        catch (TaskCanceledException)
        {
            return new CheckResult(null, "检查超时：网络太慢或被墙了", true);
        }
        catch (Exception ex)
        {
            return new CheckResult(null, "检查失败：" + ex.Message, true);
        }
    }
}
