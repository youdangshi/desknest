using DeskNest.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DeskNest.App.Ui;

/// <summary>
/// 把桌巢自己的四套配色映射到 WPF-UI 的主题：
/// 深色/纯黑 → Dark；浅色/护眼 → Light；跟随系统 → 跟随系统设置。
/// 桌面层（盒/组件）仍用我们自己的一套令牌，两者互不影响。
/// </summary>
public static class FluentTheme
{
    public static void Apply(ColorScheme scheme, MaterialKind material)
    {
        try
        {
            // 排错用：把"我们算的深浅"和"实际喂给 WPF-UI 的主题"同时记下来。
            // 2026-10-17 出现过"白底白字"：令牌是浅色、WPF-UI 资源却是深色，两套打架。
            var fg = Theme.Shell.Foreground;
            Log.Info($"主题应用：scheme={scheme} material={material} " +
                     $"isDark={Theme.Shell.IsDark} systemDark={SystemTheme.IsDark} " +
                     $"fg=#{fg.R:X2}{fg.G:X2}{fg.B:X2} bg=#{Theme.Shell.Background.R:X2}{Theme.Shell.Background.G:X2}{Theme.Shell.Background.B:X2}");
            switch (scheme)
            {
                case ColorScheme.Dark:
                case ColorScheme.PureBlack:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                case ColorScheme.Light:
                case ColorScheme.EyeCare:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                default:
                    // 「跟随系统」也走我们自己算的那一个值：WPF-UI 的 ApplySystemTheme 读的是系统设置，
                    // 和我们的 IsDark 曾经各算各的 → 白底白字。现在两边同一个来源。
                    ApplicationThemeManager.Apply(SystemTheme.IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("应用 WPF-UI 主题失败：" + ex.Message);
        }
    }

    /// <summary>窗口材质：Mica 用 Mica，其余用亚克力（Win11 才有；不可用时 WPF-UI 会自动降级）。</summary>
    public static WindowBackdropType BackdropFor(MaterialKind material) => material switch
    {
        MaterialKind.Mica => WindowBackdropType.Mica,
        MaterialKind.Solid => WindowBackdropType.None,
        _ => WindowBackdropType.Acrylic
    };
}
