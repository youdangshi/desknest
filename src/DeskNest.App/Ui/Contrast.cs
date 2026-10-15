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
}
