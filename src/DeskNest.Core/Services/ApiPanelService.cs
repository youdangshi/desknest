using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>密钥保护：由 Native 提供（Windows DPAPI）。未提供时退化为本地混淆存放，并在界面明确提示。</summary>
public interface ISecretBox
{
    string Protect(string plain);
    string? Unprotect(string cipher);
}

public sealed record ProviderStat(string ProviderId, int Calls, int Failures, double SuccessRate, long AvgLatencyMs);

/// <summary>
/// API 面板与智能分类：管理各厂商 Key（DPAPI 加密）、统计成功率与延迟、
/// 提供 OpenAI 兼容的 chat 调用（供智能分类与桌宠问答使用）。
/// </summary>
public sealed class ApiPanelService
{
    private readonly Repository _repo;
    private readonly ISecretBox? _secrets;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public ApiPanelService(Repository repo, ISecretBox? secrets = null)
    {
        _repo = repo;
        _secrets = secrets;
    }

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public List<ApiProviderRecord> Providers() => _repo.Providers();

    public string? KeyPreview(ApiProviderRecord p)
    {
        var key = Reveal(p);
        if (string.IsNullOrEmpty(key)) return null;
        return key.Length <= 8 ? "●●●●" : key[..4] + "●●●●" + key[^4..];
    }

    public void Save(ApiProviderRecord p, string? plainKey)
    {
        if (!string.IsNullOrWhiteSpace(plainKey))
            p.KeyCipher = _secrets?.Protect(plainKey!) ?? Convert.ToBase64String(Encoding.UTF8.GetBytes(plainKey!));
        _repo.Upsert(p);
    }

    public string? Reveal(ApiProviderRecord p)
    {
        if (string.IsNullOrEmpty(p.KeyCipher)) return null;
        try
        {
            return _secrets?.Unprotect(p.KeyCipher) ?? Encoding.UTF8.GetString(Convert.FromBase64String(p.KeyCipher));
        }
        catch { return null; }
    }

    public void Delete(string id) => _repo.DeleteProvider(id);

    public ProviderStat Stat(string providerId, int days = 7)
    {
        var stats = _repo.ApiStats(providerId, DateTimeOffset.Now.AddDays(-days));
        if (stats.Count == 0) return new ProviderStat(providerId, 0, 0, 0, 0);
        var failures = stats.Count(s => !s.Success);
        return new ProviderStat(providerId, stats.Count, failures,
            (stats.Count - failures) * 100.0 / stats.Count,
            (long)stats.Average(s => s.LatencyMs));
    }

    public async Task<(bool Ok, string Content, string? Error, long Ms)> ChatAsync(
        ApiProviderRecord provider, string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var key = Reveal(provider);
        if (string.IsNullOrWhiteSpace(key)) return (false, "", "未配置 API Key", 0);
        try
        {
            var url = provider.BaseUrl.TrimEnd('/') + "/chat/completions";
            var body = new
            {
                model = provider.Model ?? "deepseek-chat",
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2,
                stream = false
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Content = new StringContent(JsonSerializer.Serialize(body, J), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            sw.Stop();
            var ok = resp.IsSuccessStatusCode;
            var content = "";
            if (ok)
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                }
                catch { content = text; }
            }
            _repo.InsertApiStat(new ApiStatRecord
            {
                ProviderId = provider.Id,
                Success = ok,
                LatencyMs = sw.ElapsedMilliseconds,
                Purpose = "chat",
                ErrorKind = ok ? null : ((int)resp.StatusCode).ToString()
            });
            return (ok, content, ok ? null : $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _repo.InsertApiStat(new ApiStatRecord
            {
                ProviderId = provider.Id, Success = false, LatencyMs = sw.ElapsedMilliseconds,
                Purpose = "chat", ErrorKind = ex.GetType().Name
            });
            return (false, "", ex.Message, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>智能分类：交给模型给出"文件名 → 分类"建议（结果先预览再执行）。</summary>
    public async Task<(bool Ok, Dictionary<string, string> Map, string? Error)> ClassifyAsync(
        ApiProviderRecord provider, IEnumerable<string> fileNames, IEnumerable<string> categories, CancellationToken ct = default)
    {
        var prompt = $$"""
        你是文件整理助手。请把下面的文件名分别归入这些分类之一：{{string.Join("、", categories)}}，无法判断用"其他"。
        只输出 JSON 对象：{"文件名":"分类"}，不要解释、不要代码块。
        文件名：
        {{string.Join("\n", fileNames)}}
        """;
        var (ok, content, err, _) = await ChatAsync(provider, "你只输出 JSON。", prompt, ct).ConfigureAwait(false);
        if (!ok) return (false, new Dictionary<string, string>(), err);
        try
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start < 0 || end <= start) return (false, new Dictionary<string, string>(), "模型返回内容无法解析");
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(content[start..(end + 1)], J)
                      ?? new Dictionary<string, string>();
            return (true, map, null);
        }
        catch (Exception ex) { return (false, new Dictionary<string, string>(), ex.Message); }
    }
}
