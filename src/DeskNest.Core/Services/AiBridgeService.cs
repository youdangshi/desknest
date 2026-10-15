using System.Net;
using System.Text;
using System.Text.Json;
using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>
/// 本地 AI 接口：只读地把桌巢的数据暴露给本机的 Codex / dsh / Claude 等工具。
/// 默认只监听 127.0.0.1，必须带 token；可读范围在设置里逐项授权。
/// </summary>
public sealed class AiBridgeService : IDisposable
{
    private readonly Repository _repo;
    private readonly SettingsService _settings;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public AiBridgeService(Repository repo, SettingsService settings)
    {
        _repo = repo;
        _settings = settings;
    }

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public int Port => _settings.Get("ai.port", 8737);

    public string Token
    {
        get
        {
            var t = _settings.Get<string?>("ai.token", null);
            if (string.IsNullOrWhiteSpace(t))
            {
                t = Guid.NewGuid().ToString("n");
                _settings.Set("ai.token", t);
            }
            return t!;
        }
    }

    public string Endpoint => $"http://127.0.0.1:{Port}";
    public bool Running { get; private set; }

    public void RegenerateToken() => _settings.Set("ai.token", Guid.NewGuid().ToString("n"));

    public bool Allowed(string kind) => _settings.Get($"ai.allow.{kind}", true);
    public void SetAllowed(string kind, bool value) => _settings.Set($"ai.allow.{kind}", value);

    public void Start()
    {
        if (Running) return;
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            Running = true;
            _ = Task.Run(() => Loop(_cts.Token));
            Log.Info($"AI 接口已启动：{Endpoint}（仅本机）");
        }
        catch (Exception ex)
        {
            Running = false;
            Log.Warn("AI 接口启动失败：" + ex.Message);
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        _listener = null;
        Running = false;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => Handle(ctx), ct);
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = (ctx.Request.Url?.AbsolutePath ?? "/").ToLowerInvariant();
            var token = ctx.Request.Headers["X-DeskNest-Token"] ?? ctx.Request.QueryString["token"];
            if (!string.Equals(token, Token, StringComparison.Ordinal))
            {
                Respond(ctx, 401, new { error = "缺少或错误的 X-DeskNest-Token" });
                return;
            }

            switch (path)
            {
                case "/health":
                    Respond(ctx, 200, new
                    {
                        app = "DeskNest",
                        version = "1.0.0",
                        time = DateTimeOffset.Now,
                        scope = string.Join(", ", new[] { "boxes", "items", "todos", "notes", "usage" }
                            .Select(k => $"{k}={(Allowed(k) ? "on" : "off")}"))
                    });
                    break;

                case "/boxes":
                    if (!Guarded(ctx, "boxes")) return;
                    Respond(ctx, 200, _repo.Boxes().Select(b => new
                    {
                        b.Id, b.Name, kind = b.Kind.ToString(),
                        itemCount = _repo.Items(b.Id, b.Kind == BoxKind.Mapped).Count(i => !i.IsMissing),
                        path = b.StorageRoot ?? b.MappedPath
                    }));
                    break;

                case "/items":
                    if (!Guarded(ctx, "items")) return;
                    var boxId = ctx.Request.QueryString["box"];
                    Respond(ctx, 200, _repo.AllItems()
                        .Where(i => boxId is null || i.BoxId == boxId)
                        .Select(i => new { i.BoxId, i.DisplayName, i.Ext, i.SizeBytes, i.AddedAt, i.IsMissing }));
                    break;

                case "/todos":
                    if (!Guarded(ctx, "todos")) return;
                    Respond(ctx, 200, _repo.Todos().Select(t => new { t.Id, t.Title, t.ListName, t.Priority, t.DueAt, t.IsDone }));
                    break;

                case "/notes":
                    if (!Guarded(ctx, "notes")) return;
                    Respond(ctx, 200, _repo.Notes().Select(n => new { n.Id, n.Title, n.Body, n.IsPinned, n.UpdatedAt }));
                    break;

                case "/usage":
                    if (!Guarded(ctx, "usage")) return;
                    Respond(ctx, 200, _repo.UsageSince(DateTimeOffset.Now.AddDays(-1))
                        .GroupBy(s => s.ProcessName)
                        .Select(g => new { process = g.Key, seconds = g.Sum(s => s.DurationSeconds) })
                        .OrderByDescending(x => x.seconds));
                    break;

                default:
                    Respond(ctx, 404, new
                    {
                        error = "未知接口",
                        available = new[] { "/health", "/boxes", "/items?box=", "/todos", "/notes", "/usage" }
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("AI 接口处理异常：" + ex.Message);
            try { Respond(ctx, 500, new { error = ex.Message }); } catch { }
        }
    }

    private bool Guarded(HttpListenerContext ctx, string kind)
    {
        if (Allowed(kind)) return true;
        Respond(ctx, 403, new { error = $"未授权读取 {kind}" });
        return false;
    }

    private static void Respond(HttpListenerContext ctx, int status, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, J));
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    public void Dispose() => Stop();
}
