using System.Windows;
using System.Windows.Controls;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

public static class PerfPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("性能监视", "给自己看的仪表盘：想看什么、怎么排，都由你决定。"));

        var readout = UiKit.Text("", 13);
        void Update()
        {
            var s = AppServices.Perf.Latest;
            readout.Text = s is null
                ? "还没有采样数据（开启下面的开关后每秒刷新一次）"
                : $"CPU {s.CpuPercent:0.#}%　内存 {s.MemoryPercent:0.#}%（{UiKit.Fmt(s.MemoryUsedBytes)} / {UiKit.Fmt(s.MemoryTotalBytes)}）\n" +
                  $"网络 ↑{Rate(s.NetUpBytesPerSec)} ↓{Rate(s.NetDownBytesPerSec)}" +
                  (s.GpuPercent is { } gpu ? $"　GPU {gpu}%" + (s.GpuTempC is { } gt ? $"　{gt:0}°C" : "") : "　GPU 需 NVIDIA 驱动");
        }
        Update();
        AppServices.Perf.Sampled += _ => Update();

        col.Children.Add(UiKit.Section("实时读数",
            UiKit.ToggleRow("📈", "启用性能监视", "关闭后完全不采样",
                AppServices.Modules.IsEnabled(ModuleIds.PerfMonitor), v =>
                {
                    AppServices.Modules.SetEnabled(ModuleIds.PerfMonitor, v);
                    if (v) AppServices.Perf.Start(); else AppServices.Perf.Stop();
                }),
            UiKit.ComboRow("⏱", "采样间隔", "间隔越大越省电", new[] { "500", "1000", "2000", "5000" },
                AppServices.Perf.IntervalMs.ToString(), v =>
                {
                    if (int.TryParse(v, out var ms))
                    {
                        AppServices.Perf.IntervalMs = ms;
                        AppServices.Settings.Set("perf.intervalMs", ms);
                    }
                }),
            UiKit.Row("📊", "当前状态", null, readout)));

        col.Children.Add(UiKit.Section("桌面上显示什么",
            UiKit.ToggleRow("🖥", "CPU", null, AppServices.Settings.Get("perf.show.cpu", true), v => AppServices.Settings.Set("perf.show.cpu", v)),
            UiKit.ToggleRow("💾", "内存", null, AppServices.Settings.Get("perf.show.mem", true), v => AppServices.Settings.Set("perf.show.mem", v)),
            UiKit.ToggleRow("🌐", "网络", null, AppServices.Settings.Get("perf.show.net", true), v => AppServices.Settings.Set("perf.show.net", v)),
            UiKit.ToggleRow("🎮", "GPU", "需要 NVIDIA 驱动", AppServices.Settings.Get("perf.show.gpu", true), v => AppServices.Settings.Set("perf.show.gpu", v)),
            UiKit.ComboRow("🧱", "布局", "紧凑横条 / 竖条 / 网格 / 纯文字",
                new[] { "紧凑横条", "竖条", "网格", "纯文字" }, AppServices.Settings.Get("perf.layout", "紧凑横条"),
                v => AppServices.Settings.Set("perf.layout", v)),
            UiKit.ActionRow("🧩", "把性能面板放到桌面", "添加成桌面组件，随时能看到", "添加", () =>
            {
                var widget = new WidgetRecord
                {
                    Kind = WidgetKind.SystemMonitor,
                    Name = "性能",
                    Width = 240,
                    Height = 150,
                    X = 240,
                    Y = 260
                };
                AppServices.Repo.Upsert(widget);
                (Application.Current as App)?.Desktop.ShowWidget(widget);
                Ui.Notifications.Toast("已添加性能组件", 3);
            })));
        return col;
    }

    private static string Rate(double bytes) => bytes switch
    {
        < 1024 => $"{bytes:0} B/s",
        < 1024 * 1024 => $"{bytes / 1024:0.#} KB/s",
        _ => $"{bytes / 1024 / 1024:0.##} MB/s"
    };
}

