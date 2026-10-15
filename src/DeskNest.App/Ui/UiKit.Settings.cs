using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskNest.App.Ui;

/// <summary>
/// 设置界面的"行式"零件（参考 DeskBox 的设置页观感）：
/// 一行 = 图标 + 标题 + 说明 + 右侧控件；整页 = 若干张圆角卡片。
/// </summary>
public static partial class UiKit
{
    /// <summary>卡片容器：标题（可选）+ 若干行，行之间自动加分隔线。</summary>
    public static Border Section(string? title, params UIElement[] rows)
    {
        var panel = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            panel.Children.Add(rows[i]);
            if (i < rows.Length - 1)
                panel.Children.Add(new Border { Height = 1, Background = Theme.Current.BorderBrush, Margin = new Thickness(48, 0, 0, 0) });
        }

        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                Foreground = Theme.Current.SecondaryBrush,
                Margin = new Thickness(14, 0, 0, 6)
            });
        }
        stack.Children.Add(new Border
        {
            Background = Theme.Current.CardBrush,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Current.BorderBrush,
            Padding = new Thickness(4, 2, 4, 2),
            Child = panel
        });
        stack.Margin = new Thickness(0, 0, 0, 16);
        return new Border { Child = stack };
    }

    /// <summary>一行设置：左侧图标 + 标题/说明，右侧控件。</summary>
    public static Border Row(string glyph, string title, string? subtitle, UIElement? control, Action? onClick = null)
    {
        var grid = new Grid { Margin = new Thickness(10, 9, 10, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.Current.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(icon, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            Foreground = Theme.Current.ForegroundBrush
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Foreground = Theme.Current.SecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        Grid.SetColumn(text, 1);

        if (control is not null)
        {
            if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
        }

        grid.Children.Add(icon);
        grid.Children.Add(text);
        if (control is not null) grid.Children.Add(control);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Child = grid
        };
        if (onClick is not null)
        {
            border.Cursor = Cursors.Hand;
            border.MouseEnter += (_, _) => border.Background = BrushWith(Theme.Current.Foreground, 0.05);
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (_, _) => onClick();
        }
        return border;
    }

    private static SolidColorBrush BrushWith(Color c, double opacity)
        => new(Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B));

    /// <summary>开关（iOS 风格），点击即时生效。</summary>
    public static Border Toggle(bool value, Action<bool> onChanged)
    {
        var track = new Border
        {
            Width = 44,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = value ? Theme.Current.AccentBrush : BrushWith(Theme.Current.Foreground, 0.18),
            Cursor = Cursors.Hand
        };
        var thumb = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 0, 0),
            RenderTransform = new TranslateTransform(value ? 20 : 0, 0)
        };
        track.Child = thumb;
        var isOn = value;
        track.MouseLeftButtonUp += (_, _) =>
        {
            isOn = !isOn;
            track.Background = isOn ? Theme.Current.AccentBrush : BrushWith(Theme.Current.Foreground, 0.18);
            thumb.RenderTransform = new TranslateTransform(isOn ? 20 : 0, 0);
            onChanged(isOn);
        };
        return track;
    }

    /// <summary>行 + 开关 的便捷组合。</summary>
    public static Border ToggleRow(string glyph, string title, string? subtitle, bool value, Action<bool> onChanged)
        => Row(glyph, title, subtitle, Toggle(value, onChanged));

    /// <summary>行 + 下拉 的便捷组合。</summary>
    public static Border ComboRow(string glyph, string title, string? subtitle, IEnumerable<string> items, string selected, Action<string> onChanged)
        => Row(glyph, title, subtitle, Combo(items, selected, onChanged, 140));

    /// <summary>行 + 滑杆 的便捷组合。</summary>
    public static Border SliderRow(string glyph, string title, string? subtitle, double min, double max, double value,
        Action<double> onChanged, string? valueText = null)
    {
        var slider = Slider(min, max, value, onChanged, 160);
        if (valueText is not null)
        {
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row2.Children.Add(slider);
            var label = Text(valueText, 11.5, secondary: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            row2.Children.Add(label);
            return Row(glyph, title, subtitle, row2);
        }
        return Row(glyph, title, subtitle, slider);
    }

    /// <summary>行 + 输入框。</summary>
    public static Border InputRow(string glyph, string title, string? subtitle, string value, Action<string> onChanged, double width = 180)
        => Row(glyph, title, subtitle, Input(value, onChanged, width));

    /// <summary>行 + 按钮（右侧）。</summary>
    public static Border ActionRow(string glyph, string title, string? subtitle, string buttonText, Action onClick, bool primary = false)
    {
        var b = Btn(buttonText, onClick, primary);
        b.Margin = new Thickness(0);
        return Row(glyph, title, subtitle, b);
    }

    /// <summary>行 + 一个"进入"箭头（点击整行触发）。</summary>
    public static Border NavRow(string glyph, string title, string? subtitle, Action onClick)
    {
        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 16,
            Foreground = Theme.Current.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0)
        };
        return Row(glyph, title, subtitle, chevron, onClick);
    }

    /// <summary>页面标题（大标题 + 说明），对齐 DeskBox 的"常规"样式。</summary>
    public static UIElement PageHeader(string title, string? subtitle = null)
    {
        var stack = new StackPanel { Margin = new Thickness(2, 4, 0, 14) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.Normal,
            Foreground = Theme.Current.ForegroundBrush
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12.5,
                Foreground = Theme.Current.SecondaryBrush,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        return stack;
    }

    /// <summary>左侧导航按钮（图标 + 文字 + 选中态）。</summary>
    public static Button NavButton(string glyph, string label, bool active, Action onClick)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 15,
            Width = 24,
            Foreground = active ? Theme.Current.AccentBrush : Theme.Current.SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = active ? Theme.Current.ForegroundBrush : Theme.Current.SecondaryBrush,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
        });
        var btn = new Button
        {
            Content = stack,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 1, 0, 1),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Background = active ? BrushWith(Theme.Current.Accent, 0.14) : Brushes.Transparent
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
