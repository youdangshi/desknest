using DeskNest.App.Host;
using DeskNest.Core;
using DeskNest.Core.Data;

namespace DeskNest.App.Windows;

/// <summary>桌面窗口管理器：按数据库状态创建/销毁收纳盒窗口、组件窗口与桌宠窗口。</summary>
public sealed class DesktopWindows
{
    private readonly Dictionary<string, BoxWindow> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WidgetWindow> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private PetWindow? _pet;

    public void RestoreAll()
    {
        foreach (var box in AppServices.Repo.Boxes())
        {
            if (box.IsHidden) continue;
            ShowBox(box);
        }
        foreach (var widget in AppServices.Repo.Widgets())
        {
            if (!widget.IsVisible) continue;
            ShowWidget(widget);
        }
    }

    public void ShowBox(BoxRecord box)
    {
        if (_boxes.TryGetValue(box.Id, out var existing))
        {
            existing.Show();
            existing.ReloadItems();
            return;
        }
        var win = new BoxWindow(box);
        _boxes[box.Id] = win;
        win.Show();
    }

    public void ReloadBoxes()
    {
        var ids = AppServices.Repo.Boxes().Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _boxes.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            try { _boxes[id].Close(); } catch { }
            _boxes.Remove(id);
        }
        foreach (var box in AppServices.Repo.Boxes())
        {
            if (box.IsHidden)
            {
                if (_boxes.TryGetValue(box.Id, out var hidden)) hidden.Hide();
                continue;
            }
            ShowBox(box);
        }
    }

    public void NotifyBoxClosed(string boxId) => _boxes.Remove(boxId);

    public void ApplyLookToAll()
    {
        foreach (var win in _boxes.Values) win.ApplyLook();
        foreach (var win in _widgets.Values) win.ApplyLook();
    }

    public void SetLayerVisible(bool visible)
    {
        foreach (var win in _boxes.Values)
        {
            if (visible) win.Show(); else win.Hide();
        }
        foreach (var win in _widgets.Values)
        {
            if (visible) win.Show(); else win.Hide();
        }
        if (_pet is not null)
        {
            if (visible) _pet.Show(); else _pet.Hide();
        }
    }

    /// <summary>盒组切换：同一组内只显示被选中的盒，其余隐藏。</summary>
    public void SwitchGroupTo(string groupId, string boxId)
    {
        var group = AppServices.Repo.Groups().FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;
        foreach (var id in group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!_boxes.TryGetValue(id, out var win)) continue;
            if (id == boxId)
            {
                win.Show();
                win.ReloadItems();
                win.RefreshGroupTabsPublic();
            }
            else
            {
                win.Hide();
            }
        }
        group.ActiveMemberId = boxId;
        AppServices.Repo.Upsert(group);
        Log.Info($"组件组切换：{group.Name} → {boxId}");
    }

    public void ShowWidget(WidgetRecord widget)
    {
        if (_widgets.TryGetValue(widget.Id, out var existing))
        {
            existing.Refresh();
            existing.Show();
            return;
        }
        var win = new WidgetWindow(widget);
        _widgets[widget.Id] = win;
        win.Show();
    }

    public void NotifyWidgetClosed(string id) => _widgets.Remove(id);

    public void ShowPet()
    {
        if (_pet is not null)
        {
            _pet.Show();
            return;
        }
        _pet = new PetWindow();
        _pet.Closed += (_, _) => _pet = null;
        _pet.Show();
    }

    public void HidePet()
    {
        _pet?.Hide();
    }

    public void CloseAll()
    {
        foreach (var w in _boxes.Values) { try { w.Close(); } catch { } }
        foreach (var w in _widgets.Values) { try { w.Close(); } catch { } }
        try { _pet?.Close(); } catch { }
        _boxes.Clear();
        _widgets.Clear();
    }
}
