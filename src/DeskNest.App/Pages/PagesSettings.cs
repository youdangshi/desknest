using System.Windows;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Modules;
using DeskNest.Core.Services;

namespace DeskNest.App.Pages;

/// <summary>
/// 外观与主题：**设计窗口（主窗口/设置页）与收纳窗口（桌面盒/组件/桌宠）分成两套独立设置**，
/// 互不影响；各自的配色/材质/强度/透明度/圆角都即时生效。
/// </summary>
public static class AppearancePage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("外观与主题",
            "主窗口、设置页、桌面盒、组件、桌宠共用一套外观：改一次，全线生效。"));

        // 一套外观：主窗口、设置页、桌面盒、组件、桌宠全部跟着走。
        // 以前分「设计窗口 / 收纳窗口」两组，用户反馈"设置里分风格、界面却不一致"——
        // 两组各存一份令牌，必然会出现一边改了一边没改。现在只有一组。
        col.Children.Add(Section("外观（主窗口与桌面盒共用一套）", "theme.shell", () => Theme.Shell, host));


        col.Children.Add(UiKit.Section("统一或独立（针对桌面盒与组件）",
            UiKit.ActionRow("🎯", "所有盒与组件统一成上面这套外观", "把单独改过的盒子拉回来，重新跟随全局", "统一",
                () =>
                {
                    foreach (var box in AppServices.Repo.Boxes())
                    {
                        box.StyleScope = StyleScope.Inherit;
                        box.Material = Theme.Desktop.Material.ToString();
                        box.MaterialIntensity = Theme.Desktop.MaterialIntensity;
                        box.Opacity = Theme.Desktop.Opacity;
                        box.CornerRadius = Theme.Desktop.CornerRadius;
                        AppServices.Repo.Upsert(box);
                    }
                    foreach (var widget in AppServices.Repo.Widgets())
                    {
                        widget.StyleScope = StyleScope.Inherit;
                        widget.Material = Theme.Desktop.Material.ToString();
                        widget.MaterialIntensity = Theme.Desktop.MaterialIntensity;
                        widget.Opacity = Theme.Desktop.Opacity;
                        widget.CornerRadius = Theme.Desktop.CornerRadius;
                        AppServices.Repo.Upsert(widget);
                    }
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                    Ui.Notifications.Toast("已把桌面盒与组件统一成收纳窗口的设置", 3);
                }, true),
            UiKit.ActionRow("🧩", "全部改为各自独立样式", "此后逐个调整互不影响（可在「收纳盒」页单独调）", "独立",
                () =>
                {
                    foreach (var box in AppServices.Repo.Boxes())
                    {
                        box.StyleScope = StyleScope.Override;
                        AppServices.Repo.Upsert(box);
                    }
                    Ui.Notifications.Toast("已改为各自独立样式", 4);
                })));

        // 默认不透明：透明背景在这台机器（未打包运行）上不生效时会变成"白底白字根本看不清"。
        // 想开毛玻璃可以手动关这个开关——会先问 DWM 接不接受，不接受就弹回不透明。
        const bool opaqueDefault = true;
        col.Children.Add(UiKit.Section("主窗口背景（看不清文字时改这里）",
            UiKit.ToggleRow("🧊", "用不透明背景",
                "默认开。关掉（用毛玻璃）前会先验证这台机器是否真的支持；不支持会保持实色底",
                AppServices.Settings.Get("theme.forceOpaqueShell", opaqueDefault),
                v =>
                {
                    // 防重入：页面重建时开关会被重新创建，如果值没变就什么都别做，
                    // 否则「读设置 → 写设置 → 重画 → 再读」会来回翻转（日志里 5 次/秒的
                    // 「不透明 / 跟随材质」抖动就是这么来的，用户看到的是背景闪、文字看不清）。
                    if (AppServices.Settings.Get("theme.forceOpaqueShell", opaqueDefault) == v) return;
                    AppServices.Settings.Set("theme.forceOpaqueShell", v);
                    var view = (Application.Current as App)?.MainView;
                    view?.ApplyShellBackground();
                    if (!v && view is not null && !view.IsBackdropActive)
                    {
                        // 用户想要毛玻璃，但 DWM 没接受（未打包运行常见）：弹回不透明，别留一个看不见的窗口
                        AppServices.Settings.Set("theme.forceOpaqueShell", true);
                        view.ApplyShellBackground();
                        Ui.Notifications.Toast("这台机器上毛玻璃不生效（透明会看不清文字），已保持实色底", 6);
                        host.RefreshCurrent();
                        return;
                    }
                    Ui.Notifications.Toast(v ? "主窗口已改为不透明背景" : "主窗口改回毛玻璃背景", 3);
                })));

        var background = AppServices.Settings.Get("theme.background.image", "");
        col.Children.Add(UiKit.Section("动效与背景",
            UiKit.ToggleRow("🌀", "减少动效", "无障碍：动画降级为淡入淡出",
                Theme.Shell.ReduceMotion, v =>
                {
                    var tokens = Theme.Shell.Clone();
                    tokens.ReduceMotion = v;
                    AppServices.Settings.Set("theme.reduceMotion", v);
                    Theme.ApplyShell(tokens);
                }),
            UiKit.Row("🖼", "给盒子与组件加背景", string.IsNullOrEmpty(background) ? "当前：无" : "当前：" + Path.GetFileName(background),
                UiKit.Row(
                    UiKit.Btn("选择文件…", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.gif|视频|*.mp4;*.webm|壁纸工程|*.wallpaper|所有文件|*.*"
                        };
                        if (dlg.ShowDialog() != true) return;
                        AppServices.Settings.Set("theme.background.image", dlg.FileName);
                        (Application.Current as App)?.Desktop.ApplyLookToAll();
                        Ui.Notifications.Toast("背景已设置：" + Path.GetFileName(dlg.FileName), 4);
                        host.RefreshCurrent();
                    }),
                    UiKit.Btn("清除", () =>
                    {
                        AppServices.Settings.Set("theme.background.image", "");
                        (Application.Current as App)?.Desktop.ApplyLookToAll();
                        host.RefreshCurrent();
                    })))));
        return col;
    }

    /// <summary>
    /// 一套完整的"配色 / 材质 / 强度 / 透明度 / 圆角"设置，折成两组（反馈⑧：不平铺）：
    /// 「配色」默认展开（最常改），「材质与质感」收起。
    /// </summary>
    private static UIElement Section(string title, string prefix, Func<ThemeTokens> read, MainWindow host)
    {
        var tokens = read();
        return UiKit.Section(title,
            UiKit.ExpanderRow("🌗", "配色", "深浅色与卡片底色", null, expanded: true,
                UiKit.ComboRow("🌗", "配色", "跟随系统 / 深色 / 浅色 / 纯黑 / 护眼", UiOptions.Schemes, tokens.Scheme.ToString(),
                    v => Save(prefix, read, tokens => tokens.Scheme = Enum.Parse<ColorScheme>(v), host, rebuild: true)),
                CardColorRow(prefix, read, tokens, host)),
            UiKit.ExpanderRow("🪟", "材质与质感", "材质 / 强度 / 透明度 / 圆角", null,
                UiKit.ComboRow("🪟", "材质", "亚克力最通透，纯色最省资源；材质与强度都会改变盒子实际画出来的底色",
                    UiOptions.Materials, tokens.Material.ToString(),
                    v => Save(prefix, read, t => t.Material = Enum.Parse<MaterialKind>(v), host)),
                UiKit.SliderRow("💧", "材质强度", null, 0.1, 1.0, tokens.MaterialIntensity,
                    v => Save(prefix, read, t => t.MaterialIntensity = Math.Round(v, 2), host),
                    tokens.MaterialIntensity.ToString("0.00")),
                UiKit.SliderRow("🌫", "默认透明度", null, 0.3, 1.0, tokens.Opacity,
                    v => Save(prefix, read, t => t.Opacity = Math.Round(v, 2), host),
                    tokens.Opacity.ToString("0.00")),
                UiKit.SliderRow("⬜", "圆角", null, 0, 28, tokens.CornerRadius,
                    v => Save(prefix, read, t => t.CornerRadius = (int)Math.Round(v), host),
                    ((int)tokens.CornerRadius).ToString())));
    }

    /// <summary>
    /// 卡片颜色的预设色板。产品里以前**没有**这个入口，用户只能改"配色方案"，于是"给盒子换个颜色"无从下手。
    /// 选定后文字色会按新底色自动取深/浅，所以不会出现白底白字。
    /// </summary>
    private static readonly (string Name, string Hex)[] CardPalette =
    {
        ("深灰", "#2B2B31"), ("纯白", "#FFFFFF"), ("浅灰", "#E9E9EE"), ("米色", "#F3E9D7"),
        ("深蓝", "#16233A"), ("墨绿", "#14261E"), ("酒红", "#2A1418"), ("紫黑", "#201A2E")
    };

    private static System.Windows.Media.Color ParseHex(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;

    private static string HexOf(System.Windows.Media.Color? c) =>
        c is { } v ? $"#{v.R:X2}{v.G:X2}{v.B:X2}" : "";

    /// <summary>「卡片颜色」一行：盒底真正画出来的颜色 = 材质/强度/透明度叠加后的结果。</summary>
    private static UIElement CardColorRow(string prefix, Func<ThemeTokens> read, ThemeTokens tokens, MainWindow host)
    {
        var strip = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var currentHex = HexOf(tokens.CardColorOverride);
        foreach (var (name, hex) in CardPalette)
        {
            var picked = string.Equals(currentHex, hex, StringComparison.OrdinalIgnoreCase);
            var dot = new System.Windows.Controls.Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new System.Windows.Media.SolidColorBrush(ParseHex(hex)),
                BorderThickness = new Thickness(picked ? 3 : 1),
                BorderBrush = picked ? Theme.Shell.AccentBrush : Theme.Shell.BorderBrush,
                ToolTip = picked ? name + "（当前）" : name
            };
            var hexCaptured = hex;
            dot.MouseLeftButtonUp += (_, _) => Save(prefix, read, t => t.CardColorOverride = ParseHex(hexCaptured), host);
            strip.Children.Add(dot);
        }
        strip.Children.Add(UiKit.Btn("跟配色方案", () => Save(prefix, read, t => t.CardColorOverride = null, host), true));
        var row = (FrameworkElement)UiKit.Row("🎨", "卡片颜色",
            "盒子与组件真正的底色；不选＝跟随上面的配色方案（选了浅色会自动换成深色文字）", strip);
        // 只给收纳窗口那套：设计窗口没有"卡片"这一层（底色由配色方案决定），
        // 而且自定义色会连带改文字色，放在设计窗口上只会把主界面搞乱。
        if (prefix != "theme.desktop") row.Visibility = Visibility.Collapsed;
        return row;
    }

    /// <summary>把一次修改写进设置并立即生效（设计窗口要重建页面，收纳窗口只需刷新桌面层）。</summary>
    private static void Save(string prefix, Func<ThemeTokens> read, Action<ThemeTokens> mutate, MainWindow host,
        bool rebuild = false)
    {
        var tokens = read().Clone();
        mutate(tokens);

        AppServices.Settings.Set($"{prefix}.scheme", tokens.Scheme.ToString());
        AppServices.Settings.Set($"{prefix}.material", tokens.Material.ToString());
        AppServices.Settings.Set($"{prefix}.intensity", tokens.MaterialIntensity);
        AppServices.Settings.Set($"{prefix}.opacity", tokens.Opacity);
        AppServices.Settings.Set($"{prefix}.corner", tokens.CornerRadius);
        AppServices.Settings.Set($"{prefix}.cardColor", HexOf(tokens.CardColorOverride));

        if (prefix == "theme.shell")
        {
            // 一套外观：同一份令牌也写给桌面层（不写的话重启后桌面盒又回到默认值）
            AppServices.Settings.Set("theme.desktop.scheme", tokens.Scheme.ToString());
            AppServices.Settings.Set("theme.desktop.material", tokens.Material.ToString());
            AppServices.Settings.Set("theme.desktop.intensity", tokens.MaterialIntensity);
            AppServices.Settings.Set("theme.desktop.opacity", tokens.Opacity);
            AppServices.Settings.Set("theme.desktop.corner", tokens.CornerRadius);
            AppServices.Settings.Set("theme.desktop.cardColor", HexOf(tokens.CardColorOverride));
            Theme.ApplyShell(tokens);
            Theme.ApplyDesktop(tokens);
        }
        else Theme.ApplyDesktop(tokens);

        (Application.Current as App)?.Desktop.ApplyLookToAll();
        if (rebuild) host.RefreshCurrent();
    }

    private static void CopyTokens(string fromPrefix, string toPrefix, MainWindow host)
    {
        var source = fromPrefix == "theme.shell" ? Theme.Shell : Theme.Desktop;
        var copy = source.Clone();
        AppServices.Settings.Set($"{toPrefix}.scheme", copy.Scheme.ToString());
        AppServices.Settings.Set($"{toPrefix}.material", copy.Material.ToString());
        AppServices.Settings.Set($"{toPrefix}.intensity", copy.MaterialIntensity);
        AppServices.Settings.Set($"{toPrefix}.opacity", copy.Opacity);
        AppServices.Settings.Set($"{toPrefix}.corner", copy.CornerRadius);
        AppServices.Settings.Set($"{toPrefix}.cardColor", HexOf(copy.CardColorOverride));

        if (toPrefix == "theme.shell") Theme.ApplyShell(copy);
        else Theme.ApplyDesktop(copy);

        (Application.Current as App)?.Desktop.ApplyLookToAll();
        Ui.Notifications.Toast(toPrefix == "theme.shell" ? "已把收纳窗口的外观复制到设计窗口" : "已把设计窗口的外观复制到收纳窗口", 3);
        host.RefreshCurrent();
    }
}

