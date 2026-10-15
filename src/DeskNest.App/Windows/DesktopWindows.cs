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

    /// <summary>当前打开的收纳盒窗口（多显示器页要按屏幕统计与搬运它们）。</summary>
    public IReadOnlyDictionary<string, BoxWindow> Boxes => _boxes;

    /// <summary>当前打开的桌面组件窗口。</summary>
    public IReadOnlyDictionary<string, WidgetWindow> Widgets => _widgets;

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
            try { CloseSafely(_boxes[id]); } catch { }
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

    /// <summary>关掉某个盒子的桌面窗口（删盒子时必须真的关掉，不能只从数据库里删）。</summary>
    public void CloseBox(string boxId)
    {
        if (!_boxes.TryGetValue(boxId, out var win)) return;
        try { CloseSafely(win); }
        catch (Exception ex) { Log.Warn($"关闭盒子窗口失败 {boxId}：{ex.Message}"); }
        _boxes.Remove(boxId);
    }

    /// <summary>
    /// 关闭桌面层窗口：**先脱离桌面层再关**。
    /// 盒子的父窗口是 explorer 的 WorkerW（跨进程父子关系），挂着父窗口直接销毁时
    /// 有时会留下一个"关不掉的幽灵窗口"——用户看到的就是"格子删不掉"。
    /// </summary>
    private static void CloseSafely(System.Windows.Window win)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (hwnd != IntPtr.Zero) Native.Win32.DesktopLayer.Detach(hwnd);
        }
        catch (Exception ex) { Log.Warn("关闭前脱离桌面层失败：" + ex.Message); }

        win.Close();
    }

    /// <summary>
    /// 文档截图专用：把桌面层窗口临时从 WorkerW 上"摘"下来并置顶。
    /// 桌面层的窗口永远在所有普通窗口之下，别人（比如编辑器）一铺满屏幕就把它盖住了；
    /// 截图时必须先摘下来，拍完再用 <see cref="RestoreLayerAfterShot"/> 装回桌面层。
    /// </summary>
    public void PrepareLayerForShot()
    {
        foreach (var win in _boxes.Values)
        {
            win.PrepareForShot();
            DetachForShot(win);
        }
        foreach (var win in _widgets.Values) DetachForShot(win);
        if (_pet is not null) DetachForShot(_pet);
    }

    /// <summary>把截图期间临时摘下来的窗口装回桌面层。</summary>
    public void RestoreLayerAfterShot()
    {
        foreach (var win in _boxes.Values)
        {
            AttachAfterShot(win);
            win.ApplyLook();
        }
        foreach (var win in _widgets.Values) AttachAfterShot(win);
        if (_pet is not null) AttachAfterShot(_pet);
    }

    private static void DetachForShot(System.Windows.Window win)
    {
        try
        {
            if (!win.IsVisible) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            Native.Win32.DesktopLayer.Detach(hwnd);
            win.Topmost = true;
            win.Show();
            win.Activate();
        }
        catch (Exception ex) { Log.Warn("截图前置顶失败：" + ex.Message); }
    }

    private static void AttachAfterShot(System.Windows.Window win)
    {
        try
        {
            win.Topmost = false;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            Native.Win32.DesktopLayer.Attach(hwnd);
        }
        catch (Exception ex) { Log.Warn("截图后装回桌面层失败：" + ex.Message); }
    }

    /// <summary>
    /// 把"跑到所有显示器之外"的盒子拉回主屏。换显示器、拔掉副屏、改分辨率之后，
    /// 盒子可能停在看不见的坐标上——用户会觉得"我的盒子丢了"。
    /// </summary>
    /// <returns>被拉回来的窗口数量。</returns>
    public int RescueOffscreen()
    {
        var monitors = Native.Win32.Monitors.All();
        var primary = Native.Win32.Monitors.Primary();
        if (primary is null || monitors.Count == 0) return 0;

        var rescued = 0;
        int x = primary.WorkX + 24, y = primary.WorkY + 24;

        foreach (var win in _boxes.Values.Concat<System.Windows.Window>(_widgets.Values))
        {
            // 注意单位：WPF 的 Left/Top 是 DIP，显示器工作区是物理像素。
            // 直接拿 Left/Top 去比，125% 缩放下会算出一个"看着在屏幕内、其实在外面"的位置，
            // 于是跑出屏幕的盒子永远拉不回来。这里统一用窗口的物理矩形来判断。
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            var bounds = Native.Win32.WindowMove.PhysicalBounds(hwnd);
            if (bounds is not { } rect) continue;

            if (monitors.Any(m => m.IntersectsWorkArea(rect.X, rect.Y, Math.Max(80, rect.Width), 32))) continue;

            var target = ClampIntoWorkArea(primary, x, y, rect.Width, rect.Height);
            if (!Native.Win32.WindowMove.MoveToPhysical(hwnd, target.X, target.Y)) continue;
            Log.Info($"把跑出屏幕的窗口拉回主屏：{bounds.Value.X},{bounds.Value.Y} → {target.X},{target.Y}");
            y += 40;
            rescued++;
        }
        if (rescued > 0)
        {
            // 物理位置变了，WPF 那边也要跟上（否则下次落盘会把旧位置写回去）
            foreach (var win in _boxes.Values) win.SyncPositionFromWindow();
            Log.Info($"已把 {rescued} 个跑出屏幕的窗口拉回主屏");
        }
        return rescued;
    }

    /// <summary>把窗口的左上角夹进工作区，保证标题栏一定看得见。</summary>
    private static (int X, int Y) ClampIntoWorkArea(Native.Win32.MonitorInfo area, int x, int y, int width, int height)
    {
        var maxX = Math.Max(area.WorkX, area.WorkRight - Math.Min(width, area.WorkWidth));
        var maxY = Math.Max(area.WorkY, area.WorkBottom - Math.Min(height, area.WorkHeight));
        return (Math.Clamp(x, area.WorkX, maxX), Math.Clamp(y, area.WorkY, maxY));
    }

    /// <summary>
    /// 把桌面上的收纳盒（可选连同组件）重新排布到目标显示器：竖向排列，放不下就换一列。
    /// </summary>
    /// <param name="includeWidgets">false 时只挪收纳盒（截图模式不想打乱用户的组件布局）。</param>
    public int ArrangeOn(Native.Win32.MonitorInfo target, bool includeWidgets = true)
    {
        const double gap = 16;
        const double margin = 24;
        var x = (double)target.WorkX + margin;
        var y = (double)target.WorkY + margin;
        var bottom = (double)target.WorkBottom - margin;
        var right = (double)target.WorkRight - margin;
        var columnWidth = 0.0;
        var moved = 0;

        foreach (var win in _boxes.Values)
        {
            if (!win.IsVisible) continue;
            var w = Math.Max(160, win.Width);
            var h = Math.Max(96, win.Height);
            if (y + h > bottom && y > target.WorkY + margin)
            {
                y = target.WorkY + margin;
                x += columnWidth + gap;
                columnWidth = 0;
            }
            if (x + w > right) x = Math.Max(target.WorkX + margin, right - w);   // 列放满了就贴着右边放
            win.MoveTo(x, y);
            columnWidth = Math.Max(columnWidth, w);
            y += h + gap;
            moved++;
        }

        if (!includeWidgets) return moved;

        foreach (var win in _widgets.Values)
        {
            if (!win.IsVisible) continue;
            var w = Math.Max(160, win.Width);
            var h = Math.Max(96, win.Height);
            if (y + h > bottom && y > target.WorkY + margin)
            {
                y = target.WorkY + margin;
                x += columnWidth + gap;
                columnWidth = 0;
            }
            if (x + w > right) x = Math.Max(target.WorkX + margin, right - w);
            win.Left = x;
            win.Top = y;
            columnWidth = Math.Max(columnWidth, w);
            y += h + gap;
            moved++;
        }

        Log.Info($"已把 {moved} 个桌面窗口移到显示器 {target.DeviceName}");
        return moved;
    }

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
        foreach (var w in _boxes.Values) { try { CloseSafely(w); } catch { } }
        foreach (var w in _widgets.Values) { try { CloseSafely(w); } catch { } }
        try { if (_pet is not null) CloseSafely(_pet); } catch { }
        _boxes.Clear();
        _widgets.Clear();
    }
}
