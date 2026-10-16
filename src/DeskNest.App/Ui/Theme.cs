using System.Windows;
using System.Windows.Media;

namespace DeskNest.App.Ui;

/// <summary>同一套设计令牌 + "两级样式"（全局默认 → 单个盒/组件可覆盖）。</summary>
public enum MaterialKind { Acrylic, Mica, Solid }
public enum ColorScheme { Dark, Light, PureBlack, EyeCare, FollowSystem }

public sealed class ThemeTokens
{
    public ColorScheme Scheme { get; set; } = ColorScheme.FollowSystem;
    public MaterialKind Material { get; set; } = MaterialKind.Acrylic;
    public double MaterialIntensity { get; set; } = 0.65;
    public Color Accent { get; set; } = Color.FromRgb(0x4C, 0x8B, 0xF5);
    public int CornerRadius { get; set; } = 12;
    public double Opacity { get; set; } = 0.82;
    public bool ReduceMotion { get; set; }

    /// <summary>
    /// 明暗判定必须由配色方案决定，不能靠时间猜：护眼（浅底深字）在夜里会被判成深色，
    /// 卡片取深色、文字取深棕，直接看不清（v1.0.0 的真实缺陷）。
    /// </summary>
    public bool IsDark => Scheme switch
    {
        ColorScheme.Dark or ColorScheme.PureBlack => true,
        ColorScheme.Light or ColorScheme.EyeCare => false,
        _ => DateTime.Now.Hour is >= 19 or < 7    // 仅"跟随系统"时按时间近似
    };

    public Color Background => Scheme switch
    {
        ColorScheme.PureBlack => Color.FromRgb(0, 0, 0),
        ColorScheme.EyeCare => Color.FromRgb(0xF2, 0xE9, 0xD8),
        ColorScheme.Light => Color.FromRgb(0xF6, 0xF6, 0xF8),
        ColorScheme.Dark => Color.FromRgb(0x1B, 0x1B, 0x1F),
        _ => IsDark ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Color.FromRgb(0xF6, 0xF6, 0xF8)
    };

    public Color Foreground => Scheme switch
    {
        ColorScheme.EyeCare => Color.FromRgb(0x2B, 0x24, 0x18),
        ColorScheme.Light => Color.FromRgb(0x1A, 0x1A, 0x1A),
        _ => Color.FromRgb(0xF5, 0xF5, 0xF5)
    };

    public Brush BackgroundBrush => new SolidColorBrush(Background);
    public Brush ForegroundBrush => new SolidColorBrush(Foreground);
    /// <summary>次要/更弱的前景色（与背景混合，深浅色都自然）。</summary>
    public Color SecondaryColor => Mix(Foreground, Background, 0.38);
    public Color TertiaryColor => Mix(Foreground, Background, 0.58);

    private static Color Mix(Color fg, Color bg, double towardBg)
    {
        double Lerp(byte a, byte b) => Math.Round(a * (1 - towardBg) + b * towardBg);
        return Color.FromRgb((byte)Lerp(fg.R, bg.R), (byte)Lerp(fg.G, bg.G), (byte)Lerp(fg.B, bg.B));
    }

    public Brush AccentBrush => new SolidColorBrush(Accent);
    /// <summary>
    /// 次要/辅助文字用"与背景混合后的实色"，而不是"前景色加透明度"：
    /// 透明度会与身后不同表面（背景 / 卡片 / 悬停淡底）混出不同结果，容易糊成一片。
    /// </summary>
    public Brush SecondaryBrush => new SolidColorBrush(SecondaryColor);
    public Brush TertiaryBrush => new SolidColorBrush(TertiaryColor);
    /// <summary>卡片底色（浅色/护眼用白、深色用近黑），单独暴露成 Color 便于对比度测试。</summary>
    public Color CardColor => Scheme switch
    {
        ColorScheme.PureBlack => Color.FromRgb(0x14, 0x14, 0x18),
        ColorScheme.EyeCare => Color.FromRgb(0xFF, 0xFC, 0xF4),
        ColorScheme.Light => Color.FromRgb(0xFF, 0xFF, 0xFF),
        ColorScheme.Dark => Color.FromRgb(0x28, 0x28, 0x2E),
        _ => IsDark ? Color.FromRgb(0x28, 0x28, 0x2E) : Color.FromRgb(0xFF, 0xFF, 0xFF)
    };
    public Brush CardBrush => new SolidColorBrush(CardColor);
    public Brush BorderBrush => new SolidColorBrush(Foreground) { Opacity = IsDark ? 0.12 : 0.08 };
    /// <summary>危险操作的颜色（删除等）。单独暴露 Color，便于菜单项比较/复用。</summary>
    public Color DangerColor => Color.FromRgb(0xE2, 0x53, 0x53);
    public Brush DangerBrush => new SolidColorBrush(DangerColor);
    public Brush SuccessBrush => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));

    public ThemeTokens Clone() => (ThemeTokens)MemberwiseClone();
}