public static class ModulesPage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("模块", "每个功能都能单独开关；没装的模块不会加载、不占任何资源。"));

        var rows = new List<UIElement>
        {
            UiKit.Row("🧱", "核心功能", "整理 / 收纳盒 / 全盘搜索 / 设置（产品本身，不可关闭）", null)
        };
        foreach (var (id, title, glyph, subtitle) in ModuleCatalog())
        {
            var captured = id;
            var installed = AppServices.Modules.Loaded.ContainsKey(captured);
            var enabled = AppServices.Modules.IsEnabled(captured);
            if (!installed)
            {
                rows.Add(UiKit.Row(glyph, title, subtitle + " · 当前未安装这个模块",
                    UiKit.Text(captured == ModuleIds.Capture ? "未下载" : "未安装", 11.5, secondary: true)));
                continue;
            }
            rows.Add(UiKit.Row(glyph, title, subtitle,
                UiKit.Toggle(enabled, v =>
                {
                    AppServices.Modules.SetEnabled(captured, v);
                    ApplySwitch(captured, v);
                    host.RefreshCurrent();
                })));
        }
        col.Children.Add(UiKit.Section("功能模块", rows.ToArray()));

        col.Children.Add(UiKit.Section("性能与隐私",
            UiKit.ToggleRow("🧹", "隐藏后清理可重建缓存", "默认 30 秒，省内存",
                AppServices.Settings.Get("perf.trimOnHide", true), v => AppServices.Settings.Set("perf.trimOnHide", v)),
            UiKit.ToggleRow("💤", "空闲时修剪工作集", "长时间不用自动让出内存",
                AppServices.Settings.Get("perf.trimOnIdle", true), v => AppServices.Settings.Set("perf.trimOnIdle", v)),
            UiKit.ToggleRow("📝", "记录最近搜索历史", null,
                AppServices.Settings.Get("search.saveHistory", true), v => AppServices.Settings.Set("search.saveHistory", v)),
            UiKit.InputRow("📁", "全盘索引范围", "留空＝所有固定磁盘；也可只写某几个盘，如 D:;E:",
                AppServices.Settings.Get("search.roots", ""), v => AppServices.Settings.Set("search.roots", v), 240),
            UiKit.Row("🔍", $"已收录 {AppServices.Search.IndexedCount:N0} 条",
                AppServices.Search.IsIndexing ? "正在后台建立索引…" : "已就绪",
                UiKit.Btn("重建索引", () =>
                {
                    AppServices.Search.Clear();
                    AppServices.Search.StartIndexing(AppServices.SearchRoots());
                    Ui.Notifications.Toast("已开始重建索引（后台进行，不影响使用）", 4);
                }))));
        return col;
    }

    private static IEnumerable<(string Id, string Title, string Glyph, string Subtitle)> ModuleCatalog()
    {
        yield return (ModuleIds.Pet, "桌宠", "🐾", "工作伙伴：待办提醒、番茄钟、休息与系统状态联动、AI 问答");
        yield return (ModuleIds.UsageStats, "应用使用统计", "⏱", "类似 ManicTime，默认只记进程名，数据全在本机");
        yield return (ModuleIds.PerfMonitor, "性能监视面板", "📈", "CPU / 内存 / 网络 / GPU，可自选显示项与布局");
        yield return (ModuleIds.ApiPanel, "API 面板", "💰", "各模型余额、调用次数、成功率、平均延迟（Key 用 DPAPI 加密）");
        yield return (ModuleIds.PowerSchedule, "计划关机", "⏻", "定时 / 倒计时 / 空闲触发，执行前有提醒可取消");
        yield return (ModuleIds.AiBridge, "AI 接口（本地服务）", "🔗", "把盒内文件元数据、待办、随记、统计开放给本机 Codex / dsh / Claude");
    }

    private static void ApplySwitch(string id, bool enabled)
    {
        if (id == ModuleIds.UsageStats)
        {
            if (enabled) AppServices.Usage.Start(); else AppServices.Usage.Stop();
        }
        else if (id == ModuleIds.PerfMonitor)
        {
            if (enabled) AppServices.Perf.Start(); else AppServices.Perf.Stop();
        }
        else if (id == ModuleIds.AiBridge)
        {
            if (enabled) AppServices.AiBridge.Start(); else AppServices.AiBridge.Stop();
        }
        else if (id == ModuleIds.Pet)
        {
            if (enabled) (Application.Current as App)?.Desktop.ShowPet();
            else (Application.Current as App)?.Desktop.HidePet();
        }
    }
}

