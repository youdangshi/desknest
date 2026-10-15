using System.Runtime.InteropServices;

namespace DeskNest.Native.Win32;

/// <summary>发送媒体键（播放/暂停/上一首/下一首）给当前播放器，不需要任何权限或注入。</summary>
public static class MediaKeys
{
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    public static void Send(int virtualKey)
    {
        var vk = (byte)virtualKey;
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, IntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, IntPtr.Zero);
    }
}
