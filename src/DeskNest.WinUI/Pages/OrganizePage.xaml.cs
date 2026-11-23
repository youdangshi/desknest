using System;
using System.IO;
using System.Linq;
using DeskNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 整理页：扫描桌面 → 预览 → 执行 → 撤销，外加规则与自动化设置。
/// 引擎与规则都是 Core 现成实现（与 WPF 冻结版同一份、数据互通），这里只做 WinUI 界面。
/// </summary>
public sealed partial class OrganizePage : Page
{
    private DeskNest.Core.Services.OrganizeEngine.Plan? _plan;
    private DeskNest.Core.OrganizeRunRecord? _lastRun;
    private StackPanel? _previewHost;
    private TextBlock? _resultText;

    public OrganizePage()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        Host.Children.Add(PageKit.PageTitle("整理桌面"));
        Host.Children.Add(PageKit.PageIntro("先把要动的文件列出来给你看，确认后才真正搬；整理后随时能一键撤销。快捷方式默认不动。"));

        // 预览 / 执行
        var actions = PageKit.Column(10);
        actions.Children.Add(PageKit.ActionRow("试算预览", "只统计不搬动，看看会整理哪些文件",
            PageKit.Btn("扫描桌面", (_, _) => Preview())));
        actions.Children.Add(PageKit.Divider());
        actions.Children.Add(PageKit.ActionRow("执行整理", "按刚才的预览结果搬移文件；没预览过会先试算",
            PageKit.Btn("开始整理", (_, _) => Execute(), primary: true)));
        _previewHost = PageKit.Column(8);
        actions.Children.Add(_previewHost);
        _resultText = PageKit.Hint("");
        actions.Children.Add(_resultText);
        Host.Children.Add(PageKit.Card("一键整理", actions));

        // 设置（与 WPF 版共用同一批 organize.* 键）
        var settings = PageKit.Column(10);
        settings.Children.Add(PageKit.ActionRow("自动整理", "打开本页时按间隔检查一次（默认关）",
            Toggle("organize.auto", false)));
        settings.Children.Add(PageKit.ActionRow("检查间隔", "分钟；5–1440", NumberBox("organize.autoMinutes", 30, 5, 1440)));
        settings.Children.Add(PageKit.ActionRow("也收纳快捷方式", "默认不收，快捷方式留在桌面",
            Toggle("organize.includeShortcuts", false)));
        settings.Children.Add(PageKit.ActionRow("用文件夹归集", "开＝按类型在桌面建文件夹；关＝收进桌面盒子",
            Toggle("organize.folderMode", false)));
        Host.Children.Add(PageKit.Card("方式与自动化", settings));

        // 规则
        var rules = PageKit.Column(10);
        var list = App.Repo?.Rules() ?? new();
        if (list.Count == 0)
            rules.Children.Add(PageKit.Hint("还没有规则。规则先于默认分类生效，可把特定文件固定送到某处。"));
        foreach (var r in list)
        {
            var captured = r;
            var cond = DeskNest.Core.Services.RulesEngine.ParseConditions(r.ConditionsJson);
            var desc = $"名称含 {string.Join("/", cond.NameContains)} · 扩展 {string.Join("/", cond.Extensions)} · 命中 {r.HitCount} 次";
            var row = PageKit.Column(4);
            row.Children.Add(PageKit.InfoRow(r.Name, desc));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(PageKit.Btn(captured.Enabled ? "停用" : "启用", (_, _) =>
            {
                captured.Enabled = !captured.Enabled;
                App.Repo?.Upsert(captured);
                Rebuild();
            }));
            buttons.Children.Add(PageKit.Btn("删除", (_, _) =>
            {
                App.Repo?.DeleteRule(captured.Id);
                Rebuild();
            }));
            rules.Children.Add(buttons);
        }
        rules.Children.Add(PageKit.Divider());
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        quick.Children.Add(PageKit.Btn("＋ 截图 → 图片", (_, _) => QuickRule("截图", "截图")));
        quick.Children.Add(PageKit.Btn("＋ 安装包 → 程序", (_, _) => QuickRule("安装包", "安装包")));
        rules.Children.Add(quick);
        Host.Children.Add(PageKit.Card("规则", rules));

