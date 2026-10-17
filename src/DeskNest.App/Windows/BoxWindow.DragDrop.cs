using System.Windows;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Windows;

public sealed partial class BoxWindow
{
    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDragFeedback(e);
    private void OnDragOver(object sender, DragEventArgs e) => UpdateDragFeedback(e);

    private void UpdateDragFeedback(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            _root.BorderBrush = Theme.Current.AccentBrush;
            _root.BorderThickness = new Thickness(2);
        }
        // 此电脑 / 回收站 / 网络 这些是 Shell 对象，没有文件路径，拖过来只有文本（名字）。
        // 以前这里一律 None，系统就显示"禁止"光标、Drop 根本不触发 —— 于是"此电脑拖不进盒子"。
        else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Effects = DragDropEffects.Copy;
            _root.BorderBrush = Theme.Current.AccentBrush;
            _root.BorderThickness = new Thickness(2);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _root.BorderBrush = Theme.Current.BorderBrush;
        _root.BorderThickness = new Thickness(1);

        // 普通文件/文件夹：收进盒子（真移动）
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            AddPaths(paths);
            e.Handled = true;
            return;
        }

        // 回收站 / 此电脑 / 网络 / 控制面板这些**不是文件**：拖出来给的是 Shell 对象、没有文件路径，
        // 所以以前是"拖进去没有任何反应"。这类改为给它建一个快捷方式放进盒子。
        if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
        {
            var name = ((e.Data.GetData(DataFormats.UnicodeText) as string)
                        ?? (e.Data.GetData(DataFormats.Text) as string) ?? "").Trim();
            if (VirtualShellTargets.TryGetValue(name, out var shellTarget))
            {
                AddShellShortcut(name, shellTarget);
                e.Handled = true;
                return;
            }
            if (name.Length > 0)
            {
                Ui.Notifications.Toast($"「{name}」不是文件，收不进盒子；想收纳它：右键它 → 创建快捷方式，再把快捷方式拖进来", 8);
                e.Handled = true;
            }
        }
    }

    /// <summary>系统虚拟图标 → 对应的 shell 位置（这些没有文件路径，只能收"快捷方式"）。</summary>
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

    /// <summary>
    /// 把虚拟图标做成快捷方式放进盒子：.lnk 指向 explorer.exe + shell:xxx，双击即打开对应系统位置。
    /// 用 COM（WScript.Shell）生成，不自己拼 .lnk 二进制。
    /// </summary>
    private void AddShellShortcut(string displayName, string shellTarget)
    {
        try
        {
            var root = StorageRoot();
            Directory.CreateDirectory(root);
            var lnkPath = FileOps.UniquePath(root, displayName + ".lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                Ui.Notifications.Toast("这台机器上没法创建快捷方式（WScript.Shell 不可用）", 5);
                return;
            }
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(lnkPath);
            link.TargetPath = Path.Combine(Environment.SystemDirectory, "explorer.exe");
            link.Arguments = shellTarget;
            link.Description = displayName + "（由桌巢收纳）";
            link.Save();

            AppServices.Repo.Upsert(new ItemRecord
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
            Ui.Notifications.Toast($"已把「{displayName}」的快捷方式放进盒子（双击即可打开）", 4);
        }
        catch (Exception ex)
        {
            Log.Warn($"创建系统图标快捷方式失败：{ex.Message}");
            Ui.Notifications.Toast("收进盒子失败：" + ex.Message);
        }
    }

    private void AddFilesByDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "选择要放进盒子的文件" };
        if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        var failed = 0;
        foreach (var path in paths)
        {
            try
            {
                if (_box.Kind == BoxKind.Mapped)
                {
                    AppServices.Repo.Upsert(new ItemRecord
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

                var res = AppServices.FileOps.MoveIntoStorage(path, StorageRoot());
                if (!res.Ok)
                {
                    failed++;
                    Log.Warn($"加入盒子失败 {path}：{res.Error}");
                    continue;
                }
                AppServices.Repo.Upsert(new ItemRecord
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
                Log.Warn($"加入盒子异常 {path}：{ex.Message}");
            }
        }
        ReloadItems();
        if (added > 0 || failed > 0)
            Ui.Notifications.Toast($"已放入 {added} 个文件" + (failed > 0 ? $"，{failed} 个失败（见日志）" : ""), 4);
    }

    public string StorageRoot()
    {
        if (_box.Kind == BoxKind.Mapped) return _box.MappedPath ?? AppPaths.DefaultManagedRoot;
        return _box.StorageRoot ?? Path.Combine(AppPaths.DefaultManagedRoot, SanitizeName(_box.Name));
    }

    public static string StorageRootOf(BoxRecord box)
    {
        if (box.Kind == BoxKind.Mapped) return box.MappedPath ?? AppPaths.DefaultManagedRoot;
        return box.StorageRoot ?? Path.Combine(AppPaths.DefaultManagedRoot, SanitizeName(box.Name));
    }

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "未命名" : cleaned;
    }
}