public static class UsagePage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("使用统计", "类似 ManicTime：你每天在各软件上花了多久。默认只记程序名，数据不离开本机。"));

        col.Children.Add(UiKit.Section("开关与清理",
            UiKit.ToggleRow("⏱", "启用统计", "关闭后立即停止采样",
                AppServices.Modules.IsEnabled(ModuleIds.UsageStats), v =>
                {
                    AppServices.Modules.SetEnabled(ModuleIds.UsageStats, v);
                    if (v) AppServices.Usage.Start(); else AppServices.Usage.Stop();
                    host.RefreshCurrent();
                }),
            UiKit.ToggleRow("🏷", "记录窗口标题", "默认关：只记程序名更隐私",
                AppServices.Settings.Get("usage.recordTitles", false), v => AppServices.Settings.Set("usage.recordTitles", v)),
            UiKit.ActionRow("🗑", "清空统计", "把已经记录的数据全部删掉", "清空", () =>
            {
                AppServices.Usage.Clear();
                host.RefreshCurrent();
                Ui.Notifications.Toast("统计已清空", 3);
            }),
            UiKit.ActionRow("📤", "导出 CSV", "导出最近 30 天的记录", "导出", Export)));

        foreach (var (label, from) in new (string, DateTimeOffset)[]
                 {
                     ("今天", DateTime.Today),
                     ("最近 7 天", DateTime.Today.AddDays(-6)),
                     ("最近 30 天", DateTime.Today.AddDays(-29))
                 })
        {
            var usage = AppServices.Usage.Aggregate(from, DateTimeOffset.Now);
            var rows = new List<UIElement>();
            if (usage.Count == 0)
            {
                rows.Add(UiKit.Row("📭", "暂无数据", "开启统计后，这里会出现应用排行", null));
            }
            else
            {
                rows.Add(UiKit.Row("Σ", $"合计 {UiKit.FmtSecs(usage.Sum(u => u.Seconds))}", $"{usage.Count} 个应用", null));
                foreach (var app in usage.Take(12))
                    rows.Add(UiKit.Row("▫", app.ProcessName,
                        $"{UiKit.FmtSecs(app.Seconds)} · {app.Share:0.#}% · {app.Sessions} 次", null));
            }
            col.Children.Add(UiKit.Section(label, rows.ToArray()));
        }
        return col;
    }

    private static void Export()
    {
        try
        {
            var file = Path.Combine(AppPaths.DataRoot, $"usage-{DateTime.Now:yyyyMMdd-HHmm}.csv");
            var lines = new List<string> { "进程,开始时间,结束时间,时长(秒)" };
            foreach (var s in AppServices.Usage.Timeline(DateTime.Today.AddDays(-30), DateTimeOffset.Now))
                lines.Add($"{s.ProcessName},{s.StartAt.LocalDateTime:yyyy-MM-dd HH:mm:ss},{s.EndAt.LocalDateTime:yyyy-MM-dd HH:mm:ss},{s.DurationSeconds}");
            File.WriteAllLines(file, lines);
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
            Ui.Notifications.Toast("已导出：" + file, 5);
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("导出失败：" + ex.Message);
        }
    }
}

