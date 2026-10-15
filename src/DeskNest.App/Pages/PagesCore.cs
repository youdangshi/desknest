using System.Windows;
using System.Windows.Controls;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

public static class OverviewPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        var boxes = AppServices.Repo.Boxes();
        var widgets = AppServices.Repo.Widgets();
        var todos = AppServices.Repo.Todos();

        col.Children.Add(UiKit.PageHeader("概览", "桌巢在后台安静待着；需要它的时候，用下面的入口。"));

        col.Children.Add(UiKit.Section("桌面状态",
            UiKit.Row("🗂", $"收纳盒 {boxes.Count} 个", $"共 {AppServices.Repo.AllItems().Count} 个文件项", null,
                () => host.NavigateTo("boxes")),
            UiKit.Row("🧩", $"桌面组件 {widgets.Count} 个", $"待办 {todos.Count(t => !t.IsDone)} 项未完成", null,
                () => host.NavigateTo("widgets")),
            UiKit.Row("🔍", $"搜索索引 {AppServices.Search.IndexedCount:N0} 条",
                AppServices.Search.IsIndexing ? "正在后台建立索引…（可以正常使用）" : "已就绪，Alt+Space 呼出搜索", null,
                () => (Application.Current as App)?.ShowSearch())));

        col.Children.Add(UiKit.Section("常用操作",
            UiKit.ActionRow("⚡", "快捷搜索", "全盘搜索文件、应用与命令（Alt+Space）", "打开", () => (Application.Current as App)?.ShowSearch(), true),
            UiKit.ActionRow("🧹", "一键整理桌面", "先预览再执行，随时可以回滚", "开始", () => host.OpenOrganizeWizard()),
            UiKit.ActionRow("🆕", "新建收纳盒", "选「文件夹归集」或「桌面分区」两种方式", "新建", () => host.NavigateTo("boxes"))));

        var runs = AppServices.Repo.Runs(5);
        var rows = new List<UIElement>();
        if (runs.Count == 0)
        {
            rows.Add(UiKit.Row("🕘", "还没有整理记录", "第一次整理后这里会出现可回滚的历史", null));
        }
        else
        {
            foreach (var run in runs)
            {
                var captured = run;
                var subtitle = $"{captured.StartedAt.LocalDateTime:MM-dd HH:mm} · {captured.MovedCount} 项" +
                               (captured.Undone ? " · 已回滚" : "");
                UIElement? control = captured.Undone || captured.MovedCount == 0
                    ? null
                    : UiKit.Btn("回滚", () =>
                    {
                        var (restored, failed) = AppServices.Organizer.Undo(captured);
                        Ui.Notifications.Toast($"已回滚：还原 {restored} 项，失败 {failed} 项");
                        host.RefreshCurrent();
                    });
                rows.Add(UiKit.Row("🕘", captured.Trigger == "auto" ? "自动整理" : "手动整理", subtitle, control));
            }
        }
        col.Children.Add(UiKit.Section("最近整理（可回滚）", rows.ToArray()));

        col.Children.Add(UiKit.Section("熟悉一下",
            // 可展开行：开关 + 折叠箭头（像 DeskBox 的「开机自动启动 → 静默启动」）
            UiKit.ExpanderRow("🚀", "开机自动启动", "登录 Windows 后在后台启动",
                UiKit.Toggle(AppServices.Settings.Get("app.autoStart", false), v =>
                {
                    AppServices.Settings.Set("app.autoStart", v);
                    Native.Win32.AutoStart.SetEnabled(v);
                }),
                UiKit.ToggleRow("🌙", "静默启动", "启动时不弹主窗口，只留托盘图标",
                    AppServices.Settings.Get("app.silentStart", false), v => AppServices.Settings.Set("app.silentStart", v)),
                UiKit.ToggleRow("🔔", "双击桌面空白处切换桌面图标", "和 ZenDesktop 那种效果一致，可随时关掉",
                    AppServices.Settings.Get("desktop.doubleClick", true), v => AppServices.Settings.Set("desktop.doubleClick", v))),
            UiKit.ToggleRow("🖥", "隐藏桌面图标", "图标与桌面盒可以共存：先隐藏图标，只留分类盒",
                AppServices.Settings.Get("desktop.iconsHidden", false), v =>
                {
                    AppServices.Settings.Set("desktop.iconsHidden", v);
                    Native.Win32.DesktopIcons.SetVisible(!v);
                }),
            // 说明条：DeskBox 里也有一条几乎一样的（"为什么快捷键有时没有反应?"）
            UiKit.Row("⌨", "快捷键", "Alt+Space 呼出搜索，Alt+` 显示/隐藏桌面层", null)));

        col.Children.Add(UiKit.Banner(BannerKind.Info,
            "如果快捷键有时没反应：当前窗口是以管理员身份运行的（例如任务管理器），Windows 会暂时屏蔽全局热键给它的输入。这是系统限制，不是桌巢的问题。",
            "为什么快捷键有时没有反应？"));

        col.Children.Add(UiKit.Banner(BannerKind.Warning,
            "收纳盒的存储目录默认在 C 盘（" + AppPaths.DefaultManagedRoot + "）。把文件收进盒子里只是「换了位置」，不会释放 C 盘空间；想省 C 盘空间，请在「收纳盒」页把存储位置改到其他盘。",
            "关于收纳与磁盘空间"));
        return col;
    }
}

