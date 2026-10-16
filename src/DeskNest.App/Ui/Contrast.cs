using System.Windows.Media;

namespace DeskNest.App.Ui;

/// <summary>
/// WCAG 对比度计算：把"看不清"变成可测的回归断言。
/// 阈值：正文 ≥ 4.5:1；大字（≥18px 或 14px 粗体）与界面元件 ≥ 3:1。
/// </summary>
public static class Contrast
{
    public static double Ratio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>
    /// 在一块**任意颜色**的底上，文字该用白还是近黑——取对比度更高的那个。
    /// 强调色是用户可选的：选了浅黄还硬写白字就会"白底白字"。所有"填色底 + 文字"的地方
    /// （主按钮、徽标、选中项）都必须走这个函数，而不是固定写死白色。
    /// </summary>
    public static Color BestForegroundOn(Color background)
    {
        var light = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var dark = Color.FromRgb(0x1A, 0x1A, 0x1A);
        return Ratio(light, background) >= Ratio(dark, background) ? light : dark;
    }

    /// <summary>把一个半透明色叠到某个底色上，算出实际看到的颜色（盒子半透明时判断可读性用）。</summary>
    public static Color Composite(Color overlay, double alpha, Color behind)
    {
        var a = Math.Clamp(alpha, 0, 1);
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
        return Color.FromRgb(Mix(overlay.R, behind.R), Mix(overlay.G, behind.G), Mix(overlay.B, behind.B));
    }

    /// <summary>
    /// 文字压在"半透明卡片 + 任意壁纸"上时，靠颜色本身已经保证不了可读性——
    /// 这种情况需要给文字加一层描边/阴影来兜底。阈值取 4.5:1（正文标准）。
    /// </summary>
    public static bool NeedsTextBackdrop(Color text, Color cardColor, double cardAlpha, Color wallpaper)
        => Ratio(text, Composite(cardColor, cardAlpha, wallpaper)) < 4.5;
}
