using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>文件/文件夹图标提取（返回 HBITMAP 句柄，由界面层转换为图像；调用方负责释放）。</summary>
public static class ShellIcons
{
    public sealed record IconHandle(IntPtr HIcon, string TypeName);

    public static IconHandle? Extract(string path, bool large = false)
    {
        try
        {
            var info = new NativeMethods.SHFILEINFO();
            var flags = NativeMethods.SHGFI_ICON | (large ? NativeMethods.SHGFI_LARGEICON : NativeMethods.SHGFI_SMALLICON);
            var exists = File.Exists(path) || Directory.Exists(path);
            if (!exists)
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                var isExtOnly = Path.GetFileName(path).StartsWith('.');
                var attr = isExtOnly ? NativeMethods.FILE_ATTRIBUTE_NORMAL : NativeMethods.FILE_ATTRIBUTE_NORMAL;
                var dummy = isExtOnly ? "file" + path : path;
                var r0 = NativeMethods.SHGetFileInfo(dummy, attr, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
                if (r0 == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                return new IconHandle(info.hIcon, info.szTypeName ?? "");
            }
            var result = NativeMethods.SHGetFileInfo(path, 0, ref info,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            return new IconHandle(info.hIcon, info.szTypeName ?? "");
        }
        catch (Exception ex)
        {
            Log.Warn($"提取图标失败 {path}：{ex.Message}");
            return null;
        }
    }

    public static void Release(IntPtr hIcon)
    {
        try
        {
            if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
        }
        catch { }
    }

    /// <summary>
    /// 取文件图标并直接渲染成 BGRA 像素（给 WinUI 用：那边没有 HICON → ImageSource 的桥）。
    /// 做法：建一个 32bpp 自顶向下的 DIB，把 HICON 用 DrawIconEx 画进去（可任意尺寸，缩放质量够用），
    /// 再整块读出来。size 建议 40/48/64。
    /// </summary>
    public static (int width, int height, byte[] bgra)? ExtractPixels(string path, int size = 48)
    {
        if (size <= 0) size = 48;
        var handle = Extract(path, large: false);
        if (handle is null) return null;
        var hIcon = handle.HIcon;
        IntPtr hdc = IntPtr.Zero, memDc = IntPtr.Zero, dib = IntPtr.Zero, oldObj = IntPtr.Zero;
        try
        {
            hdc = NativeMethods.GetDC(IntPtr.Zero);
            memDc = NativeMethods.CreateCompatibleDC(hdc);

            var bi = new NativeMethods.BITMAPINFO
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
                biWidth = size,
                biHeight = -size,          // 负数 = 自顶向下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            dib = NativeMethods.CreateDIBSection(memDc, ref bi, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            oldObj = NativeMethods.SelectObject(memDc, dib);

            // 先铺透明底，再画图标（DrawIconEx 不会清背景）
            NativeMethods.PatBlt(memDc, 0, 0, size, size, NativeMethods.BLACKNESS);
            var ok = NativeMethods.DrawIconEx(memDc, 0, 0, hIcon, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
            if (!ok) return null;

            var buffer = new byte[size * size * 4];
            System.Runtime.InteropServices.Marshal.Copy(bits, buffer, 0, buffer.Length);

            // 有的旧图标 alpha 全 0（靠 mask 抠图）：这时整块设为不透明，至少不会全透明
            var anyAlpha = false;
            for (var i = 3; i < buffer.Length; i += 4) { if (buffer[i] != 0) { anyAlpha = true; break; } }
            if (!anyAlpha) for (var i = 3; i < buffer.Length; i += 4) buffer[i] = 255;

            return (size, size, buffer);
        }
        catch (Exception ex)
        {
            Log.Warn($"提取图标像素失败 {path}：{ex.Message}");
            return null;
        }
        finally
        {
            if (oldObj != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldObj);
            if (dib != IntPtr.Zero) NativeMethods.DeleteObject(dib);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (hdc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            Release(hIcon);
        }
    }
}
