using System.Windows;
using System.Windows.Controls;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

public static class BoxesPage
{
    /// <summary>
    /// 新盒子的落点：默认落在"鼠标所在的那块显示器"上并逐个错开，避免多个盒子叠在同一个坐标
    /// （以前是固定 240,160 起步，双屏时会把盒子开在用户根本没看的那块屏上）。
    /// </summary>
    private static (double X, double Y) NextBoxSpot()
    {
        var count = AppServices.Repo.Boxes().Count;
        var offsetX = count * 30.0;
        var offsetY = count * 20.0;

        if (!AppServices.Settings.Get("displays.newBoxOnCursor", true))
            return (240 + offsetX, 160 + offsetY);

        var screen = DeskNest.Native.Win32.Monitors.ForCursor();
        if (screen is null) return (240 + offsetX, 160 + offsetY);

        var maxX = Math.Max(screen.WorkX + 16, screen.WorkRight - 200);
        var maxY = Math.Max(screen.WorkY + 16, screen.WorkBottom - 120);
        return (
            Math.Min(screen.WorkX + 48 + offsetX, maxX),
            Math.Min(screen.WorkY + 48 + offsetY, maxY));
    }

    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("收纳盒", "桌面上的分类容器。文件会被移动进盒子的存储目录，删除盒子时会自动还原。"));

        // 原则：**新建盒子只建盒子，绝不顺手搬用户的文件**。
        // 整理文件是另一个动作，必须由用户明确发起，且在「整理与规则」里有预览、可回滚。
        col.Children.Add(UiKit.Section("新建",
            UiKit.ActionRow("🧲", "新建收纳盒", "只建一个空盒子，不会动你桌面上任何文件", "新建",
                () => NewBoxDialog(host), true),
            UiKit.ActionRow("🧹", "把桌面文件收进来", "这一步才会移动文件：先去「整理与规则 → 一键整理」，可以先预览再执行，随时能回滚", "去整理",
                () => { host.NavigateTo("organize"); Ui.Notifications.Toast("在这里点「一键整理」——会先给你预览，确认后才动文件", 6); })));

        var cards = new List<UIElement>();
        foreach (var box in AppServices.Repo.Boxes())
        {
            var captured = box;
            var rows = new List<UIElement>
            {
                UiKit.InputRow("🏷", "名称", KindText(captured.Kind), captured.Name, v =>
                {
                    captured.Name = v;
                    AppServices.Repo.Upsert(captured);
                }, 180),
                UiKit.Row("📂", "存储位置", BoxWindow.StorageRootOf(captured), UiKit.Btn("打开", () =>
                {
                    var root = BoxWindow.StorageRootOf(captured);
                    Directory.CreateDirectory(root);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + root + "\"");
                })),
                UiKit.ToggleRow("👁", "在桌面上显示", $"当前 {AppServices.Repo.Items(captured.Id, captured.Kind == BoxKind.Mapped).Count} 个文件项",
                    !captured.IsHidden, v =>
                    {
                        captured.IsHidden = !v;
                        AppServices.Repo.Upsert(captured);
                        (Application.Current as App)?.Desktop.ReloadBoxes();
                    }),
                UiKit.SliderRow("🌫", "透明度", null, 0.2, 1.0, captured.Opacity, v =>
                {
                    captured.Opacity = Math.Round(v, 2);
                    AppServices.Repo.Upsert(captured);
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                }, captured.Opacity.ToString("0.00")),
                UiKit.SliderRow("🔳", "图标大小", "也可以按住 Ctrl + 滚轮直接在桌面上调", 18, 72, captured.IconSize, v =>
                {
                    captured.IconSize = (int)v;
                    AppServices.Repo.Upsert(captured);
                    (Application.Current as App)?.Desktop.ReloadBoxes();
                }, ((int)captured.IconSize).ToString()),
                UiKit.SliderRow("⬜", "圆角", null, 0, 28, captured.CornerRadius, v =>
                {
                    captured.CornerRadius = (int)v;
                    AppServices.Repo.Upsert(captured);
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                }, ((int)captured.CornerRadius).ToString()),
                UiKit.ComboRow("🪟", "材质", null, UiOptions.Materials, captured.Material, m =>
                {
                    captured.Material = m;
                    AppServices.Repo.Upsert(captured);
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                }),
                UiKit.ComboRow("📋", "视图", null, UiOptions.ViewModes, captured.ViewMode, m =>
                {
                    captured.ViewMode = m;
                    AppServices.Repo.Upsert(captured);
                    (Application.Current as App)?.Desktop.ReloadBoxes();
                }),
                UiKit.ToggleRow("🎨", "单独样式（不跟随全局）", "每个盒子都可以和别的盒子长得不一样",
                    captured.StyleScope == StyleScope.Override, v =>
                    {
                        captured.StyleScope = v ? StyleScope.Override : StyleScope.Inherit;
                        AppServices.Repo.Upsert(captured);
                        (Application.Current as App)?.Desktop.ApplyLookToAll();
                    }),
                UiKit.ToggleRow("🔒", "锁定位置 / 尺寸", null, captured.LockPosition && captured.LockSize, v =>
                {
                    captured.LockPosition = v;
                    captured.LockSize = v;
                    AppServices.Repo.Upsert(captured);
                }),
                UiKit.ActionRow("🗑", "删除这个盒子", "盒内文件会逐个还原到原位置（原位置不存在则还原到桌面）", "删除", () =>
                {
                    // 确认框挂到主窗口上：不指定拥有者时对话框有可能跑到主窗口后面，
                    // 用户就会觉得"点了删除没反应"
                    var ok = MessageBox.Show(host, $"删除盒子「{captured.Name}」并把盒内文件还原到原位置？", "删除收纳盒",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (ok != MessageBoxResult.OK) return;

                    try
                    {
                        var outcome = AppServices.Boxes.Delete(captured);
                        if (outcome.StillInDatabase)
                        {
                            Ui.Notifications.Toast("删除没生效：记录仍然在库里，请把日志发给开发者", 8);
                        }
                        else
                        {
                            (Application.Current as App)?.Desktop.CloseBox(captured.Id);
                            var extra = outcome.Failed > 0 ? $"，{outcome.Failed} 个文件还原失败（见日志）" : "";
                            Ui.Notifications.Toast($"已删除「{captured.Name}」，还原 {outcome.Restored} 个文件{extra}", 5);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"删除盒子失败：{ex}");
                        Ui.Notifications.Toast("删除失败：" + ex.Message, 8);
                    }
                    host.RefreshCurrent();
                })
            };
            cards.Add(UiKit.Section(captured.Name, rows.ToArray()));
        }

        if (cards.Count == 0)
            cards.Add(UiKit.Section("还没有收纳盒", UiKit.Row("💡", "先新建一个试试", "推荐用「桌面分区」，桌面会干净很多", null)));
        foreach (var card in cards) col.Children.Add(card);
        return col;
    }

    private static string KindText(BoxKind kind) => kind switch
    {
        BoxKind.Mapped => "映射盒：只存路径，不动文件",
        BoxKind.Drawer => "抽屉盒",
        BoxKind.Todo => "待办盒",
        _ => "普通盒：文件会被移动进存储目录"
    };

    /// <summary>
    /// 新建收纳盒：**只创建一个空盒子**，不移动任何文件。
    /// （v1.1 之前这里会顺手跑一次"桌面分区整理"，把用户桌面的文件按类型搬进存储目录——
    /// 用户点"新建"却丢了桌面文件，就是这个设计造成的，已彻底去掉。）
    /// </summary>
    public static void NewBoxDialog(MainWindow host)
    {
        var nameInput = UiKit.Input("新收纳盒", _ => { }, 220);
        var folderInput = UiKit.Input("", _ => { }, 220);
        var typeChoice = "普通盒";
        var typeRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var kind in new[] { "普通盒", "映射盒（只存路径，不动文件）" })
        {
            var captured = kind;
            var button = UiKit.Btn(kind, () =>
            {
                typeChoice = captured;
                foreach (var child in typeRow.Children.OfType<Wpf.Ui.Controls.Button>())
                    child.Appearance = (child.Content as string) == captured
                        ? Wpf.Ui.Controls.ControlAppearance.Primary
                        : Wpf.Ui.Controls.ControlAppearance.Secondary;
            });
            typeRow.Children.Add(button);
        }
        var targetHint = UiKit.Text("", 11.5, secondary: true);
        void RefreshTarget()
        {
            var name = string.IsNullOrWhiteSpace(nameInput.Text) ? "新收纳盒" : nameInput.Text.Trim();
            targetHint.Text = typeChoice.StartsWith("映射")
                ? "只登记这些文件/文件夹的路径，不搬动它们"
                : "盒子存储位置：" + Path.Combine(AppPaths.DefaultManagedRoot, BoxWindow.SanitizeName(name)) + "\n不会自动搬走桌面上的任何文件";
        }
        nameInput.TextChanged += (_, _) => RefreshTarget();
        RefreshTarget();

        var win = new Window
        {
            Title = "新建收纳盒",
            Width = 560,
            Height = 460,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Current.BackgroundBrush,
            Foreground = Theme.Current.ForegroundBrush
        };

        var createBtn = UiKit.Btn("创建空盒子", () => { }, true);
        var cancelBtn = UiKit.Btn("取消", () => win.Close());

        createBtn.Click += (_, _) =>
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(nameInput.Text) ? "新收纳盒" : nameInput.Text.Trim();
                // 双屏用户：在哪个屏幕上点"新建"，盒子就开在那块屏上（可在「多显示器」页关掉）
                var spot = NextBoxSpot();
                var mapped = typeChoice.StartsWith("映射");
                var box = new BoxRecord
                {
                    Name = name,
                    Kind = mapped ? BoxKind.Mapped : BoxKind.Normal,
                    MarginX = spot.X,
                    MarginY = spot.Y
                };
                if (mapped)
                {
                    box.MappedPath = string.IsNullOrWhiteSpace(folderInput.Text)
                        ? AppPaths.DefaultManagedRoot
                        : folderInput.Text.Trim();
                }
                else
                {
                    box.StorageRoot = Path.Combine(AppPaths.DefaultManagedRoot, BoxWindow.SanitizeName(box.Name));
                }
                AppServices.Repo.Upsert(box);
                (Application.Current as App)?.Desktop.ShowBox(box);
                Ui.Notifications.Toast($"已创建空盒子「{name}」——没有移动任何文件；要把桌面文件收进来请去「整理与规则 → 一键整理」", 8);
                win.Close();
                host.RefreshCurrent();
            }
            catch (Exception ex)
            {
                Ui.Notifications.Toast("创建失败：" + ex.Message);
            }
        };

        // 内容可滚动、按钮固定在底部：内容再长也不会像以前那样被窗口边切掉半截
        var settings = UiKit.Col(
            UiKit.PageHeader("新建收纳盒", "只创建一个空盒子；不会移动你桌面上的任何文件。"),
            UiKit.Section("设置",
                UiKit.Row("🏷", "名称", "给盒子起个名字", nameInput),
                UiKit.Row("📦", "类型", "普通盒＝文件会被移动进存储目录；映射盒＝只登记路径、一个文件都不动", typeRow),
                UiKit.Row("📂", "映射到哪个文件夹（仅映射盒）", "留空＝默认存储目录", folderInput),
                UiKit.Row("📍", "去向", null, targetHint)),
            UiKit.Banner(BannerKind.Info, "想整理桌面文件？那是另一个动作：去「整理与规则 → 一键整理」，那里可以先预览、执行后还能回滚。"));
        var footer = new Border
        {
            Padding = new Thickness(16, 8, 16, 12),
            Child = UiKit.Row(createBtn, cancelBtn)
        };
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(UiKit.Scroll(settings));
        win.Content = layout;
        win.ShowDialog();
    }

    private static OrganizeEngine.Plan BuildPlan(string desktopPath, bool includeShortcuts, bool folderMode)
        => AppServices.Organizer.BuildPlan(new OrganizeEngine.ScanOptions(desktopPath, includeShortcuts, false, folderMode));
}

