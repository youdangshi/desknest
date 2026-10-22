using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// 文件图标加载：ShellIcons 取 BGRA 像素 → WriteableBitmap。
/// WinUI 没有 WPF 的 Imaging.CreateBitmapSourceFromHIcon，所以走"像素直填"这条路。
/// 带缓存（按 路径+尺寸），否则每次重列盒子都要重新问一遍 Shell，滚动会卡。
/// </summary>
internal static class IconLoader
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCache = 600;

    public static ImageSource? Get(string path, int size = 40)
    {
        var key = size + "|" + path;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var pixels = ShellIcons.ExtractPixels(path, size);
            if (pixels is null) return null;
            var (w, h, bgra) = pixels.Value;

            var bitmap = new WriteableBitmap(w, h);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(bgra, 0, bgra.Length);
            }
            bitmap.Invalidate();

            if (Cache.Count > MaxCache) Cache.Clear();   // 简单粗暴，够用
            Cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            App.Log($"加载图标失败 {Path.GetFileName(path)}：{ex.Message}");
            return null;
        }
    }

    /// <summary>没有图标时的兜底符号（文件夹/文件用不同字形）。</summary>
    public static string GlyphFor(string extOrDir)
    {
        if (extOrDir.StartsWith(".dir")) return "\uE8B7";   // 文件夹
        return "\uE8A5";                                   // 文件
    }
}
