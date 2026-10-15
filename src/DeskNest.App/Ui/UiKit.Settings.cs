using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeskNest.App.Ui;

/// <summary>
/// 设置界面的"行式"零件，度量对齐开源 DeskBox 的 SettingsOverviewResources.xaml：
/// 卡片圆角 6、行最小高度 64、行内边距 16/10、图标与文字间距 20；
/// 页面标题 28 SemiBold、设置项标题 14、说明 12。
/// 图标一律走 Icons（单色矢量字体），不再混用彩色 emoji。
/// </summary>
public static partial class UiKit
{
    /// <summary>一组设置行（圆角卡片 + 行间分隔线）。</summary>
    public static Border Section(string? title, params UIElement[] rows)
    {
        var panel = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            panel.Children.Add(rows[i]);
            if (i < rows.Length - 1)
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = Theme.Current.BorderBrush,
                    Margin = new Thickness(Tokens.RowPaddingH + Tokens.IconSize + Tokens.IconTextGap, 0, Tokens.RowPaddingH, 0)
                });
            }
        }

        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = Tokens.FontSubsectionTitle,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Current.ForegroundBrush,
                Margin = new Thickness(Tokens.Space4, 0, Tokens.Space4, Tokens.Space6)
            });
        }
        stack.Children.Add(new Border
        {
            Background = Theme.Current.CardBrush,
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Current.BorderBrush,
            Padding = Tokens.CardPadding,
            Child = panel
        });
        stack.Margin = new Thickness(0, 0, 0, Tokens.Space16);
        return new Border { Child = stack };
    }

    /// <summary>一行设置：左侧单色图标 + 标题/说明，右侧控件。</summary>
    public static Border Row(string glyph, string title, string? subtitle, UIElement? control, Action? onClick = null)
    {
        var grid = new Grid { MinHeight = Tokens.RowMinHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.RowPaddingH) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.IconSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.IconTextGap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.RowPaddingH) });

        var icon = Icons.VectorFromKey(glyph, Tokens.IconSize);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1);

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Tokens.RowPaddingV, Tokens.Space8, Tokens.RowPaddingV)
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = Tokens.FontSettingTitle,
            Foreground = Theme.Current.ForegroundBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = Tokens.FontDescription,
                Foreground = Theme.Current.SecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Tokens.Space2, 0, 0)
            });
        }
        Grid.SetColumn(text, 3);

        if (control is not null)
        {
            if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 4);
        }

        grid.Children.Add(icon);
        grid.Children.Add(text);
        if (control is not null) grid.Children.Add(control);

        var border = new Border
        {
            CornerRadius = new CornerRadius(Tokens.CornerSmall),
            Background = Brushes.Transparent,
            Child = grid
        };
        if (onClick is not null)
        {
            border.Cursor = Cursors.Hand;
            border.MouseEnter += (_, _) => border.Background = HoverBrush();
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
            border.MouseLeftButtonUp += (_, _) => onClick();
        }
        return border;
    }

    /// <summary>悬停/选中用的淡底（跟随前景色做低透明度叠加，深浅色都不会脏）。</summary>
    public static SolidColorBrush HoverBrush()
    {
        var c = Theme.Current.Foreground;
        return new SolidColorBrush(Color.FromArgb(Theme.Current.IsDark ? (byte)20 : (byte)12, c.R, c.G, c.B));
    }

    /// <summary>开关（Fluent 尺寸 40×20，圆点 14）：点击即时生效。</summary>
    public static Border Toggle(bool value, Action<bool> onChanged)
    {
        var thumbOffset = Tokens.ToggleWidth - Tokens.ToggleThumb - 6;
        var track = new Border
        {
            Width = Tokens.ToggleWidth,
            Height = Tokens.ToggleHeight,
            CornerRadius = new CornerRadius(Tokens.ToggleHeight / 2),
            Background = value ? Theme.Current.AccentBrush : HoverBrush(),
            BorderThickness = new Thickness(1),
            BorderBrush = value ? Theme.Current.AccentBrush : Theme.Current.BorderBrush,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var thumb = new Ellipse
        {
            Width = Tokens.ToggleThumb,
            Height = Tokens.ToggleThumb,
            Fill = value ? Brushes.White : Theme.Current.SecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
            RenderTransform = new TranslateTransform(value ? thumbOffset : 0, 0)
        };
        track.Child = thumb;
        var isOn = value;
        track.MouseLeftButtonUp += (_, _) =>
        {
            isOn = !isOn;
            track.Background = isOn ? Theme.Current.AccentBrush : HoverBrush();
            track.BorderBrush = isOn ? Theme.Current.AccentBrush : Theme.Current.BorderBrush;
            thumb.Fill = isOn ? Brushes.White : Theme.Current.SecondaryBrush;
            thumb.RenderTransform = new TranslateTransform(isOn ? thumbOffset : 0, 0);
            onChanged(isOn);
        };
        return track;
    }

    public static Border ToggleRow(string glyph, string title, string? subtitle, bool value, Action<bool> onChanged)
        => Row(glyph, title, subtitle, Toggle(value, onChanged));

    public static Border ComboRow(string glyph, string title, string? subtitle, IEnumerable<string> items, string selected, Action<string> onChanged)
        => Row(glyph, title, subtitle, Combo(items, selected, onChanged, 150));

    public static Border SliderRow(string glyph, string title, string? subtitle, double min, double max, double value,
        Action<double> onChanged, string? valueText = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Slider(min, max, value, onChanged, 170));
        if (valueText is not null)
        {
            var label = Text(valueText, Tokens.FontDescription, secondary: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MinWidth = 44;
            row.Children.Add(label);
        }
        return Row(glyph, title, subtitle, row);
    }

    public static Border InputRow(string glyph, string title, string? subtitle, string value, Action<string> onChanged, double width = 180)
        => Row(glyph, title, subtitle, Input(value, onChanged, width));

    public static Border ActionRow(string glyph, string title, string? subtitle, string buttonText, Action onClick, bool primary = false)
    {
        var button = Btn(buttonText, onClick, primary);
        button.Margin = new Thickness(0);
        return Row(glyph, title, subtitle, button);
    }

    public static Border NavRow(string glyph, string title, string? subtitle, Action onClick)
    {
        var chevron = Icons.Glyph(Icons.Right, 12, Theme.Current.TertiaryBrush);
        chevron.Margin = new Thickness(Tokens.Space6, 0, Tokens.Space6, 0);
        return Row(glyph, title, subtitle, chevron, onClick);
    }

    /// <summary>页面标题：28 SemiBold + 一句 12 号说明。</summary>
    public static UIElement PageHeader(string title, string? subtitle = null)
    {
        var stack = new StackPanel { Margin = new Thickness(Tokens.Space4, Tokens.Space8, 0, Tokens.Space24) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = Tokens.FontPageTitle,
            FontFamily = new FontFamily(Tokens.DisplayFontFamily),
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Current.ForegroundBrush
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = Tokens.FontDescription,
                Foreground = Theme.Current.SecondaryBrush,
                Margin = new Thickness(0, Tokens.Space6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        return stack;
    }

    /// <summary>左侧导航按钮：单色图标 + 文字 + 选中态（左侧强调条 + 淡底）。</summary>
    public static Button NavButton(string glyph, string label, bool active, Action onClick)
    {
        var grid = new Grid { Height = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.Space12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.NavIconSize + Tokens.Space12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (active)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 18,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Current.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);
        }

        var icon = Icons.VectorFromKey(glyph, Tokens.NavIconSize);
        Grid.SetColumn(icon, 2);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = label,
            FontSize = Tokens.FontBody,
            Foreground = active ? Theme.Current.ForegroundBrush : Theme.Current.SecondaryBrush,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 3);
        grid.Children.Add(text);

        var button = new Button
        {
            Content = grid,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 0, 1),
            BorderThickness = new Thickness(0),
            Background = active ? HoverBrush() : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = new CornerRadius(Tokens.CornerSmall)
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
