using DeskNest.App.Ui;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 桌面盒/组件的缩放几何：这是用户反馈"盒子没办法调节大小"之后补的回归测试。
/// 只测纯计算，不涉及真实鼠标（鼠标行为由手工冒烟清单覆盖）。
/// </summary>
public sealed class WindowDragTests
{
    [Fact]
    public void BottomRightCorner_GrowsWidthAndHeight()
    {
        var r = WindowDrag.ComputeResize(WindowDrag.Edge.BottomRight, 300, 400, 100, 100, 60, 40);
        Assert.Equal(360, r.Width);
        Assert.Equal(440, r.Height);
        Assert.Equal(100, r.Left);
        Assert.Equal(100, r.Top);
    }

    [Fact]
    public void LeftEdge_MovesWindowAndKeepsRightEdgeFixed()
    {
        var r = WindowDrag.ComputeResize(WindowDrag.Edge.Left, 300, 400, 100, 100, -50, 0);
        Assert.Equal(350, r.Width);
        Assert.Equal(50, r.Left);                 // 左边缘跟着鼠标走
        Assert.Equal(400, r.Width + r.Left);      // 右边缘保持不动（350+50=400）
    }

    [Fact]
    public void TopEdge_MovesWindowAndKeepsBottomEdgeFixed()
    {
        var r = WindowDrag.ComputeResize(WindowDrag.Edge.Top, 300, 400, 100, 100, 0, -60);
        Assert.Equal(460, r.Height);
        Assert.Equal(40, r.Top);
        Assert.Equal(500, r.Height + r.Top);      // 下边缘不动
    }

    [Fact]
    public void RightAndBottomEdges_ChangeOneDimensionOnly()
    {
        var right = WindowDrag.ComputeResize(WindowDrag.Edge.Right, 300, 400, 100, 100, 80, 999);
        Assert.Equal(380, right.Width);
        Assert.Equal(400, right.Height);

        var bottom = WindowDrag.ComputeResize(WindowDrag.Edge.Bottom, 300, 400, 100, 100, 999, 80);
        Assert.Equal(300, bottom.Width);
        Assert.Equal(480, bottom.Height);
    }

    [Fact]
    public void Shrinking_NeverGoesBelowMinimums()
    {
        var tiny = WindowDrag.ComputeResize(WindowDrag.Edge.BottomRight, 300, 400, 100, 100, -500, -500, 160, 96);
        Assert.Equal(160, tiny.Width);
        Assert.Equal(96, tiny.Height);

        var tinyLeft = WindowDrag.ComputeResize(WindowDrag.Edge.Left, 300, 400, 100, 100, 500, 0, 160, 96);
        Assert.Equal(160, tinyLeft.Width);
        Assert.Equal(240, tinyLeft.Left);   // 100 + (300-160)，右边缘仍然不动
    }
}
