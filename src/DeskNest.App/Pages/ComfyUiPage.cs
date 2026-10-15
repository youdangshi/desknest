using System.Windows;
using System.Windows.Controls;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

/// <summary>
/// AI 出图页：连接本机 ComfyUI（默认 127.0.0.1:8188），用本地模型出图并存到本地目录。
/// 观感与其它设置页一致：每行一个设置项，右侧是控件。
/// </summary>
public static partial class ComfyUiPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("AI 出图（ComfyUI）",
            "用你自己电脑上的 ComfyUI 与模型生成图片：不联网、不上传，出图直接存到本地。"));

        var baseUrl = AppServices.Settings.Get("comfy.baseUrl", "http://127.0.0.1:8188");
        var outputDir = AppServices.Settings.Get("comfy.outputDir", DefaultOutputDir());
        var statusText = AppServices.Settings.Get("comfy.lastStatus", "尚未连接");
        var status = UiKit.Text(statusText, 12, secondary: true);
        var models = AppServices.Settings.Get("comfy.models", new List<string>());
        var client = new ComfyUiClient(baseUrl);

        var testBtn = UiKit.Btn("测试连接", async () =>
        {
            status.Text = "正在连接…";
            var (ok, message, version) = await client.TestAsync();
            status.Text = ok ? $"已连接{(version is null ? "" : "，ComfyUI " + version)}" : "连接失败：" + message;
            AppServices.Settings.Set("comfy.lastStatus", status.Text);
            if (!ok) return;

            var list = await client.CheckpointsAsync();
            if (list.Count > 0)
            {
                AppServices.Settings.Set("comfy.models", list);
                Ui.Notifications.Toast($"找到 {list.Count} 个模型，已刷新下拉列表", 3);
                host.RefreshCurrent();
            }
            else
            {
                Ui.Notifications.Toast("连上了，但没读到模型：确认 models\\checkpoints 里有模型文件", 5);
            }
        }, true);

        col.Children.Add(UiKit.Section("连接",
            UiKit.Row("🔌", "ComfyUI 地址", "绘世启动后默认就是 8188 端口", testBtn),
            UiKit.InputRow("🌐", "服务地址", "只连本机，不会访问外网", baseUrl, v =>
            {
                AppServices.Settings.Set("comfy.baseUrl", v);
                client.BaseUrl = v.TrimEnd('/');
            }, 240),
            UiKit.Row("📁", "出图保存目录", outputDir, UiKit.Row(
                UiKit.Btn("更改…", () =>
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择出图保存目录" };
                    if (dlg.ShowDialog() == true)
                    {
                        AppServices.Settings.Set("comfy.outputDir", dlg.FolderName);
                        host.RefreshCurrent();
                    }
                }),
                UiKit.Btn("打开", () => OpenFolder(AppServices.Settings.Get("comfy.outputDir", DefaultOutputDir()))))),
            UiKit.Row("ℹ", "当前状态", null, status)));

        var modelList = models.Count > 0
            ? models
            : new List<string> { "（先点右边「测试连接」读取模型）" };
        var selectedModel = AppServices.Settings.Get("comfy.model", modelList[0]);
        if (!modelList.Contains(selectedModel)) selectedModel = modelList[0];

        var positive = UiKit.Input(AppServices.Settings.Get("comfy.positive",
            "masterpiece, best quality, cozy desk, soft light"), v => AppServices.Settings.Set("comfy.positive", v), 420, multiline: true);
        var negative = UiKit.Input(AppServices.Settings.Get("comfy.negative",
            "lowres, worst quality, watermark, text"), v => AppServices.Settings.Set("comfy.negative", v), 420, multiline: true);

        col.Children.Add(UiKit.Section("模型与提示词",
            UiKit.ComboRow("🧠", "模型（checkpoint）", "来自你 ComfyUI 的 models\\checkpoints", modelList, selectedModel,
                v => AppServices.Settings.Set("comfy.model", v)),
            UiKit.Row("✍", "正向提示词", "描述你想要的画面", positive),
            UiKit.Row("🚫", "反向提示词", "不想要的内容", negative)));

        var samplerList = AppServices.Settings.Get("comfy.samplers", new List<string>());
        if (samplerList.Count == 0) samplerList = new List<string> { "euler", "euler_ancestral", "dpmpp_2m", "ddim", "uni_pc" };
        var selectedSampler = AppServices.Settings.Get("comfy.sampler", samplerList[0]);
        if (!samplerList.Contains(selectedSampler)) selectedSampler = samplerList[0];

        col.Children.Add(UiKit.Section("参数",
            UiKit.Row("▭", "尺寸", "显存不够就调小", UiKit.Row(
                UiKit.Input(AppServices.Settings.Get("comfy.width", 1024).ToString(), v =>
                {
                    if (int.TryParse(v, out var n)) AppServices.Settings.Set("comfy.width", Math.Clamp(n, 256, 4096));
                }, 70),
                UiKit.Text("×", 13, secondary: true),
                UiKit.Input(AppServices.Settings.Get("comfy.height", 1024).ToString(), v =>
                {
                    if (int.TryParse(v, out var n)) AppServices.Settings.Set("comfy.height", Math.Clamp(n, 256, 4096));
                }, 70),
                UiKit.Btn("1024×1024", () =>
                {
                    AppServices.Settings.Set("comfy.width", 1024);
                    AppServices.Settings.Set("comfy.height", 1024);
                    host.RefreshCurrent();
                }),
                UiKit.Btn("512×512", () =>
                {
                    AppServices.Settings.Set("comfy.width", 512);
                    AppServices.Settings.Set("comfy.height", 512);
                    host.RefreshCurrent();
                }))),
            UiKit.InputRow("🪜", "步数", "20 左右够用，30+ 更细但更慢",
                AppServices.Settings.Get("comfy.steps", 20).ToString(), v =>
                {
                    if (int.TryParse(v, out var n)) AppServices.Settings.Set("comfy.steps", Math.Clamp(n, 1, 150));
                }, 80),
            UiKit.SliderRow("🎚", "CFG", "提示词影响力，通常 5–9", 1, 20, AppServices.Settings.Get("comfy.cfg", 7.0),
                v => AppServices.Settings.Set("comfy.cfg", Math.Round(v, 1))),
            UiKit.ComboRow("🌀", "采样器", null, samplerList, selectedSampler,
                v => AppServices.Settings.Set("comfy.sampler", v)),
            UiKit.InputRow("🎲", "随机种子", "留空＝每次随机", AppServices.Settings.Get("comfy.seed", ""),
                v => AppServices.Settings.Set("comfy.seed", v), 140),
            UiKit.InputRow("🧺", "一次出几张", "1–8", AppServices.Settings.Get("comfy.batch", 1).ToString(), v =>
            {
                if (int.TryParse(v, out var n)) AppServices.Settings.Set("comfy.batch", Math.Clamp(n, 1, 8));
            }, 70)));

        var progressText = UiKit.Text("", 12.5, secondary: true);
        var generateBtn = UiKit.Btn("开始出图", () => { }, true);
        generateBtn.Click += async (_, _) => await RunGenerateAsync(host, client, generateBtn, progressText);

        col.Children.Add(UiKit.Section("生成",
            UiKit.Row("✨", "开始出图", "用上面的模型与参数生成图片", generateBtn),
            UiKit.Row("📋", "进度", null, progressText),
            UiKit.ToggleRow("📦", "出图后自动放进第一个收纳盒", "会在盒子里建一个「AI 出图」子文件夹",
                AppServices.Settings.Get("comfy.sendToBox", false), v => AppServices.Settings.Set("comfy.sendToBox", v))));

        col.Children.Add(UiKit.Section("最近出图", BuildGallery(host, outputDir)));
        return col;
    }

    private static async Task RunGenerateAsync(MainWindow host, ComfyUiClient client, Button generateBtn, TextBlock progressText)
    {
        try
        {
            generateBtn.IsEnabled = false;
            var model = AppServices.Settings.Get("comfy.model", "");
            if (string.IsNullOrWhiteSpace(model) || model.StartsWith('（'))
            {
                Ui.Notifications.Toast("请先点「测试连接」读取模型，并在下拉里选一个", 5);
                return;
            }

            progressText.Text = "正在提交任务…";
            var options = new ComfyUiClient.Txt2ImgOptions(
                model,
                AppServices.Settings.Get("comfy.positive", ""),
                AppServices.Settings.Get("comfy.negative", ""),
                AppServices.Settings.Get("comfy.width", 1024),
                AppServices.Settings.Get("comfy.height", 1024),
                AppServices.Settings.Get("comfy.steps", 20),
                AppServices.Settings.Get("comfy.cfg", 7.0),
                AppServices.Settings.Get("comfy.sampler", "euler"),
                "normal",
                long.TryParse(AppServices.Settings.Get("comfy.seed", ""), out var seed) ? seed : null,
                AppServices.Settings.Get("comfy.batch", 1));

            var (ok, promptId, message) = await client.QueueTxt2ImgAsync(options);
            if (!ok || promptId is null)
            {
                progressText.Text = "提交失败：" + message;
                Ui.Notifications.Toast("提交失败：" + message, 6);
                return;
            }

            progressText.Text = "已排队，等待出图…";
            var (done, images, waitMessage) = await client.WaitForResultAsync(promptId, TimeSpan.FromMinutes(10),
                p => progressText.Text = p);
            if (!done)
            {
                progressText.Text = waitMessage;
                return;
            }

            var dir = AppServices.Settings.Get("comfy.outputDir", DefaultOutputDir());
            Directory.CreateDirectory(dir);
            var saved = new List<string>();
            foreach (var img in images)
            {
                var bytes = await client.DownloadAsync(img);
                if (bytes is null) continue;
                var file = FileOps.UniquePath(dir, img.FileName);
                File.WriteAllBytes(file, bytes);
                saved.Add(file);
            }
            progressText.Text = $"出图完成：{saved.Count} 张 → {dir}";
            Ui.Notifications.Toast($"已生成 {saved.Count} 张，保存到 {dir}", 6,
                action: () => OpenFolder(dir), actionText: "打开目录");

            if (AppServices.Settings.Get("comfy.sendToBox", false) && saved.Count > 0)
                MoveToFirstBox(saved);

            host.RefreshCurrent();
        }
        catch (Exception ex)
        {
            progressText.Text = "出图失败：" + ex.Message;
            Log.Error("出图失败：" + ex.Message);
        }
        finally
        {
            generateBtn.IsEnabled = true;
        }
    }

    private static void MoveToFirstBox(List<string> files)
    {
        try
        {
            if (AppServices.Repo.Boxes().FirstOrDefault() is not { } box) return;
            var target = Path.Combine(BoxWindow.StorageRootOf(box), "AI 出图");
            foreach (var file in files)
            {
                var res = AppServices.FileOps.MoveTo(file, target);
                if (!res.Ok) continue;
                AppServices.Repo.Upsert(new ItemRecord
                {
                    BoxId = box.Id,
                    Kind = ItemKind.File,
                    DisplayName = Path.GetFileName(res.StoredPath!),
                    SourcePath = file,
                    StoredPath = res.StoredPath,
                    Ext = Path.GetExtension(file).ToLowerInvariant(),
                    SizeBytes = new FileInfo(res.StoredPath!).Length,
                    AddedAt = DateTimeOffset.Now
                });
            }
            (Application.Current as App)?.Desktop.ReloadBoxes();
        }
        catch (Exception ex)
        {
            Log.Warn("出图后放入盒子失败：" + ex.Message);
        }
    }

    public static string DefaultOutputDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DeskNest 出图");

    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("打开目录失败：" + ex.Message);
        }
    }
}
