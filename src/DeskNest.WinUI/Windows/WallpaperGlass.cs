using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// **模拟磨砂**（Win10 也能用）。思路：直接拿桌面壁纸，按窗口在屏幕上的位置裁一块、
/// 高斯模糊、再叠一层主题色，铺成窗口底色。
///
/// 为什么这么做：Win10 上 WinUI 3 没有窗口级透明/磨砂（见 docs/28 §11.8 的实测表），
/// 但盒子/组件本来就贴在桌面层、背后就是壁纸——所以"把背后的壁纸模糊后画成自己的底色"
/// 在视觉上就是毛玻璃，而且完全可控、不吃系统合成，跨版本一致。
///
/// 代价与边界：
///  - 只模糊**壁纸**，不包含壁纸上面压着的其它窗口（桌面层窗口本来就在其它窗口之下，
///    所以这个近似在绝大多数时候是对的）；
///  - 壁纸换了/换显示器/拖动窗口后需要重算（拖动结束会重算，见 ApplyLook 调用点）；
///  - 按"填充(Fill)"方式贴壁纸来算裁剪，这是 Win10 默认；居中/平铺的用户会有偏差（模糊后不明显）。
/// </summary>
internal static class WallpaperGlass
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

    // 壁纸像素缓存（整张壁纸只解码一次）
    private static string? _path;
    private static DateTime _stamp;
    private static byte[]? _pixels;
    private static int _w, _h;

    public static string? WallpaperPath()
    {
        try
        {
            var sb = new StringBuilder(512);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) && sb.Length > 0)
            {
                var p = sb.ToString();
                if (File.Exists(p)) return p;
            }
        }
        catch { }

        // 幻灯片/主题壁纸走转码文件
        try
        {
            var t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            if (File.Exists(t)) return t;
        }
        catch { }
        return null;
    }

    private static async Task<bool> EnsureWallpaperAsync()
    {
        var path = WallpaperPath();
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_pixels is not null && _path == path && _stamp == stamp) return true;

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);

            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(bytes.AsBuffer());
            bitmap.Dispose();

            _pixels = bytes;
            _w = (int)decoder.PixelWidth;
            _h = (int)decoder.PixelHeight;
            _path = path;
            _stamp = stamp;
            App.Log($"壁纸已载入：{Path.GetFileName(path)} {_w}x{_h}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log("载入壁纸失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 生成"窗口位置处的磨砂壁纸"。screenX/Y/outW/outH 都是物理像素。
    /// tintAlpha：主题色叠加浓度（0–1），越大越像实心色块。
    /// </summary>
    public static async Task<ImageSource?> BuildAsync(int screenX, int screenY, int outW, int outH,
        global::Windows.UI.Color tint, double tintAlpha)
    {
        if (outW <= 0 || outH <= 0) return null;
        if (!await EnsureWallpaperAsync()) return null;
        try
        {
            var outBytes = new byte[outW * outH * 4];
            var monitor = Monitors.All().FirstOrDefault(m => m.IntersectsWorkArea(screenX, screenY, outW, outH))
                          ?? Monitors.Primary();
            if (monitor is null) return null;

            // 壁纸按"填充"铺满显示器：算缩放与偏移
            var scale = Math.Max(monitor.Width / (double)_w, monitor.Height / (double)_h);
            var drawnW = _w * scale;
            var drawnH = _h * scale;
            var offsetX = (monitor.Width - drawnW) / 2.0;
            var offsetY = (monitor.Height - drawnH) / 2.0;

            var relX = (screenX - monitor.X - offsetX) / scale;
            var relY = (screenY - monitor.Y - offsetY) / scale;
            var stepX = 1.0 / scale;   // 屏幕 1px 对应壁纸多少 px
            var stepY = 1.0 / scale;

            var src = _pixels!;
            for (var y = 0; y < outH; y++)
            {
                var sy = (int)Math.Clamp(relY + y * stepY, 0, _h - 1);
                var srcRow = sy * _w * 4;
                var dstRow = y * outW * 4;
                for (var x = 0; x < outW; x++)
                {
                    var sx = (int)Math.Clamp(relX + x * stepX, 0, _w - 1);
                    var si = srcRow + sx * 4;
                    var di = dstRow + x * 4;
                    outBytes[di] = src[si];
                    outBytes[di + 1] = src[si + 1];
                    outBytes[di + 2] = src[si + 2];
                    outBytes[di + 3] = 255;
                }
            }

            BoxBlur(outBytes, outW, outH, Math.Max(6, outW / 24), passes: 2);
            Tint(outBytes, tint, tintAlpha);

            var wb = new WriteableBitmap(outW, outH);
            using (var s = wb.PixelBuffer.AsStream()) s.Write(outBytes, 0, outBytes.Length);
            wb.Invalidate();
            return wb;
        }
        catch (Exception ex)
        {
            App.Log("生成磨砂底色失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>分离式盒式模糊（两遍近似高斯）。对纯色/渐变背景足够，速度比真高斯快一个数量级。</summary>
    private static void BoxBlur(byte[] px, int w, int h, int radius, int passes)
    {
        if (radius < 1) return;
        var tmp = new byte[px.Length];
        for (var pass = 0; pass < passes; pass++)
        {
            // 横向
            for (var y = 0; y < h; y++)
            {
                var row = y * w * 4;
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0;
                    var count = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var x = Math.Clamp(k, 0, w - 1);
                        sum += px[row + x * 4 + c];
                        count++;
                    }
                    for (var x = 0; x < w; x++)
                    {
                        tmp[row + x * 4 + c] = (byte)(sum / count);
                        var add = Math.Clamp(x + radius + 1, 0, w - 1);
                        var sub = Math.Clamp(x - radius, 0, w - 1);
                        sum += px[row + add * 4 + c] - px[row + sub * 4 + c];
                    }
                }
            }
            // 纵向
            for (var x = 0; x < w; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var sum = 0;
                    var count = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var yy = Math.Clamp(k, 0, h - 1);
                        sum += tmp[(yy * w + x) * 4 + c];
                        count++;
                    }
                    for (var y = 0; y < h; y++)
                    {
                        px[(y * w + x) * 4 + c] = (byte)(sum / count);
                        var add = Math.Clamp(y + radius + 1, 0, h - 1);
                        var sub = Math.Clamp(y - radius, 0, h - 1);
                        sum += tmp[(add * w + x) * 4 + c] - tmp[(sub * w + x) * 4 + c];
                    }
                }
            }
        }
    }

    private static void Tint(byte[] px, global::Windows.UI.Color tint, double alpha)
    {
        var a = Math.Clamp(alpha, 0, 1);
        if (a <= 0) return;
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte)(px[i] * (1 - a) + tint.B * a);
            px[i + 1] = (byte)(px[i + 1] * (1 - a) + tint.G * a);
            px[i + 2] = (byte)(px[i + 2] * (1 - a) + tint.R * a);
        }
    }
}