public static class WidgetsPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("桌面组件", "把有用的信息放到桌面上；每个组件都能单独设样式，也可以全关掉。"));

        col.Children.Add(UiKit.Section("添加组件",
            Row(WidgetKind.Todo, "待办", "快速记录、优先级、到点提醒", host),
            Row(WidgetKind.QuickNote, "随记", "Markdown 随手记，支持贴文件进去", host),
            Row(WidgetKind.Weather, "天气", "读取 Open-Meteo 免费数据，可设城市", host),
            Row(WidgetKind.Music, "音乐", "用媒体键控制当前播放器", host),
            Row(WidgetKind.SystemMonitor, "系统监视", "CPU / 内存 / 网络 / GPU 实时读数", host),
            Row(WidgetKind.Glance, "概览", "日期 + 待办数量 + 常用操作", host),
            Row(WidgetKind.File, "文件", "把某个收纳盒的内容显示成小组件", host)));

        var rows = new List<UIElement>();
        var widgets = AppServices.Repo.Widgets();
        if (widgets.Count == 0)
        {
            rows.Add(UiKit.Row("🧩", "还没有组件", "从上面的列表里挑一个添加", null));
        }
        else
        {
            foreach (var w in widgets)
            {
                var captured = w;
                var control = UiKit.Row(
                    UiKit.Btn(captured.IsVisible ? "隐藏" : "显示", () =>
                    {
                        captured.IsVisible = !captured.IsVisible;
                        AppServices.Repo.Upsert(captured);
                        if (Application.Current is App app)
                        {
                            if (captured.IsVisible) app.Desktop.ShowWidget(captured);
                            else app.Desktop.SetLayerVisible(true);
                        }
                        host.RefreshCurrent();
                    }),
                    UiKit.Btn("透明度 −", () => AdjustOpacity(captured, -0.1)),
                    UiKit.Btn("透明度 ＋", () => AdjustOpacity(captured, 0.1)),
                    UiKit.Btn("删除", () =>
                    {
                        AppServices.Repo.DeleteWidget(captured.Id);
                        host.RefreshCurrent();
                    }));
                rows.Add(UiKit.Row("🧩", KindName(captured.Kind),
                    $"{(int)captured.Width}×{(int)captured.Height} · {(captured.IsVisible ? "显示中" : "已隐藏")} · 透明度 {captured.Opacity:0.00}",
                    control));
            }
        }
        col.Children.Add(UiKit.Section("已有组件", rows.ToArray()));

        col.Children.Add(UiKit.Section("组件组（多个组件叠在一个位置）", BuildGroupRows(host).ToArray()));
        return col;
    }

    private static void AdjustOpacity(WidgetRecord widget, double delta)
    {
        widget.Opacity = Math.Round(Math.Clamp(widget.Opacity + delta, 0.3, 1.0), 2);
        AppServices.Repo.Upsert(widget);
        (Application.Current as App)?.Desktop.ApplyLookToAll();
    }

    private static UIElement Row(WidgetKind kind, string title, string subtitle, MainWindow host)
        => UiKit.ActionRow("＋", title, subtitle, "添加", () =>
        {
            var widget = new WidgetRecord
            {
                Kind = kind,
                Name = title,
                X = 200 + AppServices.Repo.Widgets().Count * 26,
                Y = 180 + AppServices.Repo.Widgets().Count * 20,
                Width = kind switch { WidgetKind.SystemMonitor => 240, WidgetKind.Glance => 260, _ => 300 },
                Height = kind switch { WidgetKind.SystemMonitor => 150, _ => 400 }
            };
            AppServices.Repo.Upsert(widget);
            (Application.Current as App)?.Desktop.ShowWidget(widget);
            Ui.Notifications.Toast($"已添加「{title}」组件到桌面", 3);
            host.RefreshCurrent();
        });

    private static List<UIElement> BuildGroupRows(MainWindow host)
    {
        var rows = new List<UIElement>();
        var boxes = AppServices.Repo.Boxes();
        if (boxes.Count < 2)
        {
            rows.Add(UiKit.Row("🔗", "至少要有 2 个收纳盒才能编组", "编组后可以像浏览器的标签页一样切换", null));
            return rows;
        }

        var first = new ComboBox { Width = 130, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var b in boxes) first.Items.Add(b.Name);
        first.SelectedIndex = 0;
        var second = new ComboBox { Width = 130, Margin = new Thickness(0, 0, 6, 0) };
        foreach (var b in boxes.Skip(1)) second.Items.Add(b.Name);
        second.SelectedIndex = 0;

        rows.Add(UiKit.Row("🔗", "把两个盒子合并到一个位置", "标题栏点击或滚轮切换；随时可以解散",
            UiKit.Row(first, second, UiKit.Btn("合并", () =>
            {
                var a = boxes[Math.Max(0, first.SelectedIndex)];
                var b = boxes.Skip(1).ElementAtOrDefault(Math.Max(0, second.SelectedIndex));
                if (b is null || a.Id == b.Id)
                {
                    Ui.Notifications.Toast("请选择两个不同的盒子", 3);
                    return;
                }
                var group = new BoxGroupRecord
                {
                    Name = a.Name + " 组",
                    MemberIds = a.Id + ";" + b.Id,
                    ActiveMemberId = a.Id,
                    MarginX = a.MarginX,
                    MarginY = a.MarginY,
                    Width = a.Width,
                    Height = a.Height
                };
                AppServices.Repo.Upsert(group);
                a.GroupId = group.Id;
                b.GroupId = group.Id;
                AppServices.Repo.Upsert(a);
                AppServices.Repo.Upsert(b);
                if (Application.Current is App app)
                {
                    app.Desktop.ReloadBoxes();
                    app.Desktop.SwitchGroupTo(group.Id, a.Id);
                }
                Ui.Notifications.Toast($"已合并：{a.Name} + {b.Name}", 4);
                host.RefreshCurrent();
            }))));

        foreach (var group in AppServices.Repo.Groups())
        {
            var captured = group;
            var count = captured.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
            rows.Add(UiKit.Row("🔗", captured.Name, $"{count} 个盒 · 滚轮切换",
                UiKit.Toggle(captured.WheelSwitch, v =>
                {
                    captured.WheelSwitch = v;
                    AppServices.Repo.Upsert(captured);
                })));
        }
        return rows;
    }

    private static string KindName(WidgetKind kind) => kind switch
    {
        WidgetKind.Todo => "待办",
        WidgetKind.QuickNote => "随记",
        WidgetKind.Weather => "天气",
        WidgetKind.Music => "音乐",
        WidgetKind.SystemMonitor => "系统监视",
        WidgetKind.Glance => "概览",
        _ => "文件"
    };
}