public static class PetPage
{
    public static UIElement Build(MainWindow host)
    {
        var pet = AppServices.Repo.Pet();
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("桌宠", "默认关闭；打开后它会安静待在桌角，需要时才来提醒你。"));

        col.Children.Add(UiKit.Section("开关",
            UiKit.ToggleRow("🐾", "启用桌宠模块", "关闭后不加载、不占内存",
                AppServices.Modules.IsEnabled(ModuleIds.Pet), v =>
                {
                    AppServices.Modules.SetEnabled(ModuleIds.Pet, v);
                    if (v) (Application.Current as App)?.Desktop.ShowPet();
                    else (Application.Current as App)?.Desktop.HidePet();
                    host.RefreshCurrent();
                }),
            UiKit.ToggleRow("👁", "显示在桌面上", null, pet.IsVisible, v =>
            {
                pet.IsVisible = v;
                Save(pet);
                if (v) (Application.Current as App)?.Desktop.ShowPet();
                else (Application.Current as App)?.Desktop.HidePet();
            })));

        col.Children.Add(UiKit.Section("行为（都可以单独关）",
            UiKit.ToggleRow("✅", "待办到点举手提醒", "气泡里可以直接打勾完成", pet.TodoReminder, v => { pet.TodoReminder = v; Save(pet); }),
            UiKit.ToggleRow("👆", "点击有反应", null, pet.ClickReaction, v => { pet.ClickReaction = v; Save(pet); }),
            UiKit.ToggleRow("📈", "跟随系统状态", "CPU 长时间很高时变累", pet.SystemReaction, v => { pet.SystemReaction = v; Save(pet); }),
            UiKit.ToggleRow("😴", "空闲时打瞌睡", null, pet.IdleReaction, v => { pet.IdleReaction = v; Save(pet); }),
            UiKit.ToggleRow("⌨", "打字陪伴", "默认关；只统计有按键这个事实，不记录任何字符、不联网", pet.KeyboardFollow, v =>
            {
                pet.KeyboardFollow = v;
                Save(pet);
                AppServices.ApplyPetSettings();
                AppServices.AttachKeyboardFollow();
            }),
            UiKit.ToggleRow("💬", "允许「问我一句」", "用你自己配置的 API Key，回答会结合你的待办", pet.AiAskEnabled, v =>
            {
                pet.AiAskEnabled = v;
                Save(pet);
            })));

