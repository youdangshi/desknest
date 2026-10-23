using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeskNest.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 搜索页。三类来源：
/// 1) 收纳盒 / 桌面组件 / 待办 / 便签 —— 直接查本地表（几毫秒，永远可用）；
/// 2) 全盘文件索引 —— 直接 SQL `LIKE`，**不**把 300 多万条读进内存（桌面版就是读进字典的，
///    那是内存大户）；代价是全表扫描约 1 秒，所以放后台线程 + 输入防抖 + 取消上一次。
/// 结果按"你自己的东西优先、文件在后"排。
/// </summary>
public sealed partial class SearchPage : Page
{
    // DispatcherQueueTimer 只能由队列创建（没有无参构造）
    private readonly DispatcherQueueTimer _debounce =
        DispatcherQueue.GetForCurrentThread().CreateTimer();
    private CancellationTokenSource? _cts;
    private string _pendingQuery = "";
    private long _indexCount;

    public SearchPage()
    {
        InitializeComponent();

        _debounce.Interval = TimeSpan.FromMilliseconds(280);
        _debounce.IsRepeating = false;
        _debounce.Tick += async (_, _) => await RunSearchAsync(_pendingQuery);

        QueryBox.TextChanged += (_, _) =>
        {
            _pendingQuery = QueryBox.Text;
            _debounce.Stop();
            _debounce.Start();
        };
        QueryBox.KeyDown += async (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            _debounce.Stop();
            await RunSearchAsync(QueryBox.Text);
        };

        // 进页面就聚焦输入框，省一次点击
        Loaded += (_, _) =>
        {
            QueryBox.Focus(FocusState.Programmatic);
            _indexCount = App.Db?.Count("search_index") ?? 0;
            ShowHints();
            App.Log($"SearchPage: 索引 {_indexCount} 条");
        };
    }

    private void ShowHints()
    {
        ResultHost.Children.Clear();
        SubtitleText.Text = _indexCount > 0
            ? $"全盘索引 {_indexCount:N0} 条；输入关键词开始搜（索引的是文件名，不是全文）"
            : "索引是空的：先去「数据与备份」页确认索引状态，或直接搜盒子/组件/待办/便签";

        var hint = PageKit.Column(6);
        hint.Children.Add(PageKit.Hint("试试搜：截图 · 发票 · 报销 · 你的盒子名"));
        ResultHost.Children.Add(hint);
    }

