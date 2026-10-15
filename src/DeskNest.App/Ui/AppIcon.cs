using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskNest.Core;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;

namespace DeskNest.App.Ui;

/// <summary>
/// 应用图标：窗口用 WPF ImageSource，托盘用 System.Drawing.Icon。
/// 文件放在 Assets\desknest.ico（由 _re/make_icon 脚本用矢量绘制生成，含 16–256 七个尺寸）。
/// 找不到文件时回退到 exe 自带图标，再不行用系统默认图标，保证不会崩。
/// </summary>
public static class AppIcon
{
    private static ImageSource? _windowIcon;
    private static DrawingIcon? _trayIcon;
    private static bool _loaded;

    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "desknest.ico");

    public static ImageSource WindowIcon
    {
        get
        {
            EnsureLoaded();
            if (_windowIcon is not null) return _windowIcon;
            try
            {
                _windowIcon = Imaging.CreateBitmapSourceFromHIcon(
                    DrawingSystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch
            {
                _windowIcon = null!;
            }
            return _windowIcon!;
        }
    }

    /// <summary>托盘图标（用完由调用方 Dispose，或者跟进程一起结束）。</summary>
    public static DrawingIcon TrayIcon
    {
        get
        {
            EnsureLoaded();
            return _trayIcon ?? DrawingSystemIcons.Application;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(IconPath))
            {
                Log.Warn("找不到应用图标文件，使用系统默认图标：" + IconPath);
                return;
            }

            // 窗口图标：直接解 32px 那帧，缩放由系统处理
            var decoder = BitmapDecoder.Create(new Uri(IconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            _windowIcon = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            if (_windowIcon is not null) _windowIcon.Freeze();

            // 托盘图标：用 16px 缩放版本，视觉上更清晰
            using var stream = File.OpenRead(IconPath);
            _trayIcon = new DrawingIcon(stream, new System.Drawing.Size(16, 16));
        }
        catch (Exception ex)
        {
            Log.Warn("加载应用图标失败：" + ex.Message);
        }
    }
}
