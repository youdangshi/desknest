using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PathShape = System.Windows.Shapes.Path;
using LineShape = System.Windows.Shapes.Line;

namespace DeskNest.App.Ui;

/// <summary>
/// 轻量图表：实时曲线（Sparkline）与横向条形图。
/// 不引第三方图表库——这里只需要"一眼看出高低"的效果，
/// 自己画反而更好控制观感（圆角、渐变、网格线都跟着主题走）。
/// </summary>
public static partial class UiKit
{
    /// <summary>
    /// 实时曲线：面积渐变 + 折线 + 网格 + 峰值/当前值。
    /// values 是 0..max 之间的读数，按时间从旧到新。
    /// </summary>
    public static SparklineChart Sparkline(double max = 100, double height = 132, string? caption = null, string unit = "%")
        => new(max, height, caption, unit);

    /// <summary>实时曲线控件：数据可以原地更新，不用重建可视化树。</summary>
    public sealed class SparklineChart : Grid
    {
        private readonly double _max;
        private readonly string _unit;
        private readonly string? _caption;
        private readonly Canvas _gridLayer = new() { IsHitTestVisible = false };
        private readonly PathShape _areaPath = new() { IsHitTestVisible = false };
        private readonly Polyline _linePath = new()
        {
            IsHitTestVisible = false,
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        private readonly TextBlock _topLabel;
        private readonly TextBlock _bottomLabel;
        private readonly TextBlock _captionLabel;
        private IReadOnlyList<double> _values;

        internal SparklineChart(double max, double height, string? caption, string unit)
        {
            _max = max <= 0 ? 100 : max;
            _unit = unit;
            _caption = caption;
            _values = Array.Empty<double>();

            Height = height;
            Margin = new Thickness(0, 6, 0, 6);
            var tokens = Theme.Current;
            _topLabel = new TextBlock { FontSize = 11, Foreground = tokens.TertiaryBrush, Margin = new Thickness(2, 0, 0, 0) };
            _bottomLabel = new TextBlock
            {
                FontSize = 11, Foreground = tokens.TertiaryBrush, Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _captionLabel = new TextBlock
            {
                FontSize = 12, Foreground = tokens.SecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
            };

            var canvas = new Canvas();
            canvas.Children.Add(_gridLayer);
            canvas.Children.Add(_areaPath);
            canvas.Children.Add(_linePath);
            Children.Add(canvas);
            Children.Add(_topLabel);
            Children.Add(_bottomLabel);
            Children.Add(_captionLabel);

            SizeChanged += (_, _) => Render();
            Loaded += (_, _) => Render();
        }

        /// <summary>换一批数据（按时间从旧到新）。</summary>
        public void SetValues(IReadOnlyList<double> values)
        {
            _values = values ?? Array.Empty<double>();
            Render();
        }

        private void Render()
        {
            var tokens = Theme.Current;
            var accent = tokens.Accent;
            var values = _values;
            var w = ActualWidth;
            var h = ActualHeight;
            if (w < 8 || h < 8) return;

            _gridLayer.Children.Clear();
            // 三条水平网格线（25% / 50% / 75%），用极淡的前景色
            for (var i = 1; i <= 3; i++)
            {
                var y = h * i / 4.0;
                _gridLayer.Children.Add(new LineShape
                {
                    X1 = 0, X2 = w, Y1 = y, Y2 = y,
                    Stroke = tokens.BorderBrush,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 4 }
                });
            }

            if (values.Count < 2)
            {
                _areaPath.Data = null;
                _linePath.Points = new PointCollection();
                _topLabel.Text = $"{_max:0}";
                _bottomLabel.Text = "0";
                _captionLabel.Text = _caption is null ? "等待采样…" : _caption + " · 等待采样…";
                return;
            }

            var span = Math.Max(values.Count - 1, 1);
            var points = new PointCollection(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var v = Math.Clamp(values[i], 0, _max);
                points.Add(new Point(w * i / span, h - h * (v / _max)));
            }
            _linePath.Points = points;
            _linePath.Stroke = new SolidColorBrush(accent);

            // 面积：折线 + 右下角回到底边形成闭合区域
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            figure.Segments.Add(new PolyLineSegment { Points = points });
            figure.Segments.Add(new LineSegment { Point = new Point(w, h) });
            figure.Segments.Add(new LineSegment { Point = new Point(0, h) });
            _areaPath.Data = new PathGeometry { Figures = { figure } };
            _areaPath.Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(96, accent.R, accent.G, accent.B), 0),
                    new GradientStop(Color.FromArgb(10, accent.R, accent.G, accent.B), 1)
                }
            };

            var peak = values.Max();
            var current = values[^1];
            _topLabel.Text = $"{Math.Min(peak, _max):0.#}{_unit}";
            _bottomLabel.Text = "0";
            _captionLabel.Text = (_caption is null ? "" : _caption + " · ") + $"当前 {current:0.#}{_unit}";
        }
    }

    /// <summary>
    /// 横向条形图：给"各应用用了多久"这类排行用。条用圆角 + 渐变，数值右对齐。
    /// </summary>
    public static UIElement Bars(IReadOnlyList<(string Label, double Value, string ValueText)> items,
        double height = 26, double labelWidth = 150)
    {
        var tokens = Theme.Current;
        var col = new StackPanel();
        if (items.Count == 0) return col;
        var peak = Math.Max(items.Max(i => i.Value), 0.0001);

        foreach (var (label, value, text) in items)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3), Height = height };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Text(label, Tokens.FontBody);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.ToolTip = label;
            Grid.SetColumn(name, 0);

            var track = new Border
            {
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = tokens.BorderBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            var fill = new Border
            {
                Height = 10,
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(
                    Color.FromArgb(210, tokens.Accent.R, tokens.Accent.G, tokens.Accent.B),
                    Color.FromArgb(150, tokens.Accent.R, tokens.Accent.G, tokens.Accent.B),
                    new Point(0, 0), new Point(1, 0))
            };
            var barHost = new Grid();
            barHost.Children.Add(track);
            barHost.Children.Add(fill);
            barHost.SizeChanged += (_, e) => fill.Width = Math.Max(6, e.NewSize.Width * (value / peak));
            Grid.SetColumn(barHost, 1);

            var amount = Text(text, Tokens.FontDescription, secondary: true);
            amount.VerticalAlignment = VerticalAlignment.Center;
            amount.Margin = new Thickness(Tokens.Space12, 0, 0, 0);
            Grid.SetColumn(amount, 2);

            row.Children.Add(name);
            row.Children.Add(barHost);
            row.Children.Add(amount);
            col.Children.Add(row);
        }
        return col;
    }
}
