using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Windows;

public sealed partial class BoxWindow
{
    private readonly List<ItemRecord> _items = new();

    public void ReloadItems()
    {
        _items.Clear();
        _iconPanel.Children.Clear();
        _listPanel.Children.Clear();

        foreach (var item in AppServices.Repo.Items(_box.Id, _box.Kind == BoxKind.Mapped)
                     .OrderByDescending(i => i.IsFavorite).ThenBy(i => i.OrderIndex).ThenBy(i => i.DisplayName))
            _items.Add(item);

        if (_items.Count == 0)
        {
            _iconPanel.Children.Add(EmptyState());
            _status.Text = "空盒 · 把文件拖进来，或点标题栏 ＋";
            return;
        }

        foreach (var item in _items)
        {
            if (_box.ViewMode == "Icon") AddIconItem(item);
            else AddListItem(item);
        }
        _status.Text = $"{_items.Count} 项 · {UiKit.Fmt(_items.Sum(i => i.SizeBytes))}";
    }

    private UIElement EmptyState() => new Border
    {
        Width = Math.Max(120, Width - 48),
        Margin = new Thickness(4),
        Padding = new Thickness(14, 18, 14, 18),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = Theme.Desktop.BorderBrush,
        Child = UiKit.Col(
            UiKit.Text("把文件拖进来", 12.5, secondary: true),
            UiKit.Text("直接拖放即可；也可以在标题栏点 ＋", 11, secondary: true))
    };

    private void AddIconItem(ItemRecord item)
    {
        var stack = new StackPanel
        {
            Width = _box.IconSize + 34,
            Margin = new Thickness(3, 5, 3, 5),
            Cursor = Cursors.Hand,
            ToolTip = ItemPath(item)
        };
        stack.Children.Add(new Image
        {
            Width = _box.IconSize,
            Height = _box.IconSize,
            Source = IconCache.Get(ItemPath(item), item.Kind == ItemKind.Folder),
            Stretch = Stretch.Uniform
        });
        if (!_box.HideFileName)
        {
            stack.Children.Add(new TextBlock
            {
                Text = DisplayName(item),
                FontSize = _box.TextSize,
                Foreground = Theme.Desktop.ForegroundBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = _box.TextSize * 1.5 * Math.Max(1, _box.FileNameLines),
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
        if (item.IsMissing) stack.Children.Add(UiKit.Text("失效", 10, secondary: true));

        var menu = new ContextMenu();
        menu.Opened += (_, _) => BuildItemMenu(menu, item);
        stack.ContextMenu = menu;
        stack.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { OpenItem(item); e.Handled = true; }
        };
        _iconPanel.Children.Add(stack);
    }

    private void AddListItem(ItemRecord item)
    {
        var row = new Grid { Margin = new Thickness(2), Cursor = Cursors.Hand, ToolTip = ItemPath(item) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var img = new Image
        {
            Width = 18,
            Height = 18,
            Source = IconCache.Get(ItemPath(item), item.Kind == ItemKind.Folder),
            Stretch = Stretch.Uniform
        };
        Grid.SetColumn(img, 0);
        var name = new TextBlock
        {
            Text = DisplayName(item),
            FontSize = _box.TextSize,
            Foreground = Theme.Desktop.ForegroundBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };
        Grid.SetColumn(name, 1);
        var size = UiKit.Text(UiKit.Fmt(item.SizeBytes), 10.5, secondary: true);
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 2);
        row.Children.Add(img);
        row.Children.Add(name);
        row.Children.Add(size);

        var menu = new ContextMenu();
        menu.Opened += (_, _) => BuildItemMenu(menu, item);
        row.ContextMenu = menu;
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { OpenItem(item); e.Handled = true; }
        };
        _listPanel.Children.Add(row);
    }

    private string DisplayName(ItemRecord item)
        => _box.ShowExtensions ? item.DisplayName : Path.GetFileNameWithoutExtension(item.DisplayName);

    private static string ItemPath(ItemRecord item) => item.StoredPath ?? item.SourcePath;

    private void BuildItemMenu(ContextMenu menu, ItemRecord item)
    {
        menu.Items.Clear();
        menu.Items.Add(MenuAction("打开", () => OpenItem(item)));
        menu.Items.Add(MenuAction("在资源管理器中显示", () => RevealItem(item)));
        menu.Items.Add(MenuAction("复制路径", () => CopyPath(ItemPath(item))));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("重命名…", () => RenameItem(item)));
        menu.Items.Add(MenuAction(item.IsFavorite ? "取消固定" : "固定到顶部", () =>
        {
            item.IsFavorite = !item.IsFavorite;
            AppServices.Repo.Upsert(item);
            ReloadItems();
        }));
        menu.Items.Add(MenuAction("移动到其他盒子…", () => ShowMoveToBoxMenu(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("从盒中移除（文件还原到原位置）", () => RemoveItem(item)));
    }

    public void OpenItem(ItemRecord item)
    {
        try
        {
            var path = ItemPath(item);
            if (!FileOps.Exists(path))
            {
                item.IsMissing = true;
                AppServices.Repo.Upsert(item);
                ReloadItems();
                Ui.Notifications.Toast($"文件已不在原位置：{item.DisplayName}");
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开失败 {item.DisplayName}：{ex.Message}");
            Ui.Notifications.Toast("打开失败：" + ex.Message);
        }
    }

    private void RevealItem(ItemRecord item)
    {
        try
        {
            var path = ItemPath(item);
            if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            else System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { Ui.Notifications.Toast("打开资源管理器失败：" + ex.Message); }
    }

    private static void CopyPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
            Ui.Notifications.Toast("路径已复制", 2);
        }
        catch { }
    }

    private void RenameItem(ItemRecord item)
    {
        var input = new TextBox { Text = item.DisplayName, Width = 280, FontSize = 13, Margin = new Thickness(0, 6, 0, 10) };
        var okBtn = UiKit.Btn("确定", () => { }, true);
        var cancelBtn = UiKit.Btn("取消", () => { }, false);
        var win = new Window
        {
            Title = "重命名",
            Width = 380,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Desktop.BackgroundBrush,
            Content = UiKit.Card(null, UiKit.Col(
                UiKit.Text("新名称（存储的文件会一起改名，还原时使用新名称）", 11.5, secondary: true),
                input,
                UiKit.Row(okBtn, cancelBtn)))
        };
        cancelBtn.Click += (_, _) => win.Close();
        okBtn.Click += (_, _) =>
        {
            var newName = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName) || newName == item.DisplayName)
            {
                win.Close();
                return;
            }
            try
            {
                var oldPath = ItemPath(item);
                var dir = Path.GetDirectoryName(oldPath)!;
                var target = FileOps.UniquePath(dir, newName);
                if (Directory.Exists(oldPath)) Directory.Move(oldPath, target);
                else File.Move(oldPath, target);
                item.DisplayName = Path.GetFileName(target);
                item.StoredPath = target;
                item.Ext = Path.GetExtension(target).ToLowerInvariant();
                AppServices.Repo.Upsert(item);
                ReloadItems();
            }
            catch (Exception ex) { Ui.Notifications.Toast("重命名失败：" + ex.Message); }
            win.Close();
        };
        win.ShowDialog();
    }

