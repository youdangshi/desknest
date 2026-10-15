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

    public bool IsDark => Scheme switch
    {
        ColorScheme.Light => false,
        ColorScheme.Dark or ColorScheme.PureBlack => true,
        _ => DateTime.Now.Hour is >= 19 or < 7
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
    public Brush SecondaryBrush => new SolidColorBrush(Foreground) { Opacity = 0.62 };
    public Brush TertiaryBrush => new SolidColorBrush(Foreground) { Opacity = 0.42 };
    public Brush CardBrush => new SolidColorBrush(IsDark
        ? Color.FromRgb(0x28, 0x28, 0x2E)
        : Color.FromRgb(0xFF, 0xFF, 0xFF));
    public Brush BorderBrush => new SolidColorBrush(Foreground) { Opacity = IsDark ? 0.12 : 0.08 };
    public Brush DangerBrush => new SolidColorBrush(Color.FromRgb(0xE2, 0x53, 0x53));
    public Brush SuccessBrush => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));

    public ThemeTokens Clone() => (ThemeTokens)MemberwiseClone();
}

public static class Theme
{
    public static ThemeTokens Current { get; private set; } = new();
    public static event Action? Changed;

    public static void Apply(ThemeTokens tokens)
    {
        Current = tokens;
        Changed?.Invoke();
    }

    /// <summary>把"全局令牌 + 单窗口覆盖"合成最终令牌。</summary>
    public static ThemeTokens Resolve(string? material, double? intensity, double? opacity, int? corner, bool inherit)
    {
        var t = Current.Clone();
        if (inherit) return t;
        if (!string.IsNullOrEmpty(material) && Enum.TryParse<MaterialKind>(material, out var m)) t.Material = m;
        if (intensity is { } i) t.MaterialIntensity = i;
        if (opacity is { } o) t.Opacity = o;
        if (corner is { } c) t.CornerRadius = c;
        return t;
    }
}