public static class ApiPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("API 面板", "看各模型的调用情况与额度；密钥用 Windows 的 DPAPI 加密后只存本机，界面默认打码。"));

        col.Children.Add(UiKit.Section("添加模型",
            UiKit.ActionRow("🐋", "DeepSeek", "https://api.deepseek.com/v1", "添加",
                () => Add("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", host)),
            UiKit.ActionRow("🌐", "OpenAI 兼容接口", "任何兼容 /chat/completions 的服务", "添加",
                () => Add("自定义", "https://", "", host)),
            UiKit.ToggleRow("🤖", "用模型做智能分类", "整理时可以先给一份「按语义分类」的建议，你确认再执行",
                AppServices.Settings.Get("ai.classify", false), v => AppServices.Settings.Set("ai.classify", v))));

        var rows = new List<UIElement>();
        var providers = AppServices.Repo.Providers();
        if (providers.Count == 0)
            rows.Add(UiKit.Row("📭", "还没有配置模型", "添加一个后就能给桌宠问答、智能分类用了", null));
        foreach (var provider in providers)
        {
            var captured = provider;
            var stat = AppServices.Api.Stat(captured.Id);
            rows.Add(UiKit.Row("🔑", captured.Name,
                $"密钥 {AppServices.Api.KeyPreview(captured) ?? "未设置"} · 近 7 天调用 {stat.Calls} 次 · 成功率 {stat.SuccessRate:0.#}% · 平均 {stat.AvgLatencyMs} 毫秒",
                UiKit.Row(
                    UiKit.Toggle(captured.Enabled, v =>
                    {
                        captured.Enabled = v;
                        AppServices.Repo.Upsert(captured);
                    }),
                    UiKit.Btn("设置密钥", () => SetKey(host, captured)),
                    UiKit.Btn("测试", async () =>
                    {
                        Ui.Notifications.Toast("正在测试…", 2);
                        var (ok, content, error, ms) = await AppServices.Api.ChatAsync(captured, "只回答两个字", "你好");
                        Ui.Notifications.Toast(ok ? $"成功（{ms} ms）：{content}" : "失败：" + error, 6);
                        host.RefreshCurrent();
                    }),
                    UiKit.Btn("删除", () =>
                    {
                        AppServices.Api.Delete(captured.Id);
                        host.RefreshCurrent();
                    }))));
        }
        col.Children.Add(UiKit.Section("已配置", rows.ToArray()));
        return col;
    }

    private static void Add(string name, string url, string model, MainWindow host)
    {
        AppServices.Repo.Upsert(new ApiProviderRecord { Name = name, BaseUrl = url, Model = model });
        Ui.Notifications.Toast($"已添加 {name}，点「设置密钥」填入", 4);
        host.RefreshCurrent();
    }

    private static void SetKey(MainWindow host, ApiProviderRecord provider)
    {
        var input = UiKit.Input("", _ => { }, 320);
        var win = new Window
        {
            Title = "设置 API 密钥（" + provider.Name + "）",
            Width = 460,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Current.BackgroundBrush,
            Foreground = Theme.Current.ForegroundBrush
        };
        var save = UiKit.Btn("保存", () => { }, true);
        save.Click += (_, _) =>
        {
            AppServices.Api.Save(provider, input.Text.Trim());
            win.Close();
            Ui.Notifications.Toast("密钥已加密保存", 3);
            host.RefreshCurrent();
        };
        win.Content = UiKit.Col(
            UiKit.PageHeader("设置 API 密钥", "保存后立即用 Windows 的 DPAPI 加密，只在本机可用。"),
            UiKit.Section("密钥", UiKit.Row("🔑", "粘贴密钥", provider.BaseUrl, input)),
            UiKit.Row("", "", null, UiKit.Row(save, UiKit.Btn("取消", () => win.Close()))));
        win.ShowDialog();
    }
}

public static class PowerPage
{
    public static UIElement Build(MainWindow host)
    {
        var plan = AppServices.Power.Plan;
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("计划关机", "到点之前一定会提醒你；可以再等 10 分钟，也可以一键取消。"));

