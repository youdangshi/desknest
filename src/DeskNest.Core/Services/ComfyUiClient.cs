using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskNest.Core.Services;

public sealed record ComfyImage(string FileName, string SubFolder, string Type, string? Url = null);

/// <summary>
/// 本地 ComfyUI 客户端：只连 127.0.0.1（默认 8188），不做任何云端上传。
/// 支持：连通性检测、读取可用模型/采样器、提交内置"文生图"工作流、轮询结果、下载图片。
/// </summary>
public sealed class ComfyUiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _clientId = Guid.NewGuid().ToString("n");
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ComfyUiClient(string? baseUrl = null)
    {
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:8188" : baseUrl!.TrimEnd('/');
    }

    public string BaseUrl { get; set; }

    public async Task<(bool Ok, string Message, string? Version)> TestAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(BaseUrl + "/system_stats", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (false, "ComfyUI 返回了 " + (int)resp.StatusCode, null);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.TryGetProperty("system", out var sys) && sys.TryGetProperty("comfyui_version", out var v)
                ? v.GetString() : null;
            return (true, "已连接", version);
        }
        catch (Exception ex)
        {
            return (false, "连不上 ComfyUI（确认它正在运行）：" + ex.Message, null);
        }
    }

    /// <summary>读取可用模型列表（CheckpointLoaderSimple 的 ckpt_name 可选值）。</summary>
    public async Task<List<string>> CheckpointsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(BaseUrl + "/object_info/CheckpointLoaderSimple", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("CheckpointLoaderSimple", out var node)
                && node.TryGetProperty("input", out var input)
                && input.TryGetProperty("required", out var required)
                && required.TryGetProperty("ckpt_name", out var ckpt)
                && ckpt.ValueKind == JsonValueKind.Array
                && ckpt.GetArrayLength() > 0
                && ckpt[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var item in ckpt[0].EnumerateArray())
                    if (item.GetString() is { } s) names.Add(s);
            }
            return names;
        }
        catch (Exception ex)
        {
            Log.Warn("读取 ComfyUI 模型列表失败：" + ex.Message);
            return new List<string>();
        }
    }

    public async Task<List<string>> SamplersAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(BaseUrl + "/object_info/KSampler", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("KSampler", out var node)
                && node.TryGetProperty("input", out var input)
                && input.TryGetProperty("required", out var required)
                && required.TryGetProperty("sampler_name", out var samplers)
                && samplers.ValueKind == JsonValueKind.Array
                && samplers.GetArrayLength() > 0
                && samplers[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var item in samplers[0].EnumerateArray())
                    if (item.GetString() is { } s) names.Add(s);
            }
            return names;
        }
        catch { return new List<string>(); }
    }

    public sealed record Txt2ImgOptions(
        string Checkpoint,
        string Positive,
        string Negative = "",
        int Width = 1024,
        int Height = 1024,
        int Steps = 20,
        double Cfg = 7.0,
        string Sampler = "euler",
        string Scheduler = "normal",
        long? Seed = null,
        int BatchSize = 1,
        string FileNamePrefix = "DeskNest");

    /// <summary>用内置节点图生成一张图（文生图），返回 prompt_id。</summary>
    public async Task<(bool Ok, string? PromptId, string Message)> QueueTxt2ImgAsync(Txt2ImgOptions opt, CancellationToken ct = default)
    {
        try
        {
            var graph = new JsonObject
            {
                ["4"] = new JsonObject
                {
                    ["class_type"] = "CheckpointLoaderSimple",
                    ["inputs"] = new JsonObject { ["ckpt_name"] = opt.Checkpoint }
                },
                ["5"] = new JsonObject
                {
                    ["class_type"] = "EmptyLatentImage",
                    ["inputs"] = new JsonObject
                    {
                        ["width"] = opt.Width, ["height"] = opt.Height, ["batch_size"] = Math.Clamp(opt.BatchSize, 1, 8)
                    }
                },
                ["6"] = new JsonObject
                {
                    ["class_type"] = "CLIPTextEncode",
                    ["inputs"] = new JsonObject { ["text"] = opt.Positive, ["clip"] = new JsonArray("4", 1) }
                },
                ["7"] = new JsonObject
                {
                    ["class_type"] = "CLIPTextEncode",
                    ["inputs"] = new JsonObject { ["text"] = opt.Negative, ["clip"] = new JsonArray("4", 1) }
                },
                ["3"] = new JsonObject
                {
                    ["class_type"] = "KSampler",
                    ["inputs"] = new JsonObject
                    {
                        ["seed"] = opt.Seed ?? Random.Shared.NextInt64(0, long.MaxValue),
                        ["steps"] = opt.Steps,
                        ["cfg"] = opt.Cfg,
                        ["sampler_name"] = opt.Sampler,
                        ["scheduler"] = opt.Scheduler,
                        ["denoise"] = 1.0,
                        ["model"] = new JsonArray("4", 0),
                        ["positive"] = new JsonArray("6", 0),
                        ["negative"] = new JsonArray("7", 0),
                        ["latent_image"] = new JsonArray("5", 0)
                    }
                },
                ["8"] = new JsonObject
                {
                    ["class_type"] = "VAEDecode",
                    ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
                },
                ["9"] = new JsonObject
                {
                    ["class_type"] = "SaveImage",
                    ["inputs"] = new JsonObject { ["filename_prefix"] = opt.FileNamePrefix, ["images"] = new JsonArray("8", 0) }
                }
            };
            return await QueueGraphAsync(graph, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>提交任意"API 格式"工作流（从文件读入的 JSON 对象）。</summary>
    public async Task<(bool Ok, string? PromptId, string Message)> QueueGraphAsync(JsonObject graph, CancellationToken ct = default)
    {
        try
        {
            var payload = new JsonObject { ["prompt"] = graph, ["client_id"] = _clientId };
            using var content = new StringContent(payload.ToJsonString(J), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var resp = await _http.PostAsync(BaseUrl + "/prompt", content, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (false, null, $"提交失败（{(int)resp.StatusCode}）：{Trim(text)}");
            using var doc = JsonDocument.Parse(text);
            var id = doc.RootElement.TryGetProperty("prompt_id", out var p) ? p.GetString() : null;
            return (true, id, "已提交");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;

    /// <summary>轮询直到出图或超时；返回本次产生的图片列表。</summary>
    public async Task<(bool Ok, List<ComfyImage> Images, string Message)> WaitForResultAsync(
        string promptId, TimeSpan timeout, Action<string>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await _http.GetStringAsync(BaseUrl + "/history/" + promptId, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(promptId, out var entry))
                {
                    var images = new List<ComfyImage>();
                    if (entry.TryGetProperty("outputs", out var outputs))
                    {
                        foreach (var node in outputs.EnumerateObject())
                        {
                            if (!node.Value.TryGetProperty("images", out var imgs)) continue;
                            foreach (var img in imgs.EnumerateArray())
                            {
                                var file = img.TryGetProperty("filename", out var f) ? f.GetString() : null;
                                if (file is null) continue;
                                images.Add(new ComfyImage(
                                    file,
                                    img.TryGetProperty("subfolder", out var sub) ? sub.GetString() ?? "" : "",
                                    img.TryGetProperty("type", out var t) ? t.GetString() ?? "output" : "output"));
                            }
                        }
                    }
                    if (images.Count > 0) return (true, images, "完成");

                    if (entry.TryGetProperty("status", out var status)
                        && status.TryGetProperty("status_str", out var statusStr)
                        && statusStr.GetString() == "error")
                        return (false, new List<ComfyImage>(), "ComfyUI 执行报错（详见 ComfyUI 控制台）");
                }
                progress?.Invoke($"等待出图…{(int)sw.Elapsed.TotalSeconds} 秒");
            }
            catch (Exception ex)
            {
                Log.Warn("查询 ComfyUI 结果失败：" + ex.Message);
            }
            await Task.Delay(1200, ct).ConfigureAwait(false);
        }
        return (false, new List<ComfyImage>(), "等待超时（可延长超时时间或简化工作流）");
    }

    public async Task<byte[]?> DownloadAsync(ComfyImage img, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/view?filename={Uri.EscapeDataString(img.FileName)}&subfolder={Uri.EscapeDataString(img.SubFolder)}&type={img.Type}";
            return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("下载生成图失败：" + ex.Message);
            return null;
        }
    }
}
