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
}
