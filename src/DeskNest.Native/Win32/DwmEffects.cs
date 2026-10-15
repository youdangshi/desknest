using System.Runtime.InteropServices;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// 窗口材质（Mica / Acrylic）。只在 Windows 11 上有效；失败时保持应用自己画的底色，
/// 不影响功能（这也是"优雅降级"：材质只是观感，不是必需品）。
/// </summary>
public static class DwmEffects
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmsbtAuto = 0;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;      // Mica
    private const int DwmsbtTransientWindow = 3; // Acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public enum Backdrop { None, Mica, Acrylic }

    public static bool TrySetBackdrop(IntPtr hwnd, Backdrop backdrop)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22000) return false;   // 仅 Windows 11
            var value = backdrop switch
            {
                Backdrop.Mica => DwmsbtMainWindow,
                Backdrop.Acrylic => DwmsbtTransientWindow,
                _ => DwmsbtNone
            };
            var hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref value, sizeof(int));
            if (hr != 0)
            {
                Log.Info($"设置窗口材质失败（HRESULT 0x{hr:X8}），继续使用自绘底色");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("设置窗口材质异常：" + ex.Message);
            return false;
        }
    }

    /// <summary>圆角窗口（Windows 11 默认就是圆角，这里显式声明，避免被系统策略关掉）。</summary>
    public static void PreferRoundCorners(IntPtr hwnd)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22000) return;
            const int DwmwcpRound = 2;
            var value = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref value, sizeof(int));
        }
        catch { }
    }
}
