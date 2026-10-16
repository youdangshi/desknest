namespace DeskNest.Core.Services;

/// <summary>
/// 拖动时的**吸附对齐**：把被拖的窗口吸到"别的盒子的边"与"屏幕工作区的边"上。
/// 写成纯函数是为了能测——吸附这种手感功能，靠肉眼试覆盖不到各种边界情况。
/// </summary>
public static class WindowSnap
{
    /// <summary>吸附阈值（物理像素）：距目标边小于这个距离就吸过去。</summary>
    public const int DefaultThreshold = 8;

    public sealed record Rect(double X, double Y, double Width, double Height);

    /// <summary>
    /// 计算吸附后的位置：横向比 左/右/中心线，纵向比 上/下/中心线，取最近的那个吸。
    /// 返回是否在某个方向发生了吸附（界面可以据此画参考线）。
    /// </summary>
    public static (double X, double Y, bool SnappedX, bool SnappedY) Snap(
        Rect moving, IReadOnlyList<Rect> candidates, double threshold = DefaultThreshold)
    {
        var bestX = moving.X;
        var bestY = moving.Y;
        var bestDx = threshold + 1;
        var bestDy = threshold + 1;

        foreach (var other in candidates)
        {
            double[] xDeltas =
            {
                other.X - moving.X,                                              // 左对左
                (other.X + other.Width) - (moving.X + moving.Width),             // 右对右
                (other.X + other.Width) - moving.X,                              // 左贴对方右
                other.X - (moving.X + moving.Width),                             // 右贴对方左
                (other.X + other.Width / 2) - (moving.X + moving.Width / 2)      // 中心对中心
            };
            foreach (var delta in xDeltas)
                if (Math.Abs(delta) < Math.Abs(bestDx)) { bestDx = delta; bestX = moving.X + delta; }

            double[] yDeltas =
            {
                other.Y - moving.Y,
                (other.Y + other.Height) - (moving.Y + moving.Height),
                (other.Y + other.Height) - moving.Y,
                other.Y - (moving.Y + moving.Height),
                (other.Y + other.Height / 2) - (moving.Y + moving.Height / 2)
            };
            foreach (var delta in yDeltas)
                if (Math.Abs(delta) < Math.Abs(bestDy)) { bestDy = delta; bestY = moving.Y + delta; }
        }

        var snappedX = Math.Abs(bestDx) <= threshold;
        var snappedY = Math.Abs(bestDy) <= threshold;
        return (snappedX ? bestX : moving.X, snappedY ? bestY : moving.Y, snappedX, snappedY);
    }
}