    private async Task RunSearchAsync(string raw)
    {
        var q = (raw ?? "").Trim();
        if (q.Length == 0) { ShowHints(); return; }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        ResultHost.Children.Clear();
        ResultHost.Children.Add(PageKit.Hint("正在搜索…"));

        var local = SearchLocal(q);
        var files = new List<DeskNest.Core.Services.SearchService.IndexEntry>();
        try
        {
            files = await Task.Run(() =>
            {
                if (App.Repo is null) return new List<DeskNest.Core.Services.SearchService.IndexEntry>();
                var rows = App.Repo.SearchPaths(q, 400);
                return rows;
            }, token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            App.Log("搜索文件索引失败：" + ex);
            ResultHost.Children.Clear();
            ResultHost.Children.Add(PageKit.Card("搜索出错", PageKit.Hint(ex.Message)));
            return;
        }
        if (token.IsCancellationRequested) return;

        // 文件名里含关键词的排前面，路径命中的排后面；同分按路径长度（越短越可能是你要的）
        var needle = q.ToLowerInvariant();
        var ranked = files
            .Select(f =>
            {
                var name = System.IO.Path.GetFileName(f.Path);
                var nameHit = name.Contains(q, StringComparison.OrdinalIgnoreCase);
                var score = nameHit ? (name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 300 : 200) : 60;
                return (entry: f, name, score);
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.entry.Path.Length)
            .Take(60)
            .ToList();

        Render(local, ranked, q);
        App.Log($"搜索「{q}」：本地 {local.Count} 项，文件 {ranked.Count} 条（候选 {files.Count}）");
    }

    /// <summary>本地表搜索：盒子 / 组件 / 待办 / 便签。</summary>
    private List<LocalHit> SearchLocal(string q)
    {
        var hits = new List<LocalHit>();
        if (App.Repo is null) return hits;

        try
        {
            foreach (var box in App.Repo.Boxes().Where(b => b.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                var count = 0;
                try { count = App.Repo.Items(box.Id, box.Kind == BoxKind.Mapped).Count; } catch { }
                hits.Add(new LocalHit(LocalKind.Box, "\uE8B7", box.Name, $"收纳盒 · {count} 个文件", () => App.OpenBox(box)));
            }
        }
        catch (Exception ex) { App.Log("搜索盒子失败：" + ex.Message); }

        try
        {
            foreach (var widget in App.Repo.Widgets().Where(w => w.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                var captured = widget;
                hits.Add(new LocalHit(LocalKind.Widget, "\uE7F4", captured.Name,
                    $"桌面组件 · {captured.Kind}", () => App.OpenWidget(captured)));
            }
        }
        catch (Exception ex) { App.Log("搜索组件失败：" + ex.Message); }

        try
        {
            foreach (var todo in App.Repo.Todos().Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(10))
            {
                hits.Add(new LocalHit(LocalKind.Todo, "\uE73A", todo.Title,
                    todo.IsDone ? "待办 · 已完成" : "待办 · 未完成", null));
            }
        }
        catch (Exception ex) { App.Log("搜索待办失败：" + ex.Message); }

        try
        {
            foreach (var note in App.Repo.Notes().Where(n => n.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                         || n.Body.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(10))
            {
                hits.Add(new LocalHit(LocalKind.Note, "\uE70B", string.IsNullOrWhiteSpace(note.Title) ? "（无标题便签）" : note.Title,
                    "便签 · " + note.UpdatedAt.ToString("MM-dd HH:mm"), null));
            }
        }
        catch (Exception ex) { App.Log("搜索便签失败：" + ex.Message); }

        return hits;
    }

    private void Render(List<LocalHit> local, List<(DeskNest.Core.Services.SearchService.IndexEntry entry, string name, int score)> files, string q)
    {
        ResultHost.Children.Clear();

        if (local.Count == 0 && files.Count == 0)
        {
            ResultHost.Children.Add(PageKit.Card("没有结果",
                PageKit.Hint($"「{q}」在盒子、组件、待办、便签和 {_indexCount:N0} 条文件索引里都没找到。文件名换个词试试？")));
            return;
        }

        if (local.Count > 0)
        {
            var col = PageKit.Column(6);
            foreach (var hit in local) col.Children.Add(BuildLocalRow(hit));
            ResultHost.Children.Add(PageKit.Card($"你的东西（{local.Count}）", col));
        }

        if (files.Count > 0)
        {
            var col = PageKit.Column(2);
            foreach (var (entry, name, _) in files) col.Children.Add(BuildFileRow(entry, name));
            ResultHost.Children.Add(PageKit.Card($"文件（{files.Count} 条，来自全盘索引）", col));
        }
    }

    private UIElement BuildLocalRow(LocalHit hit)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 4, 4, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new FontIcon { Glyph = hit.Glyph, FontSize = 14, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = hit.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        text.Children.Add(new TextBlock { Text = hit.Subtitle, FontSize = 10.5, Opacity = 0.55 });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (hit.Open is not null)
        {
            row.Tag = hit;
            row.PointerEntered += (_, _) => row.Background = HoverBrush(row);
            row.PointerExited += (_, _) => row.Background = null;
            row.DoubleTapped += (_, e) => { hit.Open(); e.Handled = true; };
            row.Tapped += (_, e) => { hit.Open(); e.Handled = true; };
        }
        return row;
    }

    private UIElement BuildFileRow(DeskNest.Core.Services.SearchService.IndexEntry entry, string name)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(4, 3, 4, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = Windows.IconLoader.Get(entry.Path, 24);
        if (icon is not null)
            row.Children.Add(new Image { Source = icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
        else
            row.Children.Add(new FontIcon { Glyph = entry.IsDirectory ? "\uE8B7" : "\uE8A5", FontSize = 14, Opacity = 0.65 });

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontSize = 12.5, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock
        {
            Text = entry.Path,
            FontSize = 10.5,
            Opacity = 0.5,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Children.Add(title);
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var size = new TextBlock
        {
            Text = entry.IsDirectory ? "" : FormatSize(entry.Size),
            FontSize = 10.5,
            Opacity = 0.45,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);

        row.PointerEntered += (_, _) => row.Background = HoverBrush(row);
        row.PointerExited += (_, _) => row.Background = null;
        row.DoubleTapped += (_, e) => { OpenPath(entry.Path); e.Handled = true; };
        row.ContextFlyout = BuildFileMenu(entry.Path);
        return row;
    }

    private static MenuFlyout BuildFileMenu(string path)
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "打开" };
        open.Click += (_, _) => OpenPath(path);
        var reveal = new MenuFlyoutItem { Text = "在资源管理器中显示" };
        reveal.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { App.Log("定位失败：" + ex.Message); }
        };
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        return menu;
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            {
                App.Log("搜索打开失败：路径不存在 " + path);
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("搜索打开失败：" + ex.Message); }
    }

    private static Brush? HoverBrush(FrameworkElement el) => PageKit.ThemedBrush(el, "DnSubtleBg");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    private enum LocalKind { Box, Widget, Todo, Note }

    private sealed record LocalHit(LocalKind Kind, string Glyph, string Title, string Subtitle, Action? Open);
}