        col.Children.Add(UiKit.Section("计划",
            UiKit.ToggleRow("⏻", "启用计划动作", "关闭后不会有任何倒计时",
                plan.Enabled, v =>
                {
                    plan.Enabled = v;
                    AppServices.Power.Apply(plan);
                }),
            UiKit.ComboRow("⏱", "触发方式", "定时 / 倒计时 / 空闲后",
                UiOptions.PowerTriggers, plan.Trigger.ToString(), v =>
                {
                    if (Enum.TryParse<PowerTriggerKind>(v, out var kind)) plan.Trigger = kind;
                }),
            UiKit.InputRow("🕘", "定时时间", "格式 HH:mm，例如 23:30", plan.ClockTime, v => plan.ClockTime = v, 90),
            UiKit.ToggleRow("📅", "仅工作日生效", null, plan.WeekdaysOnly, v => plan.WeekdaysOnly = v),
            UiKit.InputRow("⏳", "倒计时（分钟）", null, plan.CountdownMinutes.ToString(), v =>
            {
                if (int.TryParse(v, out var m)) plan.CountdownMinutes = Math.Max(1, m);
            }, 80),
            UiKit.InputRow("💤", "空闲多久后触发（分钟）", null, plan.IdleMinutes.ToString(), v =>
            {
                if (int.TryParse(v, out var m)) plan.IdleMinutes = Math.Max(1, m);
            }, 80),
            UiKit.InputRow("🔔", "提前几分钟提醒", null, plan.WarnMinutesBefore.ToString(), v =>
            {
                if (int.TryParse(v, out var m)) plan.WarnMinutesBefore = Math.Clamp(m, 1, 60);
            }, 80),
            UiKit.ComboRow("🎯", "执行动作", null, UiOptions.PowerActions, plan.Action.ToString(), v =>
            {
                if (Enum.TryParse<PowerActionKind>(v, out var action)) plan.Action = action;
            }),
            UiKit.ToggleRow("💾", "执行前自动备份数据", "推荐开启", plan.BackupBefore, v => plan.BackupBefore = v)));

        col.Children.Add(UiKit.Section("操作",
            UiKit.ActionRow("✅", "保存并启用", "设置立即生效", "保存", () =>
            {
                AppServices.Power.Apply(plan);
                Ui.Notifications.Toast(plan.Enabled ? $"已启用：{plan.Trigger} → {plan.Action}" : "计划动作已关闭", 4);
                host.RefreshCurrent();
            }, true),
            UiKit.ActionRow("🧪", "测试提醒（不会真的关机）", "看看提醒气泡长什么样", "测试", () =>
            {
                AppServices.Power.Postpone(0);
                Ui.Notifications.Toast($"测试：{plan.WarnMinutesBefore} 分钟后将执行 {plan.Action}", 6);
            }),
            UiKit.ActionRow("🛑", "取消已计划动作", "顺手把系统层面的关机计划也取消", "取消", () =>
            {
                AppServices.Power.Cancel();
                Native.Win32.PowerActions.AbortPendingShutdown();
                Ui.Notifications.Toast("已取消", 3);
                host.RefreshCurrent();
            })));

        var deadline = AppServices.Power.Deadline;
        col.Children.Add(UiKit.Section("当前状态",
            UiKit.Row("📌", "倒计时", deadline is null ? "没有正在倒计时的计划" : deadline.Value.LocalDateTime.ToString("MM-dd HH:mm:ss"), null)));
        return col;
    }
}

public static class AiPage
{
    public static UIElement Build(MainWindow host)
    {
        var bridge = AppServices.AiBridge;
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("AI 接口", "让本机的 Codex / dsh / Claude 读取桌巢的数据（只读、仅本机、需要令牌）。"));

        col.Children.Add(UiKit.Section("服务",
            UiKit.ToggleRow("🔗", "启用本地接口", "只监听 127.0.0.1，不会对外网开放",
                AppServices.Modules.IsEnabled(ModuleIds.AiBridge), v =>
                {
                    AppServices.Modules.SetEnabled(ModuleIds.AiBridge, v);
                    if (v) bridge.Start(); else bridge.Stop();
                    host.RefreshCurrent();
                }),
            UiKit.Row("🌐", "地址", bridge.Endpoint + (bridge.Running ? "（运行中）" : "（已停止）"), null),
            UiKit.Row("🔑", "访问令牌", null, UiKit.Row(
                UiKit.Text(bridge.Token, 12),
                UiKit.Btn("复制", () =>
                {
                    Clipboard.SetText(bridge.Token);
                    Ui.Notifications.Toast("令牌已复制", 2);
                }),
                UiKit.Btn("重新生成", () =>
                {
                    bridge.RegenerateToken();
                    host.RefreshCurrent();
                    Ui.Notifications.Toast("已生成新令牌（旧的立即失效）", 4);
                })))));

