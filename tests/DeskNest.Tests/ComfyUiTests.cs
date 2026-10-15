using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

public sealed class ComfyUiClientTests
{
    [Fact]
    public async Task TestAsync_ReportsFriendlyError_WhenComfyUiNotRunning()
    {
        // 用一个几乎不可能有服务在监听的端口，模拟"ComfyUI 没启动"
        var client = new ComfyUiClient("http://127.0.0.1:59999");

        var (ok, message, version) = await client.TestAsync();

        Assert.False(ok);
        Assert.Contains("ComfyUI", message);
        Assert.Null(version);
    }

    [Fact]
    public async Task CheckpointsAsync_ReturnsEmptyList_InsteadOfThrowing()
    {
        var client = new ComfyUiClient("http://127.0.0.1:59998");
        var list = await client.CheckpointsAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task QueueTxt2Img_PostsApiFormatGraph_AndReturnsPromptId()
    {
        using var server = new StubComfyServer(expectedPath: "/prompt");
        var client = new ComfyUiClient(server.BaseUrl);

        var (ok, promptId, message) = await client.QueueTxt2ImgAsync(new ComfyUiClient.Txt2ImgOptions(
            "sdxl_base.safetensors", "a cat", "bad", 512, 512, 10, 5.0, "euler", "normal", 42, 2));

        Assert.True(ok, message);
        Assert.Equal("prompt-123", promptId);
        Assert.NotNull(server.LastBody);
        var body = JsonNode.Parse(server.LastBody!)!;
        var graph = body["prompt"]!;
        Assert.Equal("CheckpointLoaderSimple", graph["4"]!["class_type"]!.GetValue<string>());
        Assert.Equal("sdxl_base.safetensors", graph["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>());
        Assert.Equal("a cat", graph["6"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal(512, graph["5"]!["inputs"]!["width"]!.GetValue<int>());
        Assert.Equal(42, graph["3"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal("KSampler", graph["3"]!["class_type"]!.GetValue<string>());
        Assert.Equal("SaveImage", graph["9"]!["class_type"]!.GetValue<string>());
    }

    /// <summary>最小可用的假 ComfyUI（只回答 /prompt），用来验证客户端发的 JSON 结构。</summary>
    private sealed class StubComfyServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public string? LastBody { get; private set; }
        public string BaseUrl { get; }

        public StubComfyServer(string expectedPath)
        {
            var port = Random.Shared.Next(58000, 59000);
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { break; }
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        LastBody = await reader.ReadToEndAsync();
                    var payload = ctx.Request.Url!.AbsolutePath == expectedPath
                        ? "{\"prompt_id\":\"prompt-123\",\"number\":1,\"node_errors\":{}}"
                        : "{}";
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.OutputStream.Close();
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }
}