public static class OrganizePage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        col.Children.Add(UiKit.PageHeader("整理与规则", "整理前先预览，整理后随时回滚；快捷方式默认不动。"));

        col.Children.Add(UiKit.Section("一键整理",
            UiKit.ActionRow("🧹", "打开整理向导", "扫描桌面 → 预览 → 执行 → 不满意就撤销", "开始",
                () => ShowWizard(host), true),
            UiKit.Row("📂", "整理范围", desktopPath, null),
            UiKit.ActionRow("🔎", "只统计不整理", "先看看会动哪些文件", "试算", () =>
            {
                var plan = BuildPlan();
                MessageBox.Show($"将整理 {plan.Items.Count} 项，保留 {plan.Skipped.Count} 项。", "预览",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            })));

        col.Children.Add(UiKit.Section("方式与自动化",
            UiKit.ToggleRow("🤖", "自动整理", "默认关闭；开启后按间隔检查桌面（连续取消会提醒）",
                AppServices.Settings.Get("organize.auto", false), v => AppServices.Settings.Set("organize.auto", v)),
            UiKit.InputRow("⏱", "检查间隔（分钟）", null, AppServices.Settings.Get("organize.autoMinutes", 30).ToString(), v =>
            {
                if (int.TryParse(v, out var m)) AppServices.Settings.Set("organize.autoMinutes", Math.Clamp(m, 5, 1440));
            }, 80),
            UiKit.ToggleRow("🔗", "也收纳快捷方式", "默认不收：快捷方式留在桌面",
                AppServices.Settings.Get("organize.includeShortcuts", false), v => AppServices.Settings.Set("organize.includeShortcuts", v)),
            UiKit.ToggleRow("📁", "用文件夹归集而不是桌面分区", "开启＝按类型建文件夹；关闭＝收进桌面盒子",
                AppServices.Settings.Get("organize.folderMode", false), v => AppServices.Settings.Set("organize.folderMode", v))));

        var ruleRows = new List<UIElement>();
        var rules = AppServices.Repo.Rules();
        if (rules.Count == 0)
            ruleRows.Add(UiKit.Row("📜", "还没有规则", "规则会先于默认分类生效，可把特定文件固定送到某个盒子", null));
        foreach (var rule in rules)
        {
            var captured = rule;
            var conditions = RulesEngine.ParseConditions(captured.ConditionsJson);
            ruleRows.Add(UiKit.Row("📜", captured.Name,
                $"类型 {string.Join("/", conditions.Extensions)}　名称含 {string.Join("/", conditions.NameContains)}　命中 {captured.HitCount} 次",
                UiKit.Row(
                    UiKit.Toggle(captured.Enabled, v =>
                    {
                        captured.Enabled = v;
                        AppServices.Repo.Upsert(captured);
                    }),
                    UiKit.Btn("删除", () =>
                    {
                        AppServices.Repo.DeleteRule(captured.Id);
                        host.RefreshCurrent();
                    }))));
        }
        ruleRows.Add(UiKit.Row("＋", "快速新建规则", "把名称里含关键词的文件固定送到指定盒",
            UiKit.Row(
                UiKit.Btn("截图 → 图片盒", () => QuickAddRule("截图", "截图", host)),
                UiKit.Btn("安装包 → 程序盒", () => QuickAddRule("安装包", "安装包", host)))));
        col.Children.Add(UiKit.Section("规则", ruleRows.ToArray()));

        var historyRows = new List<UIElement>();
        var runs = AppServices.Repo.Runs(20);
        if (runs.Count == 0)
            historyRows.Add(UiKit.Row("🕘", "还没有整理记录", null, null));
        foreach (var run in runs)
        {
            var captured = run;
            UIElement? control = captured.Undone || captured.MovedCount == 0
                ? null
                : UiKit.Btn("回滚这一条", () =>
                {
                    var (restored, failed) = AppServices.Organizer.Undo(captured);
                    Ui.Notifications.Toast($"回滚完成：还原 {restored}，失败 {failed}");
                    host.RefreshCurrent();
                });
            historyRows.Add(UiKit.Row("🕘",
                captured.StartedAt.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                $"{captured.MovedCount} 项 · {(captured.Undone ? "已回滚" : "可回滚")}", control));
        }
        col.Children.Add(UiKit.Section("整理历史", historyRows.ToArray()));
        return col;
    }

    private static OrganizeEngine.Plan BuildPlan() => AppServices.Organizer.BuildPlan(new OrganizeEngine.ScanOptions(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        AppServices.Settings.Get("organize.includeShortcuts", false),
        false,
        AppServices.Settings.Get("organize.folderMode", false)));

    private static void QuickAddRule(string name, string keyword, MainWindow host)
    {
        AppServices.Repo.Upsert(new RuleRecord
        {
            Name = name,
            ConditionsJson = RulesEngine.ToJson(new RuleConditions { NameContains = new List<string> { keyword } }),
            ActionsJson = RulesEngine.ToJson(new RuleActions { Kind = "MoveToBox" })
        });
        Ui.Notifications.Toast($"已新建规则「{name}」", 3);
        host.RefreshCurrent();
    }

    /// <param name="modal">
    /// true＝普通调用（阻塞到用户操作完）；false＝非模态打开，供文档截图模式使用。
    /// </param>
    public static void ShowWizard(MainWindow host, bool modal = true)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var useFolder = AppServices.Settings.Get("organize.folderMode", false);
        var includeShortcuts = AppServices.Settings.Get("organize.includeShortcuts", false);
        var plan = AppServices.Organizer.BuildPlan(new OrganizeEngine.ScanOptions(desktopPath, includeShortcuts, false, useFolder));

        var list = UiKit.Col();
        list.Children.Add(UiKit.Text($"将整理 {plan.Items.Count} 项，保留 {plan.Skipped.Count} 项", 13.5, bold: true));
        foreach (var group in plan.Items.GroupBy(i => i.Category))
            list.Children.Add(UiKit.Text($"· {group.Key}：{group.Count()} 项", 12.5));
        foreach (var skip in plan.Skipped.Take(8))
            list.Children.Add(UiKit.Text("· 保留：" + skip, 11.5, secondary: true));

        var preview = new Window
        {
            Title = "整理预览",
            Width = 560,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Current.BackgroundBrush,
            Foreground = Theme.Current.ForegroundBrush
        };
        var runBtn = UiKit.Btn("开始整理", () => { }, true);
        var cancelBtn = UiKit.Btn("取消", () => preview.Close());
        runBtn.Click += (_, _) =>
        {
            preview.Close();
            var run = AppServices.Organizer.Execute(plan, useFolder ? "manual-folder" : "manual-partition");
            Ui.Notifications.Toast($"整理完成：{run.MovedCount} 项", 10, action: () =>
            {
                var (restored, failed) = AppServices.Organizer.Undo(run);
                Ui.Notifications.Toast($"已回滚：{restored} 项" + (failed > 0 ? $"，{failed} 项失败" : ""));
            });
            (Application.Current as App)?.Desktop.ReloadBoxes();
            host.RefreshCurrent();
        };
        preview.Content = UiKit.Col(
            UiKit.PageHeader("整理预览", "确认无误再执行；执行后底部会出现撤销入口。"),
            UiKit.Scroll(list),
            UiKit.Row(runBtn, cancelBtn));
        if (modal) preview.ShowDialog();
        else preview.Show();
    }
}
