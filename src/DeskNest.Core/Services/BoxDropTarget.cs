namespace DeskNest.Core.Services;

/// <summary>一个可以被"拖上去合并"的盒子（屏幕矩形，物理像素）。</summary>
public sealed record BoxHit(
    string Id,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    string? GroupId,
    bool Visible)
{
    public long Area => (long)Math.Max(1, Width) * Math.Max(1, Height);
}

/// <summary>
/// 拖拽合并的落点判定。纯函数——这样"悬到哪个盒子上算命中"这条规则能写测试，
/// 而不是只能在界面上手试。
/// </summary>
public static class BoxDropTarget
{
    /// <summary>
    /// 给定鼠标落点，挑出应该合并过去的那个盒子。规则：
    /// ① 不能是自己；② 必须可见；③ 已经在同一组里的跳过（并过去也没意义，还会白亮一下提示）；
    /// ④ 命中范围允许往外放宽 <paramref name="margin"/> 像素（鼠标不容易精确落在边缘）；
    /// ⑤ 多个命中时取**面积最小**的那个（小盒子叠在大盒子上时，用户想并的是小盒子）。
    /// </summary>
    public static BoxHit? Pick(int px, int py, IEnumerable<BoxHit> candidates,
        string selfId, string? selfGroupId, int margin = 24)
    {
        BoxHit? best = null;
        foreach (var box in candidates)
        {
            if (string.Equals(box.Id, selfId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!box.Visible) continue;
            if (selfGroupId is not null && string.Equals(box.GroupId, selfGroupId, StringComparison.OrdinalIgnoreCase)) continue;

            var inside = px >= box.X - margin && px <= box.X + box.Width + margin
                      && py >= box.Y - margin && py <= box.Y + box.Height + margin;
            if (!inside) continue;

            if (best is null || box.Area < best.Area) best = box;
        }
        return best;
    }
}
