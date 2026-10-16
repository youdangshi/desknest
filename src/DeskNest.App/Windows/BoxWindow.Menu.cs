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
    private void ShowBoxMenu()
    {
        var menu = new ContextMenu();
        ApplyMenuTheme(menu);
        BuildBoxMenu(menu);
        menu.IsOpen = true;
    }

    /// <summary>右键盒子空白处也会调用它（与标题栏 ⋯ 按钮共用同一套条目）。</summary>
    public void BuildBoxContextMenu(ContextMenu menu)
    {
        ApplyMenuTheme(menu);
        BuildBoxMenu(menu);
    }

    /// <summary>
    /// 盒子菜单：按"常用动作 → 外观/行为 → 危险操作"分三段，需要多选或需要展开的东西
    /// 收进二级菜单（标题样式 / 视图与排序 / 添加格子 / 更多操作），
    /// 免得一屏二十多条平铺出来找不到东西——这也是参考 DeskBox 的菜单结构。
    /// </summary>
    private void BuildBoxMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Item("刷新", ReloadItems, "F5"));
        menu.Items.Add(Item("粘贴到盒子", PasteFromClipboard, "Ctrl+V", enabled: ClipboardHasFiles()));
        menu.Items.Add(Item("新建文件夹", NewFolderInBox, "Ctrl+Shift+N"));
        menu.Items.Add(Item("打开存储文件夹", OpenStorageFolder));
        menu.Items.Add(new Separator());

        menu.Items.Add(Sub("标题样式", BuildTitleStyleItems()));
        menu.Items.Add(Sub("视图与排序", BuildViewItems()));

        menu.Items.Add(Item("锁定位置", ToggleLockPosition, checkable: true, isChecked: _box.LockPosition));
        menu.Items.Add(Item("锁定大小", ToggleLockSize, checkable: true, isChecked: _box.LockSize));
        menu.Items.Add(Item("自动隐藏（鼠标离开变淡）", ToggleAutoHide, checkable: true, isChecked: _box.AutoHide));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("添加文件…", AddFilesByDialog, "Ctrl+O"));
        menu.Items.Add(Sub("添加格子", BuildAddBoxItems()));
        menu.Items.Add(Sub("更多操作", BuildMoreItems()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("删除盒子（盒内文件还原到原位置）", DeleteBox, danger: true));
        menu.Items.Add(Item("解散盒子（盒内文件全部回到桌面）", DissolveBox, danger: true));
    }

    /// <summary>解散：盒内文件全部丢回桌面，盒子消失。想"我不关心它原来在哪，只要回到桌面"时用这个。</summary>
    private void DissolveBox()
    {
        var confirm = MessageBox.Show(this,
            $"解散盒子「{_box.Name}」？\n\n盒内文件会全部移动到桌面（同名自动加 (1)），然后盒子消失。",
            "解散收纳盒", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            var outcome = AppServices.Boxes.DissolveToDesktop(_box);
            if (outcome.StillInDatabase)
            {
                Ui.Notifications.Toast("解散没生效：记录仍在库里，请把日志发给开发者", 8);
                return;
            }
            Ui.Notifications.Toast($"已解散「{_box.Name}」，{outcome.Restored} 个文件回到桌面", 6);
            Close();
            if (App.Current is App app) app.Desktop.NotifyBoxClosed(_box.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"解散盒子失败：{ex}");
            Ui.Notifications.Toast("解散失败：" + ex.Message, 8);
        }
    }

    /// <summary>标题样式：标准 / 紧凑 / 悬浮 / 隐藏。盒子组里"悬浮/隐藏"会禁用——组内必须一直能看到标题栏才能切成员。</summary>
    private MenuItem[] BuildTitleStyleItems()
    {
        var current = TitleStyleKey;
        var inGroup = IsInMultiBoxGroup();
        var items = new List<MenuItem>
        {
            Item("标准", () => SetTitleStyle("Standard"), checkable: true, isChecked: current == "Standard"),
            Item("紧凑", () => SetTitleStyle("Compact"), checkable: true, isChecked: current == "Compact"),
            Item("悬浮（鼠标移上去才显形）", () => SetTitleStyle("Floating"), checkable: true,
                isChecked: current == "Floating", enabled: !inGroup,
                reason: inGroup ? "格子组需要持续可见的标题栏" : null),
            Item("隐藏（拖盒子任意处移动）", () => SetTitleStyle("Hidden"), checkable: true,
                isChecked: current == "Hidden", enabled: !inGroup,
                reason: inGroup ? "格子组需要持续可见的标题栏" : null)
        };
        return items.ToArray();
    }

    /// <summary>视图与排序：图标/列表、排序字段、升降序、图标大小、文件名与扩展名。</summary>
    private object[] BuildViewItems()
    {
        var iconView = !string.Equals(_box.ViewMode, "List", StringComparison.OrdinalIgnoreCase);
        var sizes = new (string Label, int Value)[] { ("小（20）", 20), ("中（30）", 30), ("大（44）", 44), ("特大（60）", 60) };
        var iconSizeItems = sizes
            .Select(s => Item(s.Label, () => SetIconSize(s.Value), checkable: true, isChecked: Math.Abs(_box.IconSize - s.Value) < 3))
            .ToArray();

        return new object[]
        {
            Item("图标视图", () => SetViewMode("Icon"), checkable: true, isChecked: iconView),
            Item("列表视图", () => SetViewMode("List"), checkable: true, isChecked: !iconView),
            new Separator(),
            Sub("排序方式",
                Item("名称", () => SetSortMode("Name"), checkable: true, isChecked: _box.SortMode == "Name"),
                Item("加入时间", () => SetSortMode("AddedAt"), checkable: true, isChecked: _box.SortMode == "AddedAt"),
                Item("大小", () => SetSortMode("Size"), checkable: true, isChecked: _box.SortMode == "Size")),
            Item("升序", () => SetSortDescending(false), checkable: true, isChecked: !_box.SortDescending),
            Item("降序", () => SetSortDescending(true), checkable: true, isChecked: _box.SortDescending),
            new Separator(),
            Sub("图标大小", iconSizeItems),
            Item("显示文件名", ToggleFileName, checkable: true, isChecked: !_box.HideFileName),
            Item("显示扩展名", () =>
            {
                _box.ShowExtensions = !_box.ShowExtensions;
                AppServices.Repo.Upsert(_box);
                ReloadItems();
            }, checkable: true, isChecked: _box.ShowExtensions),
            new Separator(),
            // 文字大小与文件名行数以前只有数据字段、界面上没有入口（用户反馈"样式没法调"）
            Sub("文字大小",
                new (string Label, double Value)[] { ("小", 10), ("中", 11.5), ("大", 13), ("特大", 15) }
                    .Select(s => Item(s.Label, () => SetTextSize(s.Value), checkable: true,
                        isChecked: Math.Abs(_box.TextSize - s.Value) < 0.3))
                    .ToArray()),
            Sub("文件名行数",
                new (string Label, int Value)[] { ("1 行", 1), ("2 行", 2), ("3 行", 3) }
                    .Select(s => Item(s.Label, () => SetFileNameLines(s.Value), checkable: true, isChecked: _box.FileNameLines == s.Value))
                    .ToArray()),
            Item("把这套显示样式套用到所有盒", () =>
            {
                foreach (var other in AppServices.Repo.Boxes())
                {
                    other.ViewMode = _box.ViewMode;
                    other.IconSize = _box.IconSize;
                    other.TextSize = _box.TextSize;
                    other.FileNameLines = _box.FileNameLines;
                    other.HideFileName = _box.HideFileName;
                    other.ShowExtensions = _box.ShowExtensions;
                    AppServices.Repo.Upsert(other);
                }
                if (App.Current is App app) app.Desktop.ApplyLookToAll();
                Ui.Notifications.Toast("已把当前盒的显示样式套用到所有盒", 4);
            })
        };
    }

    /// <summary>添加格子：新建一个盒子，或把桌面上已有的盒子并进当前盒所在的组。</summary>
    private object[] BuildAddBoxItems()
    {
        var others = AppServices.Repo.Boxes()
            .Where(b => b.Id != _box.Id && !string.Equals(b.GroupId, _box.GroupId, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        var items = new List<object> { Item("新建收纳盒…", CreateBoxFromMenu) };
        items.Add(new Separator());
        if (others.Count == 0)
        {
            items.Add(Item("没有其他盒子可并入", null, enabled: false, reason: "桌面上只有这一个盒子"));
        }
        else
        {
            items.Add(new MenuItem { Header = "并入本组", IsEnabled = false, Foreground = Theme.Desktop.SecondaryBrush });
            foreach (var other in others)
                items.Add(Item("＋ " + other.Name, () => MergeBoxInto(other)));
        }
        return items.ToArray();
    }

    /// <summary>更多操作：样式作用域、卷起、重命名、透明度、圆角。</summary>
    private object[] BuildMoreItems()
    {
        var opacity = new (string Label, double Value)[]
        {
            ("30%", 0.30), ("50%", 0.50), ("70%", 0.70), ("85%", 0.85), ("100%", 1.00)
        };
        var corners = new (string Label, int Value)[]
        {
            ("直角（0）", 0), ("小圆角（6）", 6), ("中圆角（12）", 12), ("大圆角（18）", 18), ("胶囊（24）", 24)
        };

        return new object[]
        {
            Item(_box.StyleScope == StyleScope.Inherit ? "单独设置样式（不再跟随全局）" : "样式跟随全局", ToggleStyleScope,
                checkable: true, isChecked: _box.StyleScope == StyleScope.Override),
            Item("把当前外观应用到所有盒", ApplyLookToAllBoxes),
            Item("卷起 / 展开", () =>
            {
                _box.RolledUp = !_box.RolledUp;
                AppServices.Repo.Upsert(_box);
                ApplyLook();
            }, checkable: true, isChecked: _box.RolledUp),
            new Separator(),
            Item("重命名盒子…", RenameBox),
            new Separator(),
            Sub("双击盒子时",
                Item("隐藏盒子（DeskBox 那样）", () => SetDoubleClickAction("Hide"), checkable: true,
                    isChecked: DoubleClickAction == "Hide"),
                Item("卷起 / 展开", () => SetDoubleClickAction("RollUp"), checkable: true,
                    isChecked: DoubleClickAction == "RollUp"),
                Item("什么都不做", () => SetDoubleClickAction("Nothing"), checkable: true,
                    isChecked: DoubleClickAction == "Nothing")),
            Sub("透明度", opacity
                .Select(o => Item(o.Label, () => SetOpacity(o.Value), checkable: true, isChecked: Math.Abs(_box.Opacity - o.Value) < 0.02))
                .ToArray()),
            Sub("圆角", corners
                .Select(c => Item(c.Label, () => SetCorner(c.Value), checkable: true, isChecked: _box.CornerRadius == c.Value))
                .ToArray())
        };
    }

    // ---------- 菜单条目工厂 ----------

    /// <summary>一条可点菜单项。checkable 会在左侧显示勾；reason 会作为禁用原因写进提示。</summary>
    private static MenuItem Item(string header, Action? action, string? gesture = null,
        bool checkable = false, bool isChecked = false, bool enabled = true, string? reason = null, bool danger = false)
    {
        var mi = new MenuItem
        {
            Header = header,
            IsEnabled = action is not null && enabled,
            Foreground = danger ? Theme.Desktop.DangerBrush : Theme.Desktop.ForegroundBrush
        };
        if (gesture is not null) mi.InputGestureText = gesture;
        if (checkable)
        {
            mi.IsCheckable = true;
            mi.IsChecked = isChecked;
        }
        if (!string.IsNullOrWhiteSpace(reason)) mi.ToolTip = reason;
        if (action is not null && enabled) mi.Click += (_, _) => action();
        return mi;
    }

    /// <summary>一个带子菜单的父项（右侧自带 › 箭头）。</summary>
    private static MenuItem Sub(string header, params object[] children)
    {
        // 子菜单的弹出层**不会**继承 ContextMenu 的配色（WPF 的 Popup 是独立可视化树），
        // 所以每一项都要自己带上底色与前景色，否则会出现"浅色底 + 白色字"看不见的情况。
        var mi = new MenuItem
        {
            Header = header,
            Background = Theme.Desktop.CardBrush,
            Foreground = Theme.Desktop.ForegroundBrush
        };
        foreach (var child in children)
        {
            if (child is MenuItem item) mi.Items.Add(item);
            else if (child is IEnumerable<MenuItem> many)
                foreach (var each in many) mi.Items.Add(each);
        }
        return mi;
    }

    /// <summary>菜单配色跟随"收纳窗口"主题（盒子属于桌面层，不用设计窗口那套）。</summary>
    private static void ApplyMenuTheme(ContextMenu menu)
    {
        var t = Theme.Desktop;
        menu.Background = t.CardBrush;
        menu.Foreground = t.ForegroundBrush;
        menu.FontSize = Tokens.FontBody;
        menu.Padding = new Thickness(Tokens.Space4);
        menu.BorderBrush = t.BorderBrush;
        menu.BorderThickness = new Thickness(1);

        // WPF 的菜单高亮默认取系统色（蓝得发旧），在菜单作用域内换成我们自己的强调色
        menu.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(t.Accent) { Opacity = 0.18 };
        menu.Resources[SystemColors.HighlightTextBrushKey] = t.ForegroundBrush;
        menu.Resources[SystemColors.MenuBrushKey] = t.CardBrush;
        menu.Resources[SystemColors.MenuTextBrushKey] = t.ForegroundBrush;
        // 顶层菜单项同样显式上色（Popups 不回继承）
        menu.Opened += (_, _) => PaintItems(menu.Items, t);
        PaintItems(menu.Items, t);
    }

    /// <summary>递归给每个菜单项（含子菜单）刷上底色与前景色；危险项保留红色文字。</summary>
    private static void PaintItems(System.Collections.IEnumerable items, ThemeTokens t)
    {
        var danger = t.DangerColor;
        foreach (var entry in items)
        {
            if (entry is not System.Windows.Controls.MenuItem mi) continue;
            var isDanger = mi.Foreground is System.Windows.Media.SolidColorBrush brush && brush.Color == danger;
            mi.Background = t.CardBrush;
            if (!isDanger) mi.Foreground = t.ForegroundBrush;
            if (mi.Items.Count > 0) PaintItems(mi.Items, t);
        }
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

    private void SetViewMode(string mode)
    {
        _box.ViewMode = mode;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    private void SetSortMode(string mode)
    {
        _box.SortMode = mode;
        AppServices.Repo.Upsert(_box);
        ReloadItems();
    }

    private void SetSortDescending(bool descending)
    {
        _box.SortDescending = descending;
        AppServices.Repo.Upsert(_box);
        ReloadItems();
    }

    private void SetIconSize(int size)
    {
        _box.IconSize = size;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    /// <summary>文件名文字大小（以前只有数据字段，界面没有入口）。</summary>
    private void SetTextSize(double size)
    {
        _box.TextSize = size;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        ReloadItems();
    }

    /// <summary>文件名最多显示几行。</summary>
    private void SetFileNameLines(int lines)
    {
        _box.FileNameLines = Math.Clamp(lines, 1, 3);
        AppServices.Repo.Upsert(_box);
        ReloadItems();
    }

    private void SetOpacity(double opacity)
    {
        _box.StyleScope = StyleScope.Override;   // 单独调了就不能再被全局覆盖掉，否则用户会觉得"调了没反应"
        _box.Opacity = opacity;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
    }

    private void SetCorner(int radius)
    {
        _box.StyleScope = StyleScope.Override;
        _box.CornerRadius = radius;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
    }

    private void ToggleLockPosition()
    {
        _box.LockPosition = !_box.LockPosition;
        AppServices.Repo.Upsert(_box);
        Ui.Notifications.Toast(_box.LockPosition ? "已锁定位置" : "位置已解锁", 2);
    }

    private void ToggleLockSize()
    {
        _box.LockSize = !_box.LockSize;
        AppServices.Repo.Upsert(_box);
        Ui.Notifications.Toast(_box.LockSize ? "已锁定大小" : "大小已解锁", 2);
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
            this,
            $"确定删除盒子「{_box.Name}」吗？\n\n盒内文件会逐个还原到原位置；原位置不存在时还原到桌面。",
            "删除收纳盒", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var outcome = AppServices.Boxes.Delete(_box);
            if (outcome.StillInDatabase)
            {
                Ui.Notifications.Toast("删除没生效：记录仍然在库里，请把日志发给开发者", 8);
                return;
            }
            var extra = outcome.Failed > 0 ? $"，{outcome.Failed} 个文件还原失败（见日志）" : "";
            Ui.Notifications.Toast($"已删除「{_box.Name}」，还原 {outcome.Restored} 个文件{extra}", 5);
            Close();
            if (App.Current is App app) app.Desktop.NotifyBoxClosed(_box.Id);
        }
        catch (Exception ex)
        {
            Log.Error($"删除盒子失败：{ex}");
            Ui.Notifications.Toast("删除失败：" + ex.Message, 8);
        }
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

    // ---------- 标题样式 ----------

    /// <summary>当前标题样式（空值按"标准"处理）。</summary>
    private string TitleStyleKey => string.IsNullOrWhiteSpace(_box.TitleStyle) ? "Standard" : _box.TitleStyle;

    /// <summary>双击盒子的行为：Hide（默认，DeskBox 那样）/ RollUp / Nothing。</summary>
    private static string DoubleClickAction => AppServices.Settings.Get("box.doubleClickAction", "Hide");

    private void SetDoubleClickAction(string action)
    {
        AppServices.Settings.Set("box.doubleClickAction", action);
        var label = action switch { "RollUp" => "卷起 / 展开", "Nothing" => "什么都不做", _ => "隐藏盒子" };
        Ui.Notifications.Toast("双击盒子：" + label, 3);
    }

    /// <summary>标题栏被隐藏时，盒子的其余部分就是拖动热区（拖任意处都能移动）。</summary>
    private bool IsTitleBarHidden => TitleStyleKey == "Hidden";

    private void SetTitleStyle(string style)
    {
        _box.TitleStyle = style;
        AppServices.Repo.Upsert(_box);
        ApplyLook();
        var label = style switch
        {
            "Compact" => "紧凑",
            "Floating" => "悬浮（鼠标移上去才显形）",
            "Hidden" => "隐藏（拖动盒子任意处可移动）",
            _ => "标准"
        };
        Ui.Notifications.Toast("标题样式：" + label, style == "Hidden" ? 6 : 2);
    }

    /// <summary>是否处在"多盒一组"里：组内靠标题栏切换成员，所以不允许把标题栏藏掉。</summary>
    private bool IsInMultiBoxGroup()
    {
        if (_box.GroupId is null) return false;
        var group = AppServices.Repo.Groups().FirstOrDefault(g => g.Id == _box.GroupId);
        if (group is null) return false;
        return group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length > 1;
    }

    // ---------- 剪贴板 / 新建文件夹 ----------

    private static bool ClipboardHasFiles()
    {
        try { return Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0; }
        catch { return false; }
    }

    /// <summary>把剪贴板里复制的文件/文件夹放进盒子（和拖进来是同一条路径）。</summary>
    private void PasteFromClipboard()
    {
        try
        {
            var list = Clipboard.GetFileDropList();
            var paths = list.Cast<string?>().Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToList();
            if (paths.Count == 0)
            {
                Ui.Notifications.Toast("剪贴板里没有文件");
                return;
            }
            AddPaths(paths);
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("粘贴失败：" + ex.Message);
        }
    }

    /// <summary>在盒子的存储目录里新建一个文件夹，并立刻作为一个条目出现。</summary>
    private void NewFolderInBox()
    {
        try
        {
            var root = StorageRoot();
            Directory.CreateDirectory(root);
            var target = FileOps.UniquePath(root, "新建文件夹");
            Directory.CreateDirectory(target);
            AppServices.Repo.Upsert(new ItemRecord
            {
                BoxId = _box.Id,
                Kind = ItemKind.Folder,
                DisplayName = Path.GetFileName(target),
                SourcePath = target,
                StoredPath = target,
                Ext = "",
                AddedAt = DateTimeOffset.Now
            });
            ReloadItems();
            Ui.Notifications.Toast("已新建文件夹：" + Path.GetFileName(target), 2);
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("新建文件夹失败：" + ex.Message);
        }
    }

    // ---------- 重命名 / 新建盒子 / 合并盒子 ----------

    private void RenameBox()
    {
        var input = new TextBox
        {
            Text = _box.Name,
            Width = 280,
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 10)
        };
        var okBtn = UiKit.Btn("确定", () => { }, true);
        var cancelBtn = UiKit.Btn("取消", () => { }, false);
        var win = new Window
        {
            Title = "重命名收纳盒",
            Width = 380,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Desktop.BackgroundBrush,
            Content = UiKit.Card(null, UiKit.Col(
                UiKit.Text("只改盒子标题；盒内文件与存储目录不动。", 11.5, secondary: true),
                input,
                UiKit.Row(okBtn, cancelBtn)))
        };
        cancelBtn.Click += (_, _) => win.Close();
        okBtn.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _box.Name = name;
                AppServices.Repo.Upsert(_box);
                ApplyLook();
                ReloadItems();
            }
            win.Close();
        };
        win.ShowDialog();
    }

    private void CreateBoxFromMenu()
    {
        try
        {
            var box = new BoxRecord
            {
                Name = "新收纳盒 " + (AppServices.Repo.Boxes().Count + 1),
                Kind = BoxKind.Normal,
                MarginX = Left + 40,
                MarginY = Top + 40
            };
            box.StorageRoot = Path.Combine(AppPaths.DefaultManagedRoot, SanitizeName(box.Name));
            AppServices.Repo.Upsert(box);
            if (App.Current is App app) app.Desktop.ShowBox(box);
            Ui.Notifications.Toast($"已新建「{box.Name}」", 3);
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("新建盒子失败：" + ex.Message);
        }
    }

    /// <summary>把另一个盒子并进当前盒子所在的组：两个盒子占同一块位置，用标题栏的标签切换。</summary>
    private void MergeBoxInto(BoxRecord other)
    {
        try
        {
            // 数据部分交给可测试的 BoxService（组、本盒、被并入的盒三处写入要一致）
            var group = AppServices.Boxes.MergeInto(_box, other);
            if (App.Current is App app)
            {
                app.Desktop.ReloadBoxes();
                app.Desktop.SwitchGroupTo(group.Id, group.ActiveMemberId);   // 只留当前选中的那个盒子可见
            }
            Ui.Notifications.Toast($"已把「{other.Name}」并进「{group.Name}」，点标题栏标签或滚轮切换", 6);
        }
        catch (Exception ex)
        {
            Log.Error($"合并盒子失败：{ex}");
            Ui.Notifications.Toast("合并失败：" + ex.Message);
        }
    }
}
