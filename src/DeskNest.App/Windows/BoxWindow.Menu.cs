using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;

namespace DeskNest.App.Windows;

public sealed partial class BoxWindow
{
    private void ShowBoxMenu()
    {
        var menu = new ContextMenu();
        BuildBoxMenu(menu);
        menu.IsOpen = true;
    }

    /// <summary>右键盒子空白处也会调用它（与标题栏 ⋯ 按钮共用同一套条目）。</summary>
    public void BuildBoxContextMenu(ContextMenu menu) => BuildBoxMenu(menu);

    private void BuildBoxMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(MenuAction("添加文件…", AddFilesByDialog));
        menu.Items.Add(MenuAction("打开存储文件夹", OpenStorageFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction(_box.ViewMode == "Icon" ? "切换为列表视图" : "切换为图标视图", ToggleViewMode));
        menu.Items.Add(MenuAction("切换排序方式（名称 / 加入时间 / 大小）", CycleSortMode));
        menu.Items.Add(MenuAction(_box.HideFileName ? "显示文件名" : "隐藏文件名", ToggleFileName));
        menu.Items.Add(MenuAction(_box.AutoHide ? "关闭自动隐藏" : "开启自动隐藏", ToggleAutoHide));
        menu.Items.Add(MenuAction(_box.StyleScope == StyleScope.Inherit ? "单独设置样式（不再跟随全局）" : "样式跟随全局", ToggleStyleScope));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction(_box.LockPosition ? "解锁位置" : "锁定位置", () =>
        {
            _box.LockPosition = !_box.LockPosition;
            AppServices.Repo.Upsert(_box);
        }));
        menu.Items.Add(MenuAction(_box.LockSize ? "解锁尺寸" : "锁定尺寸", () =>
        {
            _box.LockSize = !_box.LockSize;
            ResizeMode = _box.LockSize ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            AppServices.Repo.Upsert(_box);
        }));
        menu.Items.Add(MenuAction("把我的外观应用到所有盒（统一样式）", ApplyLookToAllBoxes));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("删除盒子（盒内文件还原到原位置）", DeleteBox));
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    private void OpenStorageFolder()
    {
        try
        {
            var root = StorageRoot();
            Directory.CreateDirectory(root);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{root}\"");
        }
        catch (Exception ex) { Ui.Notifications.Toast("打开失败：" + ex.Message); }
    }

    private void ToggleViewMode()
    {
        _box.ViewMode = _box.ViewMode == "Icon" ? "List" : "Icon";
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    private void CycleSortMode()
    {
        _box.SortMode = _box.SortMode switch { "Name" => "AddedAt", "AddedAt" => "Size", _ => "Name" };
        AppServices.Repo.Upsert(_box);
        ReloadItems();
        Ui.Notifications.Toast("排序方式：" + _box.SortMode, 2);
    }

    private void ToggleFileName()
    {
        _box.HideFileName = !_box.HideFileName;
        AppServices.Repo.Upsert(_box);
        ReloadItems();
    }

    private void ToggleAutoHide()
    {
        _box.AutoHide = !_box.AutoHide;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
    }

    private void ToggleStyleScope()
    {
        _box.StyleScope = _box.StyleScope == StyleScope.Inherit ? StyleScope.Override : StyleScope.Inherit;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        Ui.Notifications.Toast(_box.StyleScope == StyleScope.Inherit
            ? "已改为跟随全局样式" : "已改为单独样式（主窗口→外观里可调）", 3);
    }

    private void ApplyLookToAllBoxes()
    {
        foreach (var other in AppServices.Repo.Boxes())
        {
            other.Opacity = _box.Opacity;
            other.Material = _box.Material;
            other.MaterialIntensity = _box.MaterialIntensity;
            other.CornerRadius = _box.CornerRadius;
            other.TextSize = _box.TextSize;
            other.IconSize = _box.IconSize;
            AppServices.Repo.Upsert(other);
        }
        if (App.Current is App app) app.Desktop.ApplyLookToAll();
        Ui.Notifications.Toast("已把当前盒的外观应用到所有盒");
    }

    private void DeleteBox()
    {
        var confirm = MessageBox.Show(
            $"确定删除盒子「{_box.Name}」吗？\n\n盒内文件会逐个还原到原位置；原位置不存在时还原到桌面。",
            "删除收纳盒", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        var items = AppServices.Repo.Items(_box.Id, _box.Kind == BoxKind.Mapped);
        var failed = 0;
        if (_box.Kind != BoxKind.Mapped)
        {
            foreach (var item in items)
            {
                var res = AppServices.FileOps.RestoreToOriginal(item.StoredPath ?? item.SourcePath, item.SourcePath);
                if (!res.Ok) failed++;
            }
        }
        AppServices.Repo.DeleteBox(_box.Id);
        Ui.Notifications.Toast(failed > 0 ? $"有 {failed} 个文件还原失败（见日志）" : "盒子已删除", failed > 0 ? 6 : 3);
        Close();
        if (App.Current is App app) app.Desktop.NotifyBoxClosed(_box.Id);
    }

    public void RefreshGroupTabsPublic()
    {
        _tabStrip.Children.Clear();
        if (_box.GroupId is null) return;
        var group = AppServices.Repo.Groups().FirstOrDefault(g => g.Id == _box.GroupId);
        if (group is null) return;

        var members = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(AppServices.Repo.Box).Where(b => b is not null).Select(b => b!).ToList();
        if (members.Count <= 1) return;

        foreach (var m in members)
        {
            var active = m.Id == _box.Id;
            var tab = new Button
            {
                Content = group.TitleDisplay == "Icon" ? "▦" : m.Name,
                FontSize = 11.5,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2, 2, 2, 0),
                Background = active ? Theme.Desktop.AccentBrush : Brushes.Transparent,
                Foreground = active ? Brushes.White : Theme.Desktop.SecondaryBrush,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            tab.Style = ControlStyles.NavButtonStyle();
            if (active) tab.Background = Theme.Desktop.AccentBrush;
            tab.Click += (_, e) =>
            {
                SwitchToMember(m.Id);
                e.Handled = true;
            };
            _tabStrip.Children.Add(tab);
        }
    }

    private void SwitchGroup(int direction)
    {
        if (_box.GroupId is null) return;
        var group = AppServices.Repo.Groups().FirstOrDefault(g => g.Id == _box.GroupId);
        if (group is null) return;
        var ids = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (ids.Count <= 1) return;
        var idx = ids.IndexOf(_box.Id);
        if (idx < 0) return;
        var next = (idx + direction + ids.Count) % ids.Count;
        SwitchToMember(ids[next]);
    }

    private void SwitchToMember(string boxId)
    {
        if (App.Current is App app) app.Desktop.SwitchGroupTo(_box.GroupId!, boxId);
    }
}
