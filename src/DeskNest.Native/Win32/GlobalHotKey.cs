using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>全局热键注册。宿主窗口负责接收 WM_HOTKEY（0x0312）消息。</summary>
public static class GlobalHotKey
{
    public const int WmHotKey = 0x0312;

    [Flags]
    public enum Mods : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000
    }

    public static bool Register(IntPtr hwnd, int id, Mods mods, uint virtualKey)
    {
        try
        {
            var ok = NativeMethods.RegisterHotKey(hwnd, id, (uint)(mods | Mods.NoRepeat), virtualKey);
            if (!ok) Log.Warn($"热键注册失败：id={id} mods={mods} vk={virtualKey}（可能被其他程序占用）");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn("热键注册异常：" + ex.Message);
            return false;
        }
    }

    public static void Unregister(IntPtr hwnd, int id)
    {
        try { NativeMethods.UnregisterHotKey(hwnd, id); } catch { }
    }
}