        col.Children.Add(UiKit.Section("允许读取",
            UiKit.ToggleRow("🗂", "收纳盒列表", null, bridge.Allowed("boxes"), v => bridge.SetAllowed("boxes", v)),
            UiKit.ToggleRow("📄", "盒内文件元数据", "名称/类型/大小/时间，不含文件内容", bridge.Allowed("items"), v => bridge.SetAllowed("items", v)),
            UiKit.ToggleRow("✅", "待办", null, bridge.Allowed("todos"), v => bridge.SetAllowed("todos", v)),
            UiKit.ToggleRow("📝", "随记", null, bridge.Allowed("notes"), v => bridge.SetAllowed("notes", v)),
            UiKit.ToggleRow("⏱", "使用统计", null, bridge.Allowed("usage"), v => bridge.SetAllowed("usage", v))));

        col.Children.Add(UiKit.Section("怎么接",
            UiKit.Row("💻", "命令行示例", null, UiKit.Text($"curl -H \"X-DeskNest-Token: {bridge.Token}\" {bridge.Endpoint}/boxes", 11.5, secondary: true)),
            UiKit.Row("📚", "可用接口", null, UiKit.Text("/health · /boxes · /items?box=<id> · /todos · /notes · /usage", 11.5, secondary: true))));
        return col;
    }
}

public static class DataPage
{
    private static long DatabaseBytes()
    {
        try
        {
            var file = new FileInfo(AppPaths.DbFile);
            return file.Exists ? file.Length : 0;
        }
        catch { return 0; }
    }

