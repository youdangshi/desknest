using System.Windows;
using System.Windows.Media;

namespace DeskNest.App.Ui;

/// <summary>同一套设计令牌 + "两级样式"（全局默认 → 单个盒/组件可覆盖）。</summary>
public enum MaterialKind { Acrylic, Mica, Solid }
public enum ColorScheme { Dark, Light, PureBlack, EyeCare, FollowSystem }

/// <summary>卡片最终画法（A/R/G/B）。抽出来是为了让冒烟验证器直接比较"画出来的像素"，不必实例化 WPF 控件。</summary>
public readonly record struct CardPaint(byte A, byte R, byte G, byte B);

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
    /// 「卡片颜色」覆盖项（null = 跟随配色方案）。桌面盒 / 组件 / 桌宠的底色优先用它。
    /// 以前只有"配色方案"能间接影响盒底，产品里没有换色入口 —— 用户反馈"改外观参数盒子颜色不变"就是这里。
    /// </summary>
    public Color? CardColorOverride { get; set; }

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

    /// <summary>
    /// 文字色：默认跟着配色方案走；**一旦自定义了卡片颜色，就按卡片自己的亮度重算**（浅底配深字、深底配浅字）。
    /// 否则"深色方案 + 白色卡片"会得到白底白字。
    /// </summary>
    public Color Foreground => CardColorOverride is { } custom
        ? Contrast.BestForegroundOn(custom)
        : Scheme switch
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

    /// <summary>卡片（桌面盒 / 组件）真正画出来的底色：自定义颜色优先，否则跟随配色方案。</summary>
    public Color PaintColor => CardColorOverride ?? Background;

    /// <summary>
    /// 材质怎么影响绘制：（不透明度系数，向前景色靠拢的比例）。
    /// 纯色 = 完全按用户设的透明度画；亚克力越强越"厚"（更实、略微发亮）；云母更贴背景（更透、更融）。
    /// 纯函数、不碰 WPF 控件，冒烟验证器可以直接断言像素。
    /// </summary>
    public (double AlphaFactor, double TintToForeground) MaterialFactors()
    {
        var i = Math.Clamp(MaterialIntensity, 0, 1);
        return Material switch
        {
            MaterialKind.Solid => (1.00, 0.00),
            MaterialKind.Mica => (0.40 + 0.35 * i, 0.28 * i),
            _ => (0.60 + 0.40 * i, 0.10 * i)          // Acrylic（默认）
        };
    }

    /// <summary>把令牌算成最终像素：颜色 + 材质 + 材质强度 + 透明度一起参与，谁都不是死控件。</summary>
    public CardPaint ResolveCardPaint()
    {
        var (alphaFactor, tint) = MaterialFactors();
        var card = PaintColor;
        var fg = Foreground;
        byte Mix(byte a, byte b) => (byte)Math.Round(a * (1 - tint) + b * tint);
        return new CardPaint(
            (byte)(Math.Clamp(Opacity, 0.25, 1.0) * alphaFactor * 255),
            Mix(card.R, fg.R), Mix(card.G, fg.G), Mix(card.B, fg.B));
    }

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
    public static ThemeTokens ResolveDesktop(string? material, double? intensity, double? opacity, int? corner, bool inherit,
        Color? cardColor = null)
        => ResolveFrom(Desktop, material, intensity, opacity, corner, inherit, cardColor);

    private static ThemeTokens ResolveFrom(ThemeTokens basis, string? material, double? intensity, double? opacity, int? corner,
        bool inherit, Color? cardColor = null)
    {
        var t = basis.Clone();
        if (inherit) return t;
        if (cardColor is { } cc) t.CardColorOverride = cc;
        if (!string.IsNullOrEmpty(material) && Enum.TryParse<MaterialKind>(material, out var m)) t.Material = m;
        if (intensity is { } i) t.MaterialIntensity = i;
        if (opacity is { } o) t.Opacity = o;
        if (corner is { } c) t.CornerRadius = c;
        return t;
    }
}
