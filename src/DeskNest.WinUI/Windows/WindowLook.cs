using System;
using DeskNest.Native.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskNest.WinUI.Windows;

/// <summary>
/// 桌面窗口的外观：材质（亚克力/云母/纯色）+ 不透明度。
///
/// 实现路线（Win10 22H2 实测后的结论，别再走弯路）：
/// - **亚克力/云母**：Win10 上 WinAppSDK 的 MicaBackdrop 静默无效、DesktopAcrylicBackdrop 对
///   owner 挂桌面的窗口也无效；改用 user32 的 SetWindowCompositionAttribute（DWM 在窗口背后模糊）。
/// - **不透明度**：WinUI 3 窗口带 WS_EX_NOREDIRECTIONBITMAP，`SetLayeredWindowAttributes` 必然失败，
///   所以"透明度"落到**根元素底色 alpha** 上——配合模糊就是真透出桌面，且文字始终清晰。
/// - 系统关闭"透明效果"时模糊不会显示，此时自动退回接近纯色的观感。
/// </summary>
internal static class WindowLook
{
    public const string MaterialAcrylic = "Acrylic";
    public const string MaterialMica = "Mica";
    public const string MaterialSolid = "Solid";

    /// <summary>
    /// 当前系统能不能做窗口级透明/磨砂。
    /// WinUI 3 的 `SystemBackdrop` 只在 Windows 11（build 22000+）有效；Win10 上：
    ///  - MicaBackdrop / DesktopAcrylicBackdrop 设置后**不报错但不生效**；
    ///  - SetWindowCompositionAttribute（Win10 经典磨砂）对带 WS_EX_NOREDIRECTIONBITMAP 的
    ///    WinUI 3 窗口也无效；
    ///  - SetLayeredWindowAttributes 直接返回 False（没有重定向位图可做 alpha）。
    /// 老 WPF 版是靠 AllowsTransparency 做的，迁到 WinUI 3 在 Win10 上就是做不到——别硬试。
    /// </summary>
    public static bool IsTransparencySupported =>
        Environment.OSVersion.Version.Build >= 22000;

    /// <summary>把材质 + 不透明度应用到某个窗口。</summary>
    public static void Apply(Window window, Panel root, double opacity, string material)
    {
        try
        {
            var theme = root.ActualTheme;
            var tint = CardColor(theme);
            var value = Math.Clamp(opacity, 0.35, 1.0);

            // 系统不支持时强制纯色：宁可看起来普通，也不要给用户一个"拖了半天没反应"的滑块
            var effective = IsTransparencySupported ? material : MaterialSolid;
            if (effective != material)
                App.Log($"系统不支持窗口透明（build {Environment.OSVersion.Version.Build}），材质降级为纯色");

            try
            {
                window.SystemBackdrop = effective switch
                {
                    MaterialAcrylic => new DesktopAcrylicBackdrop(),
                    MaterialMica => new MicaBackdrop
                    {
                        Kind = global::Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
                    },
                    _ => null
                };
            }
            catch (Exception ex)
            {
                App.Log($"后备材质不可用，退回纯色：{ex.Message}");
                window.SystemBackdrop = null;
                effective = MaterialSolid;
            }

            var blurOn = effective != MaterialSolid;

            // Win10 再兜一层 DWM 磨砂（对某些窗口类型有效；无效也不会坏）
            if (!IsTransparencySupported)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WindowAcrylic.Apply(hwnd, "Solid", tint.R, tint.G, tint.B, 0);
            }

            byte alpha;
            if (!blurOn) alpha = 255;
            else alpha = (byte)Math.Round(Math.Clamp(0.10 + value * 0.45, 0.10, 0.60) * 255);

            root.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(alpha, tint.R, tint.G, tint.B));
            App.Log($"外观：请求={material} 实际={effective} 底色alpha={alpha} 不透明度={value:0.00} 主题={theme}");
        }
        catch (Exception ex) { App.Log("应用窗口外观失败：" + ex.Message); }
    }

    /// <summary>从 App.xaml 的 Dn* 主题字典里取卡片底色。</summary>
    private static global::Windows.UI.Color CardColor(ElementTheme theme)
    {
        var dictName = theme == ElementTheme.Light ? "Light" : "Default";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(dictName, out var dictObj)
            && dictObj is ResourceDictionary dict
            && dict.TryGetValue("DnCardBg", out var brushObj)
            && brushObj is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return theme == ElementTheme.Light
            ? global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : global::Windows.UI.Color.FromArgb(255, 43, 43, 43);
    }
}
