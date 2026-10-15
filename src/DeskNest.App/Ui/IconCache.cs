using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskNest.Native.Win32;

namespace DeskNest.App.Ui;

/// <summary>Shell 图标缓存：按"扩展名/目录 + 尺寸"复用，避免频繁调用系统接口。</summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static ImageSource? Get(string path, bool isDirectory, bool large = false)
    {
        var key = (large ? "L|" : "S|") + (isDirectory ? "<dir>" : Path.GetExtension(path).ToLowerInvariant());
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        ImageSource? image = null;
        var handle = ShellIcons.Extract(path, large);
        if (handle is not null)
        {
            try
            {
                image = Imaging.CreateBitmapSourceFromHIcon(handle.HIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
            catch { image = null; }
            finally { ShellIcons.Release(handle.HIcon); }
        }

        lock (Gate) { Cache[key] = image; }
        return image;
    }

    public static void Clear()
    {
        lock (Gate) Cache.Clear();
    }
}