    private static string DatabaseSizeText() =>
        DatabaseBytes() is var size && size > 0 ? "当前 " + UiKit.Fmt(size) : "数据库还没生成";

    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("数据与备份", "所有数据都在你本机；备份就是一个 zip，随时可以回滚。"));

        col.Children.Add(UiKit.Section("位置",
            UiKit.Row("📂", "数据目录", AppPaths.DataRoot, UiKit.Btn("打开",
                () => System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.DataRoot + "\""))),
            UiKit.Row("🧾", "运行日志", "排错用；导出诊断包会自动带上", UiKit.Btn("查看", () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + AppPaths.LogFile + "\""); }
                catch (Exception ex) { Ui.Notifications.Toast("打开失败：" + ex.Message); }
            }))));

        col.Children.Add(UiKit.Section("备份",
            UiKit.InputRow("🔢", "保留份数", "超出会自动删最早的", AppServices.Settings.Get("backup.retention", 7).ToString(), v =>
            {
                if (int.TryParse(v, out var n)) AppServices.Settings.Set("backup.retention", Math.Clamp(n, 1, 90));
            }, 70),
            UiKit.ToggleRow("🚪", "退出时自动备份", null, AppServices.Settings.Get("backup.onExit", false),
                v => AppServices.Settings.Set("backup.onExit", v)),
            UiKit.ActionRow("💾", "立即备份", "生成一份包含设置与数据库的 zip", "备份", () =>
            {
                var info = AppServices.Backup.Create("manual");
                Ui.Notifications.Toast($"备份完成：{Path.GetFileName(info.Path)}（{UiKit.Fmt(info.SizeBytes)}）", 5);
                host.RefreshCurrent();
            }, true)));

        var rows = new List<UIElement>();
        var backups = AppServices.Backup.List();
        if (backups.Count == 0) rows.Add(UiKit.Row("📭", "还没有备份", "点上面的「立即备份」试试", null));
        foreach (var backup in backups.Take(15))
        {
            var captured = backup;
            rows.Add(UiKit.Row("🗄", captured.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                Path.GetFileName(captured.Path) + " · " + UiKit.Fmt(captured.SizeBytes),
                UiKit.Btn("回滚到这份", () =>
                {
                    var ok = MessageBox.Show("回滚会覆盖当前数据（会先自动备份一份现状）。继续？", "回滚备份",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (ok != MessageBoxResult.OK) return;
                    var (success, message) = AppServices.Backup.Restore(captured.Path);
                    Ui.Notifications.Toast(message, 6);
                })));
        }
        col.Children.Add(UiKit.Section("备份列表", rows.ToArray()));

        col.Children.Add(UiKit.Section("诊断",
            UiKit.ActionRow("🧰", "导出诊断包", "只含日志、设置与环境信息，不含任何文件内容", "导出", () =>
            {
                try
                {
                    var file = AppServices.Backup.ExportDiagnostics();
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
                    Ui.Notifications.Toast("已导出：" + file, 5);
                }
                catch (Exception ex) { Ui.Notifications.Toast("导出失败：" + ex.Message); }
            }),
            UiKit.ActionRow("🧹", "压缩数据库", DatabaseSizeText() + "；全盘索引每次重写都会留下空闲页，压缩后还给磁盘",
                "压缩", () =>
                {
                    var before = DatabaseBytes();
                    try
                    {
                        AppServices.Db.Vacuum();
                        var after = DatabaseBytes();
                        Ui.Notifications.Toast($"数据库已压缩：{UiKit.Fmt(before)} → {UiKit.Fmt(after)}", 6);
                    }
                    catch (Exception ex)
                    {
                        Ui.Notifications.Toast("压缩失败（可能正在建索引）：" + ex.Message, 6);
                    }
                    host.RefreshCurrent();
                })));
        return col;
    }
}

