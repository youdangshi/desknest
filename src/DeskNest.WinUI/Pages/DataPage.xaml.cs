using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DeskNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 数据与备份：数据目录/数据库/设置的位置，备份列表与"立即备份"。
/// 备份实现直接复用 Core 的 BackupService（与桌面版同一份，zip 格式互通）。
/// </summary>
public sealed partial class DataPage : Page
{
    private TextBlock? _status;

    public DataPage()
    {
        InitializeComponent();
        Build();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshBackups();
    }

    private void Build()
    {
        Host.Children.Add(PageKit.PageTitle("数据与备份"));
        Host.Children.Add(PageKit.PageIntro("所有数据都在你自己的电脑上，没有云端。备份是一个 zip 包，换机或重装时能整个带回来。"));

        // 数据位置
        var loc = PageKit.Column(10);
        loc.Children.Add(PageKit.InfoRow("数据目录", AppPaths.DataRoot));
        loc.Children.Add(PageKit.InfoRow("数据库", AppPaths.DbFile));
        loc.Children.Add(PageKit.InfoRow("设置", AppPaths.SettingsFile));
        loc.Children.Add(PageKit.InfoRow("日志", AppPaths.LogFile));
        loc.Children.Add(PageKit.InfoRow("盒子文件", AppPaths.DefaultManagedRoot));
        loc.Children.Add(PageKit.ActionRow("打开数据目录", null, PageKit.Btn("打开", (_, _) => OpenFolder(AppPaths.DataRoot))));
        Host.Children.Add(PageKit.Card("数据位置", loc));

        // 搜索索引（全盘搜索用；行数可能上百万，是数据库体积的主因）
        var idx = PageKit.Column(10);
        long idxCount = 0;
        try { idxCount = App.Db?.Count("search_index") ?? 0; } catch { }
        idx.Children.Add(PageKit.InfoRow("已索引条目", $"{idxCount:N0} 条（全盘搜索用，不是你的文件）"));
        idx.Children.Add(PageKit.ActionRow("重建 / 清空索引",
            "索引随时可以重建；清空后再搜索会现建。数据库体积偏大时先清这里",
            PageKit.Btn("清空索引", async (_, _) => await ClearSearchIndex())));
        Host.Children.Add(PageKit.Card("搜索索引", idx));

        // 备份
        var backupCol = PageKit.Column(10);
        _status = new TextBlock { FontSize = 11.5, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        backupCol.Children.Add(PageKit.InfoRow("保留份数", (App.Backup?.RetentionCount ?? 7) + " 份（超出后自动清理最旧的）"));
        backupCol.Children.Add(PageKit.ActionRow("立即备份", "把数据库、设置和盒子内容打成一个 zip",
            PageKit.Btn("备份现在", async (_, _) => await CreateBackup(), primary: true)));
        backupCol.Children.Add(PageKit.Divider());
        _backupListHost = PageKit.Column(8);
        backupCol.Children.Add(_backupListHost);
        backupCol.Children.Add(_status);
        Host.Children.Add(PageKit.Card("备份", backupCol));
    }

    private StackPanel? _backupListHost;

    private void RefreshBackups()
    {
        if (_backupListHost is null) return;
        _backupListHost.Children.Clear();
        try
        {
            var list = App.Backup?.List().OrderByDescending(b => b.CreatedAt).Take(6).ToList() ?? new();
            if (list.Count == 0)
            {
                _backupListHost.Children.Add(PageKit.Hint("还没有备份。建议先点上面「备份现在」做一份。"));
                return;
            }
            foreach (var b in list)
            {
                var captured = b;
                _backupListHost.Children.Add(PageKit.ActionRow(
                    captured.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    $"{Path.GetFileName(captured.Path)} · {OverviewPage.FormatSize(captured.SizeBytes)}",
                    PageKit.Btn("打开位置", (_, _) => OpenFolder(Path.GetDirectoryName(captured.Path) ?? AppPaths.BackupRoot))));
            }
            App.Log($"DataPage: backups={list.Count}");
        }
        catch (Exception ex)
        {
            App.Log("读取备份列表失败：" + ex);
            _backupListHost.Children.Add(PageKit.Hint("读取备份列表失败：" + ex.Message));
        }
    }

    private async Task CreateBackup()
    {
        if (_status is null) return;
        _status.Text = "正在备份…（文件多时可能要几秒）";
        try
        {
            var info = await Task.Run(() => App.Backup!.Create("manual"));
            _status.Text = $"已备份：{Path.GetFileName(info.Path)}（{OverviewPage.FormatSize(info.SizeBytes)}）";
            App.Log($"手动备份完成：{info.Path}");
            RefreshBackups();
        }
        catch (Exception ex)
        {
            _status.Text = "备份失败：" + ex.Message;
            App.Log("备份失败：" + ex);
        }
    }

    /// <summary>
    /// 清空全盘搜索索引。索引是可再生的（下次搜索时重建），但 330 万行清完再做 VACUUM
    /// 要几十秒，所以确认框里说清楚，且放在后台线程跑。
    /// </summary>
    private async Task ClearSearchIndex()
    {
        var confirm = new ContentDialog
        {
            Title = "清空搜索索引？",
            Content = "只会删掉「全盘搜索」的索引条目（会重新生成），不会删你的任何文件，也不会动收纳盒。\n" +
                      "清空后第一次搜索会慢一些。数据库体积会明显变小。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        if (_status is not null) _status.Text = "正在清空索引并压缩数据库…（可能要几十秒）";
        try
        {
            await Task.Run(() =>
            {
                App.Db?.Exec("DELETE FROM search_index");
                // VACUUM 在 WAL/有活动连接时可能失败；失败不影响"已清空"这个结果，单独吞掉
                try { App.Db?.Exec("VACUUM"); }
                catch (Exception ex) { App.Log("VACUUM 失败（不影响清空结果）：" + ex.Message); }
            });
            if (_status is not null) _status.Text = "索引已清空。";
            App.Log("搜索索引已清空 + VACUUM");
            // 数字要重新读，所以整页重建一次
            Host.Children.Clear();
            Build();
            RefreshBackups();
        }
        catch (Exception ex)
        {
            if (_status is not null) _status.Text = "清空失败：" + ex.Message;
            App.Log("清空索引失败：" + ex);
        }
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
