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

public static class AppearancePage
{
    public static UIElement Build(MainWindow host)
    {
        var col = UiKit.Col();
        var theme = Theme.Current;
        col.Children.Add(UiKit.PageHeader("外观与主题", "两级样式：先定一套全局观感，个别盒子或组件可以单独设置。"));

        col.Children.Add(UiKit.Section("全局样式",
            UiKit.ComboRow("🌗", "配色", "跟随系统 / 深色 / 浅色 / 纯黑 / 护眼",
                new[] { "FollowSystem", "Dark", "Light", "PureBlack", "EyeCare" }, theme.Scheme.ToString(), value =>
                {
                    if (!Enum.TryParse<ColorScheme>(value, out var scheme)) return;
                    theme.Scheme = scheme;
                    AppServices.Settings.Set("theme.scheme", value);
                    Theme.Apply(theme);
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                    host.RefreshCurrent();
                }),
            UiKit.ComboRow("🪟", "材质", "亚克力最通透，纯色最省资源",
                new[] { "Acrylic", "Mica", "Solid" }, theme.Material.ToString(), value =>
                {
                    if (!Enum.TryParse<MaterialKind>(value, out var material)) return;
                    theme.Material = material;
                    AppServices.Settings.Set("theme.material", value);
                    Theme.Apply(theme);
                    (Application.Current as App)?.Desktop.ApplyLookToAll();
                }),
            UiKit.SliderRow("💧", "材质强度", null, 0.1, 1.0, theme.MaterialIntensity, v =>
            {
                theme.MaterialIntensity = Math.Round(v, 2);
                AppServices.Settings.Set("theme.intensity", theme.MaterialIntensity);
                Theme.Apply(theme);
                (Application.Current as App)?.Desktop.ApplyLookToAll();
            }, theme.MaterialIntensity.ToString("0.00")),
            UiKit.SliderRow("🌫", "默认透明度", "统一所有窗口用下面的按钮", 0.3, 1.0, theme.Opacity, v =>
            {
                theme.Opacity = Math.Round(v, 2);
                AppServices.Settings.Set("theme.opacity", theme.Opacity);
                Theme.Apply(theme);
                (Application.Current as App)?.Desktop.ApplyLookToAll();
            }, theme.Opacity.ToString("0.00")),
            UiKit.SliderRow("⬜", "圆角", null, 0, 28, theme.CornerRadius, v =>
            {
                theme.CornerRadius = (int)v;
                AppServices.Settings.Set("theme.corner", theme.CornerRadius);
                Theme.Apply(theme);
                (Application.Current as App)?.Desktop.ApplyLookToAll();
            }, ((int)theme.CornerRadius).ToString()),
            UiKit.ToggleRow("🌀", "减少动效", "无障碍：动画降级为淡入淡出",
                theme.ReduceMotion, v =>
                {
                    theme.ReduceMotion = v;
                    AppServices.Settings.Set("theme.reduceMotion", v);
                    Theme.Apply(theme);
                })));

        col.Children.Add(UiKit.Section("统一或独立",
            UiKit.ActionRow("🎯", "把所有盒子与组件统一成全局样式", "等于一键回到「大家都一样」", "统一", () =>
            {
                foreach (var box in AppServices.Repo.Boxes())
                {
                    box.StyleScope = StyleScope.Inherit;
                    box.Material = Theme.Current.Material.ToString();
                    box.MaterialIntensity = Theme.Current.MaterialIntensity;
                    box.Opacity = Theme.Current.Opacity;
                    box.CornerRadius = Theme.Current.CornerRadius;
                    AppServices.Repo.Upsert(box);
                }
                foreach (var widget in AppServices.Repo.Widgets())
                {
                    widget.StyleScope = StyleScope.Inherit;
                    widget.Material = Theme.Current.Material.ToString();
                    widget.MaterialIntensity = Theme.Current.MaterialIntensity;
                    widget.Opacity = Theme.Current.Opacity;
                    widget.CornerRadius = Theme.Current.CornerRadius;
                    AppServices.Repo.Upsert(widget);
                }
                (Application.Current as App)?.Desktop.ApplyLookToAll();
                Ui.Notifications.Toast("已统一为全局样式", 3);
            }, true),
            UiKit.ActionRow("🧩", "全部改为各自独立样式", "此后逐个调整互不影响（像 DeskBox 那样单独控制）", "独立", () =>
            {
                foreach (var box in AppServices.Repo.Boxes())
                {
                    box.StyleScope = StyleScope.Override;
                    AppServices.Repo.Upsert(box);
                }
                Ui.Notifications.Toast("已改为各自独立样式，可在「收纳盒」页逐个调整", 4);
            })));

        var background = AppServices.Settings.Get("theme.background.image", "");
        col.Children.Add(UiKit.Section("背景（可选）",
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
            UiKit.Row("🔍", $"已收录 {AppServices.Search.IndexedCount} 条",
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
        yield return (ModuleIds.ComfyUi, "AI 出图（本地 ComfyUI）", "🖼", "用你自己电脑上的 ComfyUI 出图，图片存本地");
        yield return (ModuleIds.Pet, "桌宠", "🐾", "工作伙伴：待办提醒、番茄钟、休息与系统状态联动、AI 问答");
        yield return (ModuleIds.UsageStats, "应用使用统计", "⏱", "类似 ManicTime，默认只记进程名，数据全在本机");
        yield return (ModuleIds.PerfMonitor, "性能监视面板", "📈", "CPU / 内存 / 网络 / GPU，可自选显示项与布局");
        yield return (ModuleIds.ApiPanel, "API 面板", "💰", "各模型余额、调用次数、成功率、平均延迟（Key 用 DPAPI 加密）");
        yield return (ModuleIds.PowerSchedule, "计划关机", "⏻", "定时 / 倒计时 / 空闲触发，执行前有提醒可取消");
        yield return (ModuleIds.AiBridge, "AI 接口（本地服务）", "🔗", "把盒内文件元数据、待办、随记、统计开放给本机 Codex / dsh / Claude");
        yield return (ModuleIds.Capture, "截图 / 录屏 / GIF", "📸", "基础截图与录制，独立可选包");
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
            UiKit.ToggleRow("✅", "待办到点举手提醒", "气泡里可以直接打勾完成", pet.TodoReminder, v =>
            {
                pet.TodoReminder = v;
                Save(pet);
            }),
            UiKit.ToggleRow("👆", "点击有反应", null, pet.ClickReaction, v =>
            {
                pet.ClickReaction = v;
                Save(pet);
            }),
            UiKit.ToggleRow("📈", "跟随系统状态", "CPU 长时间很高时变累，并提示打开性能面板", pet.SystemReaction, v =>
            {
                pet.SystemReaction = v;
                Save(pet);
            }),
            UiKit.ToggleRow("😴", "空闲时打瞌睡", null, pet.IdleReaction, v =>
            {
                pet.IdleReaction = v;
                Save(pet);
            }),
            UiKit.ToggleRow("⌨", "打字陪伴", "默认关；只统计有按键这个事实，不记录任何字符、不联网",
                pet.KeyboardFollow, v =>
                {
                    pet.KeyboardFollow = v;
                    Save(pet);
                    AppServices.ApplyPetSettings();
                    AppServices.AttachKeyboardFollow();
                }),
            UiKit.ToggleRow("💬", "允许问我一句", "用你自己配置的 API Key，回答会结合你的待办",
                pet.AiAskEnabled, v =>
                {
                    pet.AiAskEnabled = v;
                    Save(pet);
                })));

        col.Children.Add(UiKit.Section("外观",
            UiKit.SliderRow("📏", "尺寸", null, 0.6, 2.0, pet.Scale, v =>
            {
                pet.Scale = Math.Round(v, 2);
                Save(pet);
            }, pet.Scale.ToString("0.00")),
            UiKit.SliderRow("🌫", "透明度", null, 0.3, 1.0, pet.Opacity, v =>
            {
                pet.Opacity = Math.Round(v, 2);
                Save(pet);
            }, pet.Opacity.ToString("0.00")),
            UiKit.ComboRow("🎞", "帧率上限", "越低越省电", new[] { "15", "30", "60" }, pet.FpsLimit.ToString(), v =>
            {
                if (int.TryParse(v, out var fps))
                {
                    pet.FpsLimit = fps;
                    Save(pet);
                }
            }),
            UiKit.ComboRow("📌", "层级", "总在最前 / 贴桌面层 / 空闲才出现",
                new[] { "Always", "OnDesktop", "WhenIdle" }, pet.TopmostMode, v =>
                {
                    pet.TopmostMode = v;
                    Save(pet);
                })));

        var packRows = new List<UIElement>();
        PetPackStore.EnsureBuiltin();
        foreach (var pack in PetPackStore.Installed())
        {
            var captured = pack;
            packRows.Add(UiKit.Row("🎭", captured.Name,
                $"作者 {captured.Author} · 授权 {captured.License}",
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
