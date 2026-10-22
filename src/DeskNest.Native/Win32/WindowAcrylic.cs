using System;
using System.Runtime.InteropServices;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// Win10 上给窗口加"磨砂/模糊"底色（业界常用做法：user32 的 SetWindowCompositionAttribute）。
///
/// 为什么不用 WinAppSDK 的 MicaBackdrop / DesktopAcrylicBackdrop：
///  - Mica 是 Windows 11 才有的；本机 Win10 22H2 调它**不报错但什么也不发生**；
///  - DesktopAcrylicBackdrop 在 Win10 上对 owner 挂到桌面的窗口也不生效（实测底色纯白）；
///  - `WS_EX_LAYERED + SetLayeredWindowAttributes` 这种整窗 alpha 在 WinUI 3 下必然失败：
///    窗口带 WS_EX_NOREDIRECTIONBITMAP，没有重定向位图可做 alpha（日志里 ok=False 就是这么来的）。
/// 所以 Win10 走这条：DWM 在窗口背后做模糊，窗口自身的半透明底色露出模糊结果。
/// </summary>
public static class WindowAcrylic
{
    private const int WCA_ACCENT_POLICY = 19;

    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;   // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// 开启模糊。tint 是叠加色（不含 alpha），opacity 是 tint 的浓度 0–1。
    /// kind: "Acrylic"（亚克力）/ "Mica"（退化为普通模糊）/ "Solid"（关闭）。
    /// </summary>
    public static bool Apply(IntPtr hwnd, string kind, byte tintR, byte tintG, byte tintB, double opacity)
    {
        try
        {
            var state = kind switch
            {
                "Acrylic" => ACCENT_ENABLE_ACRYLICBLURBEHIND,
                "Mica" => ACCENT_ENABLE_BLURBEHIND,
                _ => ACCENT_DISABLED
            };

            // GradientColor 是 AABBGGRR（注意字节序和 WPF/WinUI 的 ARGB 相反）
            var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
            var gradient = (alpha << 24) | (tintB << 16) | (tintG << 8) | tintR;

            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 2,
                GradientColor = gradient,
                AnimationId = 0
            };

            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                var hr = SetWindowCompositionAttribute(hwnd, ref data);
                Log.Info($"窗口模糊：{kind} state={state} tint={alpha},{tintR},{tintG},{tintB} 返回={hr}");
                return hr != 0;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch (Exception ex)
        {
            Log.Warn("设置窗口模糊失败：" + ex.Message);
            return false;
        }
    }
}
