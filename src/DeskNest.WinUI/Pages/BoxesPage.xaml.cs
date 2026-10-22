using System;
using System.Linq;
using System.Threading.Tasks;
using DeskNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 收纳盒页：新建盒子（只建空盒子，不动用户文件）、盒子列表（显示/隐藏、打开、删除）。
/// 数据直接读写 Core 仓库（与 WPF 版同库互通）；盒子窗口的开关走 App.OpenBox/CloseBox。
/// </summary>
public sealed partial class BoxesPage : Page
{
    public BoxesPage()
    {
        InitializeComponent();
        NewBoxButton.Click += async (_, _) => await NewBoxDialog();
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
        // 页面离开导航缓存后订阅就失效，防重复触发与泄漏
        App.BoxesChanged -= OnBoxesChanged;
    }

    private void OnBoxesChanged()
    {
        // 通知可能来自盒子窗口（同进程 UI 线程），保险起见回 Dispatcher
        DispatcherQueue.TryEnqueue(Reload);
    }

    private void Reload()
    {
        BoxListHost.Children.Clear();
        if (App.Repo is null)
        {
            EmptyHint.Text = "数据库还没准备好。";
            EmptyHint.Visibility = Visibility.Visible;
            return;
        }
        var boxes = App.Repo.Boxes();
        EmptyHint.Text = "还没有收纳盒，点上面「新建收纳盒」试试。";
        EmptyHint.Visibility = boxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var box in boxes) BoxListHost.Children.Add(BuildBoxRow(box));
    }

    private UIElement BuildBoxRow(BoxRecord box)
    {
        var captured = box;
        var kind = captured.Kind == BoxKind.Mapped ? "映射盒" : "普通盒";
        var itemCount = 0;
        try { itemCount = App.Repo?.Items(captured.Id, captured.Kind == BoxKind.Mapped).Count ?? 0; } catch { }
        var shown = App.BoxWindows.Any(w => w.BoxId == captured.Id);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

        grid.Children.Add(new FontIcon
        {
            Glyph = captured.Kind == BoxKind.Mapped ? "\uE8B7" : "\uE8A5",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center
        });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        info.Children.Add(new TextBlock { Text = captured.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13 });
        info.Children.Add(new TextBlock
        {
            Text = $"{kind} · {itemCount} 个文件 · {(shown ? "显示中" : "已隐藏")}",
            FontSize = 11,
            Opacity = 0.65
        });
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 2);

        var initialOn = !captured.IsHidden;
        var showToggle = new ToggleSwitch
        {
            IsOn = initialOn,
            OnContent = "显示",
            OffContent = "隐藏",
            MinWidth = 0
        };
        showToggle.Toggled += (_, _) =>
        {
            // 防重入：Reload 重建行时 IsOn 赋值会再触发一次 Toggled，值没变就跳过
            var stored = App.Repo?.Box(captured.Id);
            if (stored is null || showToggle.IsOn == !stored.IsHidden) return;
            captured.IsHidden = !showToggle.IsOn;
            try { App.Repo?.Upsert(captured); } catch (Exception ex) { App.Log("显示/隐藏保存失败：" + ex.Message); }
            if (showToggle.IsOn) App.OpenBox(captured); else App.CloseBox(captured.Id);
            Reload();
        };
        actions.Children.Add(showToggle);

        var openBtn = new Button { Content = "打开", Padding = new Thickness(12, 4, 12, 4) };
        openBtn.Click += (_, _) => App.OpenBox(captured);
        actions.Children.Add(openBtn);

        var deleteBtn = new Button { Content = "删除", Padding = new Thickness(12, 4, 12, 4) };
        deleteBtn.Click += async (_, _) => await DeleteBox(captured);
        actions.Children.Add(deleteBtn);

        grid.Children.Add(actions);

        return new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Child = grid
        };
    }

    /// <summary>新建收纳盒：**只创建一个空盒子**，不移动任何文件。</summary>
    private async Task NewBoxDialog()
    {
        var nameInput = new TextBox { PlaceholderText = "新收纳盒" };
        var kindChoice = new ComboBox { SelectedIndex = 0, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        kindChoice.Items.Add("普通盒（文件移动进存储目录）");
        kindChoice.Items.Add("映射盒（只存路径，不动文件）");
        var hint = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        void RefreshHint()
        {
            var name = string.IsNullOrWhiteSpace(nameInput.Text) ? "新收纳盒" : nameInput.Text.Trim();
            hint.Text = kindChoice.SelectedIndex == 1
                ? "只登记这些文件/文件夹的路径，不搬动它们"
                : "盒子存储位置：" + System.IO.Path.Combine(AppPaths.DefaultManagedRoot, Windows.BoxWindow.SanitizeName(name))
                  + "\n不会自动搬走桌面上的任何文件";
        }
        nameInput.TextChanged += (_, _) => RefreshHint();
        kindChoice.SelectionChanged += (_, _) => RefreshHint();
        RefreshHint();

        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = "名称", FontSize = 12, Opacity = 0.8 });
        panel.Children.Add(nameInput);
        panel.Children.Add(new TextBlock { Text = "类型", FontSize = 12, Opacity = 0.8 });
        panel.Children.Add(kindChoice);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            Title = "新建收纳盒",
            Content = panel,
            PrimaryButtonText = "创建空盒子",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var name = string.IsNullOrWhiteSpace(nameInput.Text) ? "新收纳盒" : nameInput.Text.Trim();
            var mapped = kindChoice.SelectedIndex == 1;
            // 新盒子错开摆放，避免叠在一起（WPF 版 NextBoxSpot 的简化版：按已有盒子数量错位）
            var count = App.Repo?.Boxes().Count ?? 0;
            var box = new BoxRecord
            {
                Name = name,
                Kind = mapped ? BoxKind.Mapped : BoxKind.Normal,
                MarginX = 160 + (count % 6) * 48,
                MarginY = 120 + (count % 6) * 40,
                MappedPath = mapped ? AppPaths.DefaultManagedRoot : null
            };
            App.Repo?.Upsert(box);
            App.Log($"新建盒子「{name}」（{(mapped ? "映射" : "普通")}）");
            App.OpenBox(box);
            Reload();
        }
        catch (Exception ex)
        {
            App.Log("新建盒子失败：" + ex);
            await ShowMessage("新建失败：" + ex.Message);
        }
    }

    private async Task DeleteBox(BoxRecord box)
    {
        if (App.Boxes is null) return;
        var dialog = new ContentDialog
        {
            Title = "删除收纳盒",
            Content = $"删除盒子「{box.Name}」并把盒内文件还原到原位置？（原位置不存在则还原到桌面）",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var outcome = App.Boxes.Delete(box);
            App.CloseBox(box.Id);
            if (outcome.StillInDatabase)
            {
                await ShowMessage("删除没生效：记录仍然在库里，请把日志发给开发者");
            }
            else
            {
                var extra = outcome.Failed > 0 ? $"，{outcome.Failed} 个文件还原失败（见日志）" : "";
                await ShowMessage($"已删除「{box.Name}」，还原 {outcome.Restored} 个文件{extra}");
            }
        }
        catch (Exception ex)
        {
            App.Log("删除盒子失败：" + ex);
            await ShowMessage("删除失败：" + ex.Message);
        }
        Reload();
    }

    private Task ShowMessage(string message)
    {
        return new ContentDialog
        {
            Title = "收纳盒",
            Content = message,
            CloseButtonText = "知道了",
            XamlRoot = this.XamlRoot
        }.ShowAsync().AsTask();
    }
}