public static class Theme
{
    /// <summary>设计窗口（主窗口与设置页）的令牌。</summary>
    public static ThemeTokens Shell { get; private set; } = new();

    /// <summary>收纳窗口（桌面盒 / 组件 / 桌宠）的令牌。与设计窗口互不影响，各自独立设置。</summary>
    public static ThemeTokens Desktop { get; private set; } = new();

    /// <summary>兼容旧调用：等同于设计窗口令牌（设置页、通知、搜索面板都用它）。</summary>
    public static ThemeTokens Current => Shell;

    public static event Action? Changed;

    public static void Apply(ThemeTokens tokens) => ApplyShell(tokens);

    public static void ApplyShell(ThemeTokens tokens)
    {
        Shell = tokens;
        Changed?.Invoke();
    }

    public static void ApplyDesktop(ThemeTokens tokens)
    {
        Desktop = tokens;
        ApplyMenuColors(tokens);
        Changed?.Invoke();
    }

    /// <summary>
    /// 把菜单（右键菜单/子菜单）的配色改成我们自己的。
    /// 必须写在**应用级资源**里：WPF 菜单模板用的是 `{StaticResource SystemColors.MenuBrushKey}`
    /// 这类静态引用，在控件实例上覆盖没用——这正是"暗色主题下菜单变成浅底白字、看不清"的原因。
    /// </summary>
    private static void ApplyMenuColors(ThemeTokens t)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null) return;
            var highlight = Color.FromArgb(48, t.Accent.R, t.Accent.G, t.Accent.B);
            app.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(t.CardColor);
            app.Resources[SystemColors.MenuBarBrushKey] = new SolidColorBrush(t.CardColor);
            app.Resources[SystemColors.MenuTextBrushKey] = new SolidColorBrush(t.Foreground);
            app.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(highlight);
            app.Resources[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(t.Foreground);
            app.Resources[SystemColors.MenuHighlightBrushKey] = new SolidColorBrush(highlight);
        }
        catch { }
    }

    /// <summary>把当前令牌应用到窗口外壳（背景/前景）。运行时换主题必须调用，否则只剩页面内容变色。</summary>
    public static void ApplyToShell(System.Windows.Window window)
    {
        window.Background = Shell.BackgroundBrush;
        window.Foreground = Shell.ForegroundBrush;
    }

    /// <summary>把"设计窗口令牌 + 单对象覆盖"合成最终令牌。</summary>
    public static ThemeTokens Resolve(string? material, double? intensity, double? opacity, int? corner, bool inherit)
        => ResolveFrom(Shell, material, intensity, opacity, corner, inherit);

    /// <summary>把"收纳窗口令牌 + 单对象覆盖"合成最终令牌（桌面盒 / 组件用）。</summary>
    public static ThemeTokens ResolveDesktop(string? material, double? intensity, double? opacity, int? corner, bool inherit)
        => ResolveFrom(Desktop, material, intensity, opacity, corner, inherit);

    private static ThemeTokens ResolveFrom(ThemeTokens basis, string? material, double? intensity, double? opacity, int? corner, bool inherit)
    {
        var t = basis.Clone();
        if (inherit) return t;
        if (!string.IsNullOrEmpty(material) && Enum.TryParse<MaterialKind>(material, out var m)) t.Material = m;
        if (intensity is { } i) t.MaterialIntensity = i;
        if (opacity is { } o) t.Opacity = o;
        if (corner is { } c) t.CornerRadius = c;
        return t;
    }
}