public static class AboutPage
{
    private static readonly UpdateService Updater = new();

    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader(AppInfo.ProductName,
            $"版本 {AppInfo.Version} · MIT 开源 · 数据全部留在本机"));

        // 更新结果就地显示在这一块里，按钮点完不用再找地方看结果
        var resultBox = new StackPanel { Margin = new Thickness(0, Tokens.Space4, 0, 0) };
        resultBox.Children.Add(UiKit.Text(LastCheckText(), Tokens.FontDescription, secondary: true));

        var checkBtn = UiKit.Btn("检查更新", () => { }, true);
        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            checkBtn.Content = "正在检查…";
            resultBox.Children.Clear();
            resultBox.Children.Add(UiKit.Text("正在向 GitHub 查询最新发布版…", Tokens.FontDescription, secondary: true));

            var result = await Task.Run(() => Updater.CheckAsync(AppInfo.Version));
            AppServices.Settings.Set("update.lastCheck", DateTimeOffset.Now);

            checkBtn.Content = "检查更新";
            checkBtn.IsEnabled = true;
            resultBox.Children.Clear();

            if (result.Failed)
            {
                resultBox.Children.Add(UiKit.Banner(BannerKind.Warning, result.Message));
            }
            else if (result.Update is { } info && VersionCompare.IsNewer(info.Version, AppInfo.Version))
            {
                resultBox.Children.Add(UiKit.Banner(BannerKind.Success,
                    $"发现新版本 {info.Version}" + (info.PublishedAt is { } at ? $"（发布于 {at.LocalDateTime:yyyy-MM-dd}）" : "")));
                var notes = string.IsNullOrWhiteSpace(info.Notes) ? "这次发布没有写更新说明。" : Shorten(info.Notes!, 260);
                resultBox.Children.Add(UiKit.Text(notes, Tokens.FontDescription, secondary: true));
                resultBox.Children.Add(UiKit.Row(
                    UiKit.Btn("打开下载页", () => OpenUrl(info.Url), true),
                    UiKit.Btn("稍后再说", () => { })));
            }
            else
            {
                resultBox.Children.Add(UiKit.Banner(BannerKind.Info, result.Message));
                resultBox.Children.Add(UiKit.Row(UiKit.Btn("查看更新日志", () => OpenUrl(Updater.ChangelogUrl))));
            }

            resultBox.Children.Add(UiKit.Text(LastCheckText(), Tokens.FontDescription, secondary: true));
        };

        col.Children.Add(UiKit.Section("版本与更新",
            UiKit.Row(Icons.Download, "当前版本", $"{AppInfo.Version}（开源免费，MIT 许可）", null),
            UiKit.Row(Icons.Refresh, "检查更新", $"查询 GitHub 上的最新发布版：{Updater.Owner}/{Updater.Repo}",
                UiKit.Row(checkBtn, UiKit.Btn("更新日志", () => OpenUrl(Updater.ChangelogUrl)))),
            UiKit.ToggleRow(Icons.Clock, "启动时自动检查更新",
                "默认关闭；打开后每次启动静默查一次，只有真的有新版本才提示你",
                AppServices.Settings.Get("update.autoCheck", false),
                v =>
                {
                    AppServices.Settings.Set("update.autoCheck", v);
                    Ui.Notifications.Toast(v ? "已打开：启动时会自动检查更新" : "已关闭自动检查更新", 3);
                })));

        col.Children.Add(resultBox);

        col.Children.Add(UiKit.Section("原则",
            UiKit.Row("↩", "一切可撤销", "整理有历史，删除有 10 秒撤销", null),
            UiKit.Row("🔘", "一切可关闭", "每个模块、每个动效、每个热键都能关", null),
            UiKit.Row("🔒", "数据在你本机", "不上传、不统计、不追踪", null),
            UiKit.Row("🧬", "不注入其它程序", "桌面层用普通窗口实现，卸载即干净", null),
            UiKit.Row("🚫", "永久不做", "广告、推广位、系统右键菜单注入、基础功能付费墙", null),
            UiKit.Row(Icons.Upload, "联网只有两件事", "查天气（默认关）与检查更新（点了才查）；都不上传任何本机信息", null)));

        col.Children.Add(UiKit.Section("环境",
            UiKit.Row(Icons.Monitor, "系统", WindowsName(), null),
            UiKit.Row("⚙", "运行时", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, null),
            UiKit.Row(Icons.Folder, "数据目录", AppPaths.DataRoot, UiKit.Btn("打开", () => OpenUrl(AppPaths.DataRoot))),
            UiKit.Row(Icons.Globe, "项目主页 / 反馈", Updater.ProjectUrl, UiKit.Btn("打开", () => OpenUrl(Updater.ProjectUrl))),
            UiKit.ActionRow("🎬", "重新查看引导", "下次启动会重新显示引导页", "重置", () =>
            {
                AppServices.Settings.Set("onboarding.done", false);
                Ui.Notifications.Toast("已重置，下次启动会重新引导", 4);
            })));
        return col;
    }

    /// <summary>"Microsoft Windows NT 10.0.22631.0" 这种给开发看的东西要翻译成人话。</summary>
    private static string WindowsName()
    {
        var v = Environment.OSVersion.Version;
        var build = v.Build;
        var name = build >= 22000 ? "Windows 11" : build >= 10240 ? "Windows 10" : "Windows";
        return $"{name}（内部版本 {build}）";
    }

    private static string LastCheckText()
    {
        var last = AppServices.Settings.Get<DateTimeOffset?>("update.lastCheck", null);
        return last is { } at ? $"上次检查：{at.LocalDateTime:yyyy-MM-dd HH:mm}" : "还没有检查过更新";
    }

    private static string Shorten(string text, int max)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { Ui.Notifications.Toast("打开失败：" + ex.Message); }
    }
}
