using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskNest.Core;

namespace DeskNest.App.Host;

/// <summary>
/// 截图用的"壁纸底布"：一块铺满屏幕、置顶、无边框的窗口，内容就是当前桌面壁纸。
/// 桌面上本来就有用户在用的窗口，直接抓屏会把别人的界面拍进去；
/// 铺一层真实壁纸之后，收纳盒看起来就像真的摆在桌面上（用于文档与商店页截图）。
/// </summary>
internal sealed class WallpaperBackdrop : Window
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int MaxPath = 260;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int action, int param, System.Text.StringBuilder buffer, int winIni);

    public WallpaperBackdrop()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = BuildBrush();
        var area = VirtualScreen();
        Left = area.X;
        Top = area.Y;
        Width = area.Width;
        Height = area.Height;
    }

    private static Rect VirtualScreen()
    {
        try
        {
            // 用 WinForms 的虚拟屏幕矩形：多显示器下也要铺满
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        catch
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }
    }

    private static Brush BuildBrush()
    {
        try
        {
            var buffer = new System.Text.StringBuilder(MaxPath);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, MaxPath, buffer, 0))
            {
                var path = buffer.ToString();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(path);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                }
            }
        }
        catch (Exception ex) { Log.Warn("读取壁纸失败，改用纯色底：" + ex.Message); }

        return new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2C));
    }
}