        col.Children.Add(UiKit.Section("外观",
            UiKit.SliderRow("📏", "尺寸", null, 0.6, 2.0, pet.Scale, v => { pet.Scale = Math.Round(v, 2); Save(pet); }, null),
            UiKit.SliderRow("🌫", "透明度", null, 0.3, 1.0, pet.Opacity, v => { pet.Opacity = Math.Round(v, 2); Save(pet); }, null),
            UiKit.ComboRow("🎞", "帧率上限", "越低越省电", new[] { "15", "30", "60" }, pet.FpsLimit.ToString(), v =>
            {
                if (int.TryParse(v, out var fps)) { pet.FpsLimit = fps; Save(pet); }
            }),
            UiKit.ComboRow("📌", "层级", "总在最前 / 贴桌面层 / 空闲才出现",
                UiOptions.PetLayers, pet.TopmostMode, v => { pet.TopmostMode = v; Save(pet); })));

        var packRows = new List<UIElement>();
        PetPackStore.EnsureBuiltin();
        foreach (var pack in PetPackStore.Installed())
        {
            var captured = pack;
            packRows.Add(UiKit.Row("🎭", captured.Name, $"作者 {captured.Author} · 授权 {captured.License}",
                UiKit.Row(
                    UiKit.Btn("使用", () =>
                    {
                        pet.PackId = captured.Id;
                        pet.Name = captured.Name;
                        Save(pet);
                        Ui.Notifications.Toast("已切换素材包：" + captured.Name, 3);
                    }),
                    UiKit.Btn("删除", () =>
                    {
                        PetPackStore.Remove(captured.Id);
                        host.RefreshCurrent();
                    }))));
        }
        packRows.Add(UiKit.ActionRow("📥", "导入 .petpack 素材包", "素材包 = 一个 zip：manifest.json + 动画文件；授权字段缺失会提示", "导入", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "桌宠素材包 (*.petpack)|*.petpack|所有文件 (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            var (ok, message) = PetPackStore.Import(dlg.FileName);
            Ui.Notifications.Toast(message, 6);
            if (ok) host.RefreshCurrent();
        }));
        col.Children.Add(UiKit.Section("素材包", packRows.ToArray()));
        return col;
    }

    private static void Save(PetRecord pet)
    {
        AppServices.Repo.SavePet(pet);
        AppServices.ApplyPetSettings();
    }
}
