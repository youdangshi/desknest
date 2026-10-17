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
        var items = AppServices.Repo.AllItems();
        var totalMb = items.Sum(i => i.SizeBytes) / 1048576.0;

        col.Children.Add(UiKit.PageHeader("概览", "桌面上的收纳情况，一眼看完。"));

        col.Children.Add(UiKit.Section("桌面状态",
            UiKit.Row("🗂", $"收纳盒 {boxes.Count} 个", boxes.Count == 0 ? "还没有盒子，先建一个" : "点击进入「收纳盒」", null,
                () => host.NavigateTo("boxes")),
            UiKit.Row("📦", $"已收纳 {items.Count} 项", $"共 {totalMb:0.#} MB（真移动，不占双份空间）", null,
                () => OpenDataRoot()),
            UiKit.Row("📌", "托盘：常驻运行中", "左键单击 = 显示 / 隐藏所有盒子", null)));

        col.Children.Add(UiKit.Section("常用操作",
            UiKit.ActionRow("🆕", "新建收纳盒", "只建空盒子，一个文件都不会动", "新建", () => host.NavigateTo("boxes"), true),
            UiKit.ActionRow("👀", "显示 / 隐藏所有盒子", "想快速清屏时按一下（托盘上单击也一样）", "切换", () => Commands.ToggleLayerRequested?.Invoke()),
            UiKit.ActionRow("📂", "打开数据目录", "盒子里的文件真实存在这里", "打开", () => OpenDataRoot())));

        if (boxes.Count == 0)
            col.Children.Add(UiKit.Banner(BannerKind.Info,
                "桌面上的文件还没地方放？先建一个盒子，再把文件拖进去 —— 文件会真的移进盒子，桌面立刻干净。",
                "第一次使用"));

        col.Children.Add(UiKit.Section("开机与启动",
            UiKit.ExpanderRow("🚀", "开机自动启动", "登录 Windows 后在后台启动",
                UiKit.Toggle(AppServices.Settings.Get("app.autoStart", false), v =>
                {
                    AppServices.Settings.Set("app.autoStart", v);
                    Native.Win32.AutoStart.SetEnabled(v);
                }),
                UiKit.ToggleRow("🌙", "静默启动", "启动时不弹主窗口，只留托盘图标",
                    AppServices.Settings.Get("app.silentStart", false), v => AppServices.Settings.Set("app.silentStart", v)))));

        col.Children.Add(UiKit.Banner(BannerKind.Warning,
            "收纳盒的存储目录默认在 C 盘（" + AppPaths.DefaultManagedRoot + "）。把文件收进盒子里只是「换了位置」，不会释放 C 盘空间。",
            "关于收纳与磁盘空间"));

        return col;
    }

    /// <summary>打开数据目录（收纳盒里文件真实存放的地方）。</summary>
    private static void OpenDataRoot()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.DataRoot);
            System.Diagnostics.Process.Start("explorer.exe", AppPaths.DataRoot);
        }
        catch (Exception ex) { Ui.Notifications.Toast("打不开数据目录：" + ex.Message); }
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