    private void ShowMoveToBoxMenu(ItemRecord item)
    {
        var menu = new ContextMenu();
        foreach (var box in AppServices.Repo.Boxes().Where(b => b.Id != _box.Id && b.Kind != BoxKind.Mapped).Take(12))
            menu.Items.Add(MenuAction(box.Name, () => MoveItemTo(item, box)));
        if (menu.Items.Count == 0) menu.Items.Add(MenuAction("没有其他盒子", () => { }));
        menu.IsOpen = true;
    }

    private void MoveItemTo(ItemRecord item, BoxRecord target)
    {
        try
        {
            var res = AppServices.FileOps.MoveTo(ItemPath(item), StorageRootOf(target));
            if (!res.Ok)
            {
                Ui.Notifications.Toast("移动失败：" + res.Error);
                return;
            }
            AppServices.Repo.DeleteItem(item.Id);
            AppServices.Repo.Upsert(new ItemRecord
            {
                BoxId = target.Id,
                Kind = item.Kind,
                DisplayName = Path.GetFileName(res.StoredPath!),
                SourcePath = item.SourcePath,
                StoredPath = res.StoredPath,
                Ext = Path.GetExtension(res.StoredPath!).ToLowerInvariant(),
                SizeBytes = item.SizeBytes,
                AddedAt = DateTimeOffset.Now
            });
            ReloadItems();
            Ui.Notifications.Toast($"已移动到「{target.Name}」");
        }
        catch (Exception ex) { Ui.Notifications.Toast("移动失败：" + ex.Message); }
    }

    private void RemoveItem(ItemRecord item)
    {
        try
        {
            if (_box.Kind == BoxKind.Mapped)
            {
                AppServices.Repo.DeleteItem(item.Id);
                ReloadItems();
                Ui.Notifications.Toast($"已移除引用（源文件没有改动）：{item.DisplayName}");
                return;
            }
            var res = AppServices.FileOps.RestoreToOriginal(ItemPath(item), item.SourcePath);
            if (!res.Ok)
            {
                Ui.Notifications.Toast("还原失败：" + res.Error);
                return;
            }
            AppServices.Repo.DeleteItem(item.Id);
            ReloadItems();
            Ui.Notifications.Toast($"已还原到：{res.StoredPath}", 10, action: () => ReAddBack(item, res.StoredPath!));
        }
        catch (Exception ex) { Ui.Notifications.Toast("移除失败：" + ex.Message); }
    }

    private void ReAddBack(ItemRecord item, string path)
    {
        try
        {
            var back = AppServices.FileOps.MoveIntoStorage(path, StorageRoot());
            if (!back.Ok)
            {
                Ui.Notifications.Toast("撤销失败：" + back.Error);
                return;
            }
            item.StoredPath = back.StoredPath;
            item.IsMissing = false;
            AppServices.Repo.Upsert(item);
            ReloadItems();
            Ui.Notifications.Toast("已撤销");
        }
        catch (Exception ex) { Ui.Notifications.Toast("撤销失败：" + ex.Message); }
    }
}