        // 历史 + 回滚
        var history = PageKit.Column(10);
        var runs = App.Repo?.Runs(20) ?? new();
        if (runs.Count == 0)
            history.Children.Add(PageKit.Hint("还没有整理记录。"));
        foreach (var run in runs)
        {
            var captured = run;
            var canUndo = !captured.Undone && captured.MovedCount > 0;
            var row = PageKit.Column(4);
            row.Children.Add(PageKit.InfoRow(captured.StartedAt.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                $"{captured.MovedCount} 项 · {(captured.Undone ? "已回滚" : canUndo ? "可回滚" : "—")}"));
            if (canUndo)
                row.Children.Add(PageKit.Btn("回滚这一条", (_, _) => Undo(captured)));
            history.Children.Add(row);
        }
        Host.Children.Add(PageKit.Card("整理历史", history));
    }

    private void Rebuild()
    {
        Host.Children.Clear();
        Build();
    }

    private ToggleSwitch Toggle(string key, bool fallback)
    {
        var t = new ToggleSwitch { IsOn = App.Settings?.Get(key, fallback) ?? fallback, OnContent = "开", OffContent = "关" };
        t.Toggled += (_, _) =>
        {
            App.Settings?.Set(key, t.IsOn);
            App.Log($"设置 {key}={t.IsOn}");
        };
        return t;
    }

    private TextBox NumberBox(string key, int fallback, int min, int max)
    {
        var value = App.Settings?.Get(key, fallback) ?? fallback;
        var box = new TextBox { Text = value.ToString(), Width = 90 };
        box.LosingFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, out var n))
            {
                n = Math.Clamp(n, min, max);
                App.Settings?.Set(key, n);
                box.Text = n.ToString();
            }
            else box.Text = value.ToString();
        };
        return box;
    }

    private static DeskNest.Core.Services.OrganizeEngine.ScanOptions CurrentOptions() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        IncludeShortcuts: App.Settings?.Get("organize.includeShortcuts", false) ?? false,
        IncludeHidden: false,
        UseFolderMode: App.Settings?.Get("organize.folderMode", false) ?? false);

    private void Preview()
    {
        if (App.Organizer is null) { SetResult("整理引擎未就绪"); return; }
        try
        {
            _plan = App.Organizer.BuildPlan(CurrentOptions());
            if (_previewHost is null) return;
            _previewHost.Children.Clear();
            _previewHost.Children.Add(PageKit.Hint($"将整理 {_plan.Items.Count} 项，保留 {_plan.Skipped.Count} 项"));
            foreach (var g in _plan.Items.GroupBy(i => i.Category))
                _previewHost.Children.Add(PageKit.Hint($"· {g.Key}：{g.Count()} 项"));
            foreach (var skip in _plan.Skipped.Take(8))
                _previewHost.Children.Add(PageKit.Hint("· 保留：" + skip));
            SetResult(_plan.Items.Count == 0 ? "没有需要整理的文件" : "预览完成，确认无误点「开始整理」");

            // 自动整理：打开本页时按间隔检查一次（默认关）
            if (App.Settings?.Get("organize.auto", false) == true && _plan.Items.Count > 0)
            {
                var minutes = App.Settings?.Get("organize.autoMinutes", 30) ?? 30;
                var last = App.Settings?.Get<DateTimeOffset?>("organize.lastRun", null);
                if (last is null || DateTimeOffset.Now - last.Value > TimeSpan.FromMinutes(minutes))
                    SetResult("自动整理已开启，点「开始整理」即可按当前预览执行");
            }
        }
        catch (Exception ex) { SetResult("预览失败：" + ex.Message); App.Log("整理预览失败：" + ex); }
    }

    private void Execute()
    {
        if (App.Organizer is null) { SetResult("整理引擎未就绪"); return; }
        try
        {
            _plan ??= App.Organizer.BuildPlan(CurrentOptions());
            if (_plan.Items.Count == 0) { SetResult("没有需要整理的文件"); return; }
            var run = App.Organizer.Execute(_plan, "manual");
            _lastRun = run;
            App.Settings?.Set("organize.lastRun", DateTimeOffset.Now);
            App.Log($"整理页：执行完成 {run.MovedCount} 项");
            _plan = null;
            Rebuild(); // 重建后 _resultText 是新对象，结果要在重建之后再写
            SetResult($"整理完成：{run.MovedCount} 项。可在下方「整理历史」回滚。");
        }
        catch (Exception ex) { SetResult("整理失败：" + ex.Message); App.Log("整理执行失败：" + ex); }
    }

    private void Undo(DeskNest.Core.OrganizeRunRecord run)
    {
        try
        {
            var (restored, failed) = App.Organizer?.Undo(run) ?? (0, 0);
            App.Log($"整理页：回滚 {restored} 项，失败 {failed} 项");
            Rebuild();
            SetResult($"已回滚：还原 {restored} 项" + (failed > 0 ? $"，失败 {failed} 项" : ""));
        }
        catch (Exception ex) { SetResult("回滚失败：" + ex.Message); App.Log("整理回滚失败：" + ex); }
    }

    private void QuickRule(string name, string keyword)
    {
        if (App.Repo is null) return;
        App.Repo.Upsert(new DeskNest.Core.RuleRecord
        {
            Name = name,
            ConditionsJson = DeskNest.Core.Services.RulesEngine.ToJson(
                new DeskNest.Core.Services.RuleConditions { NameContains = new() { keyword } }),
            ActionsJson = DeskNest.Core.Services.RulesEngine.ToJson(new DeskNest.Core.Services.RuleActions())
        });
        App.Log($"整理页：新建规则「{name}」");
        Rebuild();
    }

    private void SetResult(string text)
    {
        if (_resultText is not null) _resultText.Text = text;
    }
}
