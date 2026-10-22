using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DeskNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 概览页：真实统计（盒子数 / 文件数 / 数据库大小）+ 常用操作。
/// 数字全部现读 Core 仓库，不缓存——盒子随时可能被新建/删除。
/// </summary>
public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        ShowSkeleton();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.BoxesChanged += OnBoxesChanged;
        Reload();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.BoxesChanged -= OnBoxesChanged;
    }

    private void OnBoxesChanged() => DispatcherQueue.TryEnqueue(Reload);

    private void ShowSkeleton()
    {
        Host.Children.Clear();
        Host.Children.Add(PageKit.PageTitle("概览"));
        Host.Children.Add(PageKit.PageIntro("你的桌面收纳现状。所有数字都是实时从本地数据库读的。"));
    }

    private void Reload()
    {
        ShowSkeleton();
        try
        {
            var boxes = App.Repo?.Boxes() ?? new();
            var shown = boxes.Count(b => !b.IsHidden);
            var hidden = boxes.Count(b => b.IsHidden);
            var items = App.Repo?.AllItems() ?? new();

            // 统计卡
            Host.Children.Add(PageKit.Card(null, PageKit.StatRow(
                PageKit.Stat(boxes.Count.ToString(), "收纳盒"),
                PageKit.Stat($"{shown} / {hidden}", "显示中 / 已隐藏"),
                PageKit.Stat(items.Count.ToString(), "盒内文件"),
                PageKit.Stat(FormatSize(DbSize()), "数据库大小"))));

            // 常用操作
            var actions = PageKit.Column(12);
            actions.Children.Add(PageKit.ActionRow("新建收纳盒", "只建空盒子，不会动你桌面上的任何文件",
                PageKit.Btn("去收纳盒页", (_, _) => Navigate("boxes"), primary: true)));
            actions.Children.Add(PageKit.Divider());

            var showAll = PageKit.Btn("全部显示", (_, _) => SetAllVisible(true));
            actions.Children.Add(PageKit.ActionRow("显示全部盒子", hidden == 0 ? "现在都显示着" : $"把 {hidden} 个隐藏的盒子放回桌面", showAll));
            actions.Children.Add(PageKit.Divider());

            var hideAll = PageKit.Btn("全部隐藏", (_, _) => SetAllVisible(false));
            actions.Children.Add(PageKit.ActionRow("隐藏全部盒子", "桌面暂时空出来；随时可以从收纳盒页放回", hideAll));
            actions.Children.Add(PageKit.Divider());

            actions.Children.Add(PageKit.ActionRow("数据目录", AppPaths.DataRoot,
                PageKit.Btn("打开", (_, _) => OpenFolder(AppPaths.DataRoot))));
            Host.Children.Add(PageKit.Card("常用操作", actions));

            // 最近更新的盒子（最常见的"找东西"入口）
            var recent = boxes.OrderByDescending(b => b.UpdatedAt).Take(5).ToList();
            if (recent.Count > 0)
            {
                var list = PageKit.Column(8);
                foreach (var box in recent)
                {
                    var captured = box;
                    var count = 0;
                    try { count = App.Repo?.Items(captured.Id, captured.Kind == BoxKind.Mapped).Count ?? 0; } catch { }
                    list.Children.Add(PageKit.ActionRow(
                        captured.Name,
                        $"{count} 个文件 · {(captured.IsHidden ? "已隐藏" : "显示中")} · {captured.UpdatedAt:MM-dd HH:mm}",
                        PageKit.Btn("打开盒子", (_, _) => App.OpenBox(captured))));
                }
                Host.Children.Add(PageKit.Card("最近更新", list));
            }

            App.Log($"OverviewPage: boxes={boxes.Count} shown={shown} items={items.Count}");
        }
        catch (Exception ex)
        {
            App.Log("OverviewPage 载入失败：" + ex);
            Host.Children.Add(PageKit.Card("载入失败", PageKit.Hint(ex.Message)));
        }
    }

    private void SetAllVisible(bool visible)
    {
        try
        {
            foreach (var box in App.Repo?.Boxes() ?? new())
            {
                box.IsHidden = !visible;
                App.Repo?.Upsert(box);
                if (visible) App.OpenBox(box); else App.CloseBox(box.Id);
            }
            App.NotifyBoxesChanged();
            App.Log($"概览：全部{(visible ? "显示" : "隐藏")}");
            Reload();
        }
        catch (Exception ex) { App.Log("批量显示/隐藏失败：" + ex); }
    }

    private static void Navigate(string tag)
    {
        // 通过主窗口的导航视图切页，保持左侧选中项同步
        App.Main?.SelectPage(tag);
    }

    private static long DbSize()
    {
        try { return File.Exists(AppPaths.DbFile) ? new FileInfo(AppPaths.DbFile).Length : 0; }
        catch { return 0; }
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : "0.#") + " " + units[i];
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
}
