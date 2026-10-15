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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        AddPaths(paths);
        e.Handled = true;
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
