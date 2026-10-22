using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DeskNest.Core;
using DeskNest.Core.Services;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// 桌面盒窗口。
///  P2：窗口骨架 + 桌面层挂载（AttachOwner）+ 手动增量拖动/吸附（全程物理像素）+ 卷起。
///  P3：拖文件进盒子（StorageItems 真移动；文本拖入走虚拟目标表建 .lnk）、右键菜单
///      （存储位置/添加文件/重命名/卷起/隐藏/删除）、双击列表项打开文件、状态条提示。
/// 与 WPF 版共用 Core（Repository/FileOps/BoxService/WindowSnap），数据互通。
/// </summary>
public sealed partial class BoxWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>此电脑 / 回收站 这类虚拟图标拖出来只有文本没有文件路径，映射到 shell 位置建快捷方式。</summary>
    private static readonly Dictionary<string, string> VirtualShellTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["回收站"] = "shell:RecycleBinFolder",
        ["此电脑"] = "shell:MyComputerFolder",
        ["计算机"] = "shell:MyComputerFolder",
        ["网络"] = "shell:NetworkPlacesFolder",
        ["控制面板"] = "shell:ControlPanelFolder",
        ["用户文件夹"] = "shell:Profile",
        ["文档"] = "shell:Personal"
    };

    private readonly BoxRecord _box;
    private bool _shellReady;
    private double _scale = 1.0;            // 物理像素 / DIP

    public string BoxId => _box.Id;

    // 拖动状态
    private bool _dragging;
    private POINT _dragStartCursor;         // 物理像素
    private global::Windows.Graphics.PointInt32 _dragStartPos;

    // 卷起状态
    private bool _rolledUp;
    private int _expandedHeight;            // 物理像素

    private readonly DispatcherTimer _statusTimer = new();

    public BoxWindow(BoxRecord box)
    {
        _box = box;
        App.Log($"BoxWindow ctor: begin（{box.Name}#{box.Id}）");
        InitializeComponent();
        Title = box.Name;   // 进程枚举/截图工具靠它认出这个盒子
        TitleText.Text = box.Name;

        _statusTimer.Interval = TimeSpan.FromSeconds(4);
        _statusTimer.Tick += (_, _) => { StatusBar.Visibility = Visibility.Collapsed; _statusTimer.Stop(); };

        ReloadItems();
        WireDrop();
        WireMenu();
        WireItemOpen();
        WireTitleHover();

        CloseButton.Click += (_, _) => HideBox();
        RollupButton.Click += (_, _) => ToggleRollup();
        WireDrag();

        Activated += OnFirstActivated;
        Closed += (_, _) =>
        {
            App.BoxWindows.Remove(this);
            App.Log($"BoxWindow[{_box.Name}] Closed");
        };
    }

    // ---------- 数据 ----------

    /// <summary>
    /// 从数据库读盒内文件项（Core 与原 WPF 版共用，数据互通）。
    /// 显示用 BoxItemVm：图标是 Shell 真图标（缩略图级 40px），取不到才退回字体符号。
    /// </summary>
    public void ReloadItems()
    {
        try
        {
            var items = App.Repo?.Items(_box.Id, _box.Kind == BoxKind.Mapped) ?? new List<ItemRecord>();
            var vms = items.Select(ToVm).ToList();
            ItemList.ItemsSource = vms;

            CountText.Text = vms.Count.ToString();
            EmptyState.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            App.Log($"BoxWindow[{_box.Name}] items = {items.Count}");
        }
        catch (Exception ex)
        {
            App.Log($"BoxWindow[{_box.Name}] load items FAILED: {ex.Message}");
        }
    }

    /// <summary>一条文件项 → 列表用的视图模型（图标 + 名称 + 原记录）。</summary>
    private static BoxItemVm ToVm(ItemRecord item)
    {
        var path = item.StoredPath ?? item.SourcePath ?? "";
        var isDir = item.Kind == ItemKind.Folder
                    || (!string.IsNullOrEmpty(path) && !Path.HasExtension(path) && Directory.Exists(path));
        var icon = string.IsNullOrEmpty(path) ? null : IconLoader.Get(path, 40);
        return new BoxItemVm
        {
            Title = item.DisplayName,
            Record = item,
            Icon = icon,
            Glyph = isDir ? "\uE8B7" : "\uE8A5",
            GlyphVisibility = icon is null ? Visibility.Visible : Visibility.Collapsed
        };
    }

    /// <summary>GridView 的数据项。</summary>
    public sealed class BoxItemVm
    {
        public string Title { get; set; } = "";
        public ItemRecord Record { get; set; } = new();
        public Microsoft.UI.Xaml.Media.ImageSource? Icon { get; set; }
        public string Glyph { get; set; } = "\uE8A5";
        public Visibility GlyphVisibility { get; set; } = Visibility.Visible;
    }

    /// <summary>盒子存储目录（与 WPF 版 BoxWindow.StorageRootOf 同一套规则）。</summary>
    private string StorageRoot()
    {
        if (_box.Kind == BoxKind.Mapped) return _box.MappedPath ?? AppPaths.DefaultManagedRoot;
        return _box.StorageRoot ?? Path.Combine(AppPaths.DefaultManagedRoot, SanitizeName(_box.Name));
    }

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }

    // ---------- 窗口骨架（P2-1） ----------

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_shellReady) return;
        _shellReady = true;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // 1) 摘掉系统标题栏（盒子的标题栏是自绘的 TitleBarGrid）
            var style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE,
                style & ~WS_CAPTION & ~WS_SYSMENU & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // 2) 挂桌面层：owner = 桌面图标宿主（P0 探针验证过；AttachOwner 内部会核实）
            var attached = DesktopLayer.AttachOwner(hwnd);
            App.Log($"BoxWindow[{_box.Name}] shell: AttachOwner -> {attached}");

            // 3) 位置与尺寸：库里存的是 DIP（与 WPF 版一致），乘 DPI 缩放换物理像素
            _scale = GetDpiForWindow(hwnd) / 96.0;
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                (int)Math.Round(_box.MarginX * _scale), (int)Math.Round(_box.MarginY * _scale),
                (int)Math.Round(_box.Width * _scale), (int)Math.Round(_box.Height * _scale)));
            _expandedHeight = AppWindow.Size.Height;

            // 4) 库里记着"卷起"的话，开局就卷起
            if (_box.RolledUp) SetRolledUp(true, persist: false);

            // 5) 外观跟随全局（配色 + 圆角，与 WPF 版共用 theme.desktop.* 键）
            ApplyLook();

            App.Log($"BoxWindow[{_box.Name}] shell ready: scale={_scale:0.00}, pos={AppWindow.Position.X},{AppWindow.Position.Y}, size={AppWindow.Size.Width}x{AppWindow.Size.Height}");
        }
        catch (Exception ex)
        {
            App.Log($"BoxWindow[{_box.Name}] shell setup FAILED: " + ex);
        }
    }

    // ---------- 外观 ----------

    /// <summary>
    /// 应用全局外观：配色（ElementTheme）与圆角。
    /// 盒子上设了单盒圆角（CornerRadius）时以单盒为准——与 WPF 版"单盒可覆盖全局"一致。
    /// </summary>
    public void ApplyLook()
    {
        try
        {
            Root.RequestedTheme = App.CurrentElementTheme;
            // 单盒"单独样式"才用自己的圆角；跟随全局（默认）时用设置里的圆角，
            // 否则全局调圆角对盒子永远无效（盒子库里默认就存着 12）。
            var corner = _box.StyleScope == StyleScope.Override && _box.CornerRadius > 0
                ? _box.CornerRadius
                : App.CurrentCorner;
            Root.CornerRadius = new CornerRadius(corner);
            App.Log($"BoxWindow[{_box.Name}] look: theme={App.CurrentElementTheme}, corner={corner}");
        }
        catch (Exception ex) { App.Log("应用外观失败：" + ex.Message); }
    }

    // ---------- 拖动（手动增量 + 吸附，P2-2） ----------

    private void WireDrag()
    {
        TitleBarGrid.PointerPressed += OnDragStart;
        TitleBarGrid.PointerMoved += OnDragMove;
        TitleBarGrid.PointerReleased += OnDragEnd;
        TitleBarGrid.PointerCaptureLost += (_, _) => _dragging = false;
    }

    private void OnDragStart(object sender, PointerRoutedEventArgs e)
    {
        if (_box.LockPosition) return;
        var point = e.GetCurrentPoint(TitleBarGrid);
        if (!point.Properties.IsLeftButtonPressed) return;
        // 点在标题栏按钮上不拖（按钮要吃这次点击）
        if (e.OriginalSource is DependencyObject d && IsInside<Button>(d)) return;

        GetCursorPos(out _dragStartCursor);
        _dragStartPos = AppWindow.Position;
        _dragging = true;
        TitleBarGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnDragMove(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        GetCursorPos(out var now);
        var nextX = _dragStartPos.X + now.X - _dragStartCursor.X;
        var nextY = _dragStartPos.Y + now.Y - _dragStartCursor.Y;

        // 吸附：其它盒子 + 主屏工作区（都是物理像素，与 GetCursorPos 一致）
        var snap = WindowSnap.Snap(
            new WindowSnap.Rect(nextX, nextY, AppWindow.Size.Width, AppWindow.Size.Height),
            SnapTargets());
        AppWindow.Move(new global::Windows.Graphics.PointInt32((int)snap.X, (int)snap.Y));
    }

    private void OnDragEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        TitleBarGrid.ReleasePointerCapture(e.Pointer);

        // 落点存库（换回 DIP，WPF 版读出来就是同一位置）
        _box.MarginX = Math.Round(AppWindow.Position.X / _scale, 1);
        _box.MarginY = Math.Round(AppWindow.Position.Y / _scale, 1);
        try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("保存盒子位置失败：" + ex.Message); }
        App.Log($"BoxWindow[{_box.Name}] drag end: {_box.MarginX},{_box.MarginY} DIP");
    }

    private List<WindowSnap.Rect> SnapTargets()
    {
        var list = new List<WindowSnap.Rect>();
        foreach (var other in App.BoxWindows)
        {
            if (ReferenceEquals(other, this)) continue;
            list.Add(new WindowSnap.Rect(other.AppWindow.Position.X, other.AppWindow.Position.Y,
                other.AppWindow.Size.Width, other.AppWindow.Size.Height));
        }
        if (Monitors.Primary() is { } screen)
            list.Add(new WindowSnap.Rect(screen.WorkX, screen.WorkY, screen.WorkWidth, screen.WorkHeight));
        return list;
    }

    private static bool IsInside<T>(DependencyObject node) where T : DependencyObject
    {
        for (var cur = node; cur is not null; cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur))
            if (cur is T) return true;
        return false;
    }

    // ---------- 卷起 / 展开（P2-3） ----------

    private void ToggleRollup() => SetRolledUp(!_rolledUp, persist: true);

    private void SetRolledUp(bool rolled, bool persist)
    {
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        var titleHeight = (int)Math.Round(36 * _scale);
        if (rolled)
        {
            _expandedHeight = AppWindow.Size.Height;
            // 关键坑（WPF 版踩过）：先放开最小高度，否则设高度会被夹回去、卷起不生效
            if (presenter is not null) presenter.PreferredMinimumHeight = 0;
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, titleHeight));
        }
        else
        {
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width,
                Math.Max(_expandedHeight, titleHeight * 4)));
        }
        _rolledUp = rolled;
        _box.RolledUp = rolled;
        if (RollupButton.Content is FontIcon glyph) glyph.Glyph = rolled ? "\uE70D" : "\uE70E";
        MenuRollup.Text = rolled ? "展开" : "卷起";
        if (persist)
        {
            try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("保存卷起状态失败：" + ex.Message); }
        }
        App.Log($"BoxWindow[{_box.Name}] rollup -> {rolled}（height={AppWindow.Size.Height}）");
    }

    // ---------- 拖文件进盒子（P3-1） ----------

    private void WireDrop()
    {
        Root.DragOver += OnDragOver;
        Root.DragLeave += OnDragLeave;
        Root.Drop += OnDrop;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // 普通文件或文本（虚拟系统图标）都收；其它一律拒绝
        if (e.DataView.Contains(StandardDataFormats.StorageItems)
            || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = $"收进「{_box.Name}」";
            DropFrame.Visibility = Visibility.Visible;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            DropFrame.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ResetDragVisual();

    private void ResetDragVisual() => DropFrame.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        ResetDragVisual();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (paths.Count > 0) AddPaths(paths);
                return;
            }

            // 此电脑 / 回收站 / 网络 这些是 Shell 对象，没有文件路径，拖过来只有文本（名字）
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var name = (await e.DataView.GetTextAsync())?.Trim() ?? "";
                if (VirtualShellTargets.TryGetValue(name, out var shellTarget))
                {
                    AddShellShortcut(name, shellTarget);
                }
                else if (name.Length > 0)
                {
                    Status($"「{name}」不是文件，收不进盒子；右键它 → 创建快捷方式，再把快捷方式拖进来");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"BoxWindow[{_box.Name}] drop FAILED: {ex.Message}");
            Status("收进盒子失败：" + ex.Message);
        }
    }

    /// <summary>把文件/文件夹收进盒子：普通盒真移动进存储目录，映射盒只登记路径（与 WPF 版同一套逻辑）。</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        if (App.Repo is null || App.Files is null) { Status("数据库还没准备好"); return; }
        var added = 0;
        var failed = 0;
        foreach (var path in paths)
        {
            try
            {
                if (_box.Kind == BoxKind.Mapped)
                {
                    App.Repo.Upsert(new ItemRecord
                    {
                        BoxId = _box.Id,
                        Kind = Directory.Exists(path) ? ItemKind.Folder : ItemKind.File,
                        DisplayName = Path.GetFileName(path.TrimEnd('\\')),
                        SourcePath = path,
                        StoredPath = path,
                        Ext = Path.GetExtension(path).ToLowerInvariant(),
                        SizeBytes = OrganizeEngine.SafeSize(path),
                        AddedAt = DateTimeOffset.Now
                    });
                    added++;
                    continue;
                }

                var res = App.Files.MoveIntoStorage(path, StorageRoot());
                if (!res.Ok)
                {
                    failed++;
                    App.Log($"加入盒子失败 {path}：{res.Error}");
                    continue;
                }
                App.Repo.Upsert(new ItemRecord
                {
                    BoxId = _box.Id,
                    Kind = Directory.Exists(res.StoredPath!) ? ItemKind.Folder : ItemKind.File,
                    DisplayName = Path.GetFileName(res.StoredPath!),
                    SourcePath = path,
                    StoredPath = res.StoredPath,
                    Ext = Path.GetExtension(res.StoredPath!).ToLowerInvariant(),
                    SizeBytes = OrganizeEngine.SafeSize(res.StoredPath!),
                    AddedAt = DateTimeOffset.Now
                });
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                App.Log($"加入盒子异常 {path}：{ex.Message}");
            }
        }
        ReloadItems();
        if (added > 0 || failed > 0)
            Status($"已放入 {added} 个文件" + (failed > 0 ? $"，{failed} 个失败（见日志）" : ""));
    }

    /// <summary>虚拟系统图标 → 建 .lnk 放进盒子（双击即打开对应系统位置）。</summary>
    private void AddShellShortcut(string displayName, string shellTarget)
    {
        try
        {
            var root = StorageRoot();
            Directory.CreateDirectory(root);
            var lnkPath = FileOps.UniquePath(root, displayName + ".lnk");
            CreateShellShortcutLink(lnkPath, shellTarget, displayName);

            App.Repo?.Upsert(new ItemRecord
            {
                BoxId = _box.Id,
                Kind = ItemKind.Shortcut,
                DisplayName = Path.GetFileName(lnkPath),
                SourcePath = shellTarget,
                StoredPath = lnkPath,
                Ext = ".lnk",
                SizeBytes = new FileInfo(lnkPath).Length,
                AddedAt = DateTimeOffset.Now
            });
            ReloadItems();
            Status($"已把「{displayName}」的快捷方式放进盒子（双击即可打开）");
        }
        catch (Exception ex)
        {
            App.Log($"创建系统图标快捷方式失败：{ex.Message}");
            Status("收进盒子失败：" + ex.Message);
        }
    }

    /// <summary>真正写 .lnk 的纯函数（COM WScript.Shell）。与 WPF 版实现一致（那边冻结，这边自带一份）。</summary>
    private static void CreateShellShortcutLink(string lnkPath, string shellTarget, string displayName)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("这台机器上没法创建快捷方式（WScript.Shell 不可用）");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(lnkPath);
        link.TargetPath = Path.Combine(Environment.SystemDirectory, "explorer.exe");
        link.Arguments = shellTarget;
        link.Description = displayName + "（由桌巢收纳）";
        link.Save();
    }

    // ---------- 右键菜单（P3-2） ----------

    private void WireMenu()
    {
        MenuOpenStorage.Click += (_, _) =>
        {
            try
            {
                var root = StorageRoot();
                Directory.CreateDirectory(root);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + root + "\"");
            }
            catch (Exception ex) { Status("打开失败：" + ex.Message); }
        };
        MenuAddFiles.Click += async (_, _) => await AddFilesByPicker();
        MenuRename.Click += async (_, _) => await RenameBox();
        MenuRollup.Click += (_, _) => ToggleRollup();
        MenuHide.Click += (_, _) => HideBox();
        MenuDelete.Click += async (_, _) => await DeleteBox();
    }

    /// <summary>双击列表项：打开文件（映射盒=原路径，普通盒=存储路径）。</summary>
    private void WireItemOpen()
    {
        // 桌面盒子的习惯：单击选中，双击打开（和资源管理器一致）
        ItemList.DoubleTapped += (_, e) =>
        {
            if (ItemList.SelectedItem is not BoxItemVm vm) return;
            OpenItem(vm.Record);
            e.Handled = true;
        };
        ItemList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is BoxItemVm vm) App.Log($"BoxWindow[{_box.Name}] 选中「{vm.Title}」");
        };

        // 每一项的右键菜单：打开 / 在资源管理器中显示 / 移出盒子
        ItemList.ContainerContentChanging += (_, e) =>
        {
            if (e.Item is not BoxItemVm vm) return;
            if (e.InRecycleQueue) return;
            var menu = new MenuFlyout();
            menu.Items.Add(MenuItem("打开", () => OpenItem(vm.Record)));
            menu.Items.Add(MenuItem("在资源管理器中显示", () => RevealItem(vm.Record)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("移出盒子（还原到原位置）", () => RemoveItem(vm.Record)));
            e.ItemContainer.ContextFlyout = menu;
        };
    }

    private static MenuFlyoutItem MenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenItem(ItemRecord item)
    {
        var path = item.StoredPath ?? item.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status("打开失败：" + ex.Message);
            App.Log($"打开 {path} 失败：{ex.Message}");
        }
    }

    private void RevealItem(ItemRecord item)
    {
        var path = item.StoredPath ?? item.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { Status("打开失败：" + ex.Message); }
    }

    /// <summary>移出盒子：文件还原回原位置（原位置没了就回桌面），再删掉这条记录。</summary>
    private void RemoveItem(ItemRecord item)
    {
        try
        {
            if (App.Repo is null || App.Files is null) return;
            var stored = item.StoredPath ?? item.SourcePath ?? "";
            var result = App.Files.RestoreToOriginal(stored, item.SourcePath);
            if (!result.Ok)
            {
                Status("移出失败：" + result.Error);
                App.Log($"移出失败 {stored}：{result.Error}");
                return;
            }
            App.Repo.DeleteItem(item.Id);
            ReloadItems();
            App.NotifyBoxesChanged();
            Status($"已移出「{item.DisplayName}」");
            App.Log($"移出盒子：{item.DisplayName} -> {result.StoredPath ?? item.SourcePath}");
        }
        catch (Exception ex)
        {
            Status("移出失败：" + ex.Message);
            App.Log("移出失败：" + ex);
        }
    }

    /// <summary>鼠标移到标题栏才显示 卷起/隐藏 两个按钮（平时让界面安静）。</summary>
    private void WireTitleHover()
    {
        TitleBarGrid.PointerEntered += (_, _) => TitleActions.Opacity = 1;
        TitleBarGrid.PointerExited += (_, _) =>
        {
            if (!_dragging) TitleActions.Opacity = 0;
        };
        // 双击标题栏 = 卷起/展开（常见桌面窗口习惯）
        TitleBarGrid.DoubleTapped += (_, e) =>
        {
            ToggleRollup();
            e.Handled = true;
        };
    }

    private async Task AddFilesByPicker()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add("*");
            // WinUI 3 的 picker 必须挂到一个 HWND 上
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var picked = await picker.PickMultipleFilesAsync();
            if (picked is not null && picked.Count > 0)
                AddPaths(picked.Select(f => f.Path));
        }
        catch (Exception ex)
        {
            Status("选择文件失败：" + ex.Message);
        }
    }

    private async Task RenameBox()
    {
        var input = new TextBox { Text = _box.Name, PlaceholderText = "盒子名称" };
        var dialog = new ContentDialog
        {
            Title = "重命名收纳盒",
            Content = input,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = Root.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = input.Text.Trim();
        if (name.Length == 0 || name == _box.Name) return;
        _box.Name = name;
        Title = name;
        TitleText.Text = name;
        try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("重命名保存失败：" + ex.Message); }
        App.NotifyBoxesChanged();
        Status($"已改名为「{name}」");
    }

    private void HideBox()
    {
        _box.IsHidden = true;
        try { App.Repo?.Upsert(_box); } catch (Exception ex) { App.Log("隐藏保存失败：" + ex.Message); }
        App.Log($"BoxWindow[{_box.Name}] 隐藏");
        App.NotifyBoxesChanged();
        Close();
    }

    private async Task DeleteBox()
    {
        if (App.Boxes is null) return;
        var dialog = new ContentDialog
        {
            Title = "删除收纳盒",
            Content = $"删除盒子「{_box.Name}」并把盒内文件还原到原位置？（原位置不存在则还原到桌面）",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var outcome = App.Boxes.Delete(_box);
            if (outcome.StillInDatabase)
            {
                Status("删除没生效：记录仍在库里，请把日志发给开发者");
                return;
            }
            var extra = outcome.Failed > 0 ? $"，{outcome.Failed} 个文件还原失败（见日志）" : "";
            App.Log($"BoxWindow[{_box.Name}] 已删除，还原 {outcome.Restored} 个文件{extra}");
            App.NotifyBoxesChanged();
            Close();
        }
        catch (Exception ex)
        {
            App.Log($"删除盒子失败：{ex}");
            Status("删除失败：" + ex.Message);
        }
    }

    /// <summary>状态条提示，几秒后自动消隐。</summary>
    private void Status(string message)
    {
        StatusText.Text = message;
        StatusBar.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
