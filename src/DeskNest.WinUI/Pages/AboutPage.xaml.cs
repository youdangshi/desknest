using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DeskNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskNest.WinUI.Pages;

/// <summary>关于页：版本、技术栈、数据位置、许可证。全部现读，不写死版本号。</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Build();
    }

    private void Build()
    {
        var asm = typeof(AboutPage).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "1.0.0";
        var buildTime = File.Exists(asm.Location)
            ? File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm")
            : "—";

        Host.Children.Add(PageKit.PageTitle("关于"));

        var hero = PageKit.Column(4);
        hero.Children.Add(new TextBlock { Text = "桌巢 DeskNest", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        hero.Children.Add(new TextBlock { Text = "把 Windows 桌面收拾成本地收纳盒：文件归类、盒子贴桌面、数据全在本机。", FontSize = 12.5, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        Host.Children.Add(PageKit.Card(null, hero));

        var info = PageKit.Column(8);
        info.Children.Add(PageKit.InfoRow("版本", version));
        info.Children.Add(PageKit.InfoRow("构建", buildTime));
        info.Children.Add(PageKit.InfoRow("界面", "WinUI 3（Windows App SDK 1.8）"));
        info.Children.Add(PageKit.InfoRow("运行时", $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64 位" : "32 位")}"));
        info.Children.Add(PageKit.InfoRow("系统", Environment.OSVersion.VersionString));
        info.Children.Add(PageKit.InfoRow("数据目录", AppPaths.DataRoot));
        info.Children.Add(PageKit.InfoRow("日志文件", App.LogPath));
        Host.Children.Add(PageKit.Card("版本信息", info));

        // 开机自启（MSIX 版走 shell:AppsFolder，不能写裸 exe 路径）
        var autoStart = new ToggleSwitch { OnContent = "已开启", OffContent = "已关闭" };
        var suppressToggle = false; // 回滚时不触发事件，避免递归
        try
        {
            autoStart.IsOn = DeskNest.Native.Win32.AutoStart.IsMsixEnabled();
        }
        catch { autoStart.IsOn = false; }
        autoStart.Toggled += (_, _) =>
        {
            if (suppressToggle) return;
            string? pfn = null;
            try { pfn = global::Windows.ApplicationModel.Package.Current.Id.FamilyName; }
            catch (Exception ex) { App.Log("取包标识失败（可能不是 MSIX 运行）：" + ex.Message); }
            var ok = DeskNest.Native.Win32.AutoStart.SetMsixEnabled(autoStart.IsOn, pfn);
            if (!ok)
            {
                App.Log("设置开机自启失败，已回滚开关");
                suppressToggle = true;
                autoStart.IsOn = !autoStart.IsOn;
                suppressToggle = false;
            }
        };
        Host.Children.Add(PageKit.Card("启动", PageKit.ActionRow(
            "开机自动启动", "登录后自动运行桌巢，桌面盒与组件随之恢复", autoStart)));

        var actions = PageKit.Column(10);
        actions.Children.Add(PageKit.ActionRow("打开日志目录", "反馈问题时把最新的 desknest.log / winui.log 发过来",
            PageKit.Btn("打开", (_, _) => OpenFolder(Path.GetDirectoryName(App.LogPath) ?? AppPaths.DataRoot))));
        actions.Children.Add(PageKit.Divider());
        actions.Children.Add(PageKit.ActionRow("项目主页", "https://github.com/youdangshi/desknest",
            PageKit.Btn("打开", (_, _) => OpenUrl("https://github.com/youdangshi/desknest"))));
        Host.Children.Add(PageKit.Card("帮助与反馈", actions));

        Host.Children.Add(PageKit.Hint("许可证：GNU GPL v3.0（2026-10-17 由 MIT 切换，为兼容 DeskBox 的 GPL 实现）。" +
                                       "图标来自微软 Fluent UI System Icons（MIT）。"));
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }
        catch (Exception ex) { App.Log("打开目录失败：" + ex.Message); }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log("打开链接失败：" + ex.Message); }
    }
}
