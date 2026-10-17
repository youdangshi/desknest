using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfTextBlock = Wpf.Ui.Controls.TextBlock;
using TextBlock = System.Windows.Controls.TextBlock;

namespace DeskNest.App.Ui;

/// <summary>
/// 设置界面的"行式"组件，改用 WPF-UI（lepoco，MIT）的原生 Fluent 控件实现：
///  - 每行 = <see cref="CardControl"/>（左：图标 + 标题 + 说明；右：控件）
///  - 开关 = <see cref="ToggleSwitch"/>；输入 = WPF-UI 的 TextBox（自带占位提示与清除按钮）
///  - 提示条 = <see cref="InfoBar"/>；导航项 = <see cref="NavigationViewItem"/>（含分组头）
/// 度量仍沿用我们定的令牌（行高 64 / 圆角 6 / 内容列 840 等）。
/// </summary>
public static partial class UiKit
{
    /// <summary>一组设置行：可选分组标题 + 若干卡片（每张卡片自带 Fluent 圆角与描边）。</summary>
    public static UIElement Section(string? title, params UIElement[] rows)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, Tokens.Space16) };
        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(new WpfTextBlock
            {
                Text = title,
                FontSize = Tokens.FontSubsectionTitle,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(Tokens.Space2, 0, 0, Tokens.Space8)
            });
        }
        foreach (var row in rows)
        {
            if (row is FrameworkElement element) element.Margin = new Thickness(0, 0, 0, Tokens.Space4);
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>一行设置（Fluent 卡片）：左侧图标 + 标题 + 说明，右侧放控件。</summary>
    public static UIElement Row(string glyph, string title, string? subtitle, UIElement? control, Action? onClick = null)
    {
        var header = new StackPanel { Margin = new Thickness(0, Tokens.Space4, 0, Tokens.Space4) };
        header.Children.Add(new WpfTextBlock
        {
            Text = title,
            FontSize = Tokens.FontSettingTitle,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Children.Add(new WpfTextBlock
            {
                Text = subtitle,
                FontSize = Tokens.FontDescription,
                // 以前是 Foreground + Opacity 0.72：白底上算出来只有 2.7:1（用户说看不清就是这里）。
                // 改成实色次要色（0.18 混背景），不再叠透明度。
                Foreground = Theme.Current.SecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Tokens.Space2, 0, 0)
            });
        }

        var card = new CardControl
        {
            Header = header,
            Icon = new SymbolIcon { Symbol = Icons.SymbolFor(glyph), FontSize = Tokens.IconSize },
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            Margin = new Thickness(0, 0, 0, Tokens.Space4)
        };

        if (control is not null)
        {
            if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
            card.Content = control;
        }
        if (onClick is not null)
        {
            card.Cursor = Cursors.Hand;
            card.Click += (_, _) => onClick();
        }
        return card;
    }

    /// <summary>Fluent 开关。</summary>
    public static UIElement Toggle(bool value, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        return toggle;
    }

    public static UIElement ToggleRow(string glyph, string title, string? subtitle, bool value, Action<bool> onChanged)
        => Row(glyph, title, subtitle, Toggle(value, onChanged));

    public static UIElement ComboRow(string glyph, string title, string? subtitle, IEnumerable<string> items, string selected, Action<string> onChanged)
        => Row(glyph, title, subtitle, Combo(items, selected, onChanged, 150));

    /// <summary>
    /// 带中文标签的下拉行：界面上显示中文，设置里仍然存原始值
    /// （配色/材质这类值是设置文件与代码里的枚举名，直接显示会满屏英文）。
    /// </summary>
    public static UIElement ComboRow(string glyph, string title, string? subtitle,
        (string Value, string Label)[] items, string selected, Action<string> onChanged)
    {
        var values = items.Select(i => i.Value).ToList();
        var labels = items.ToDictionary(i => i.Value, i => i.Label, StringComparer.OrdinalIgnoreCase);

        var box = Combo(items.Select(i => i.Label), labels.TryGetValue(selected, out var current) ? current : items.FirstOrDefault().Label,
            label => onChanged(labels.FirstOrDefault(kv => kv.Value == label).Key ?? label), 150);
        box.ToolTip = string.Join(" / ", items.Select(i => i.Label));
        return Row(glyph, title, subtitle, box);
    }

    public static UIElement SliderRow(string glyph, string title, string? subtitle, double min, double max, double value,
        Action<double> onChanged, string? valueText = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        TextBlock? label = null;
        if (valueText is not null) label = Text(valueText, Tokens.FontDescription, secondary: false);   // 数值是数据，用正文字色

        var slider = Slider(min, max, value, v =>
        {
            if (label is not null)
                label.Text = min >= 0 && max <= 1.2 ? v.ToString("0.00") : ((int)Math.Round(v)).ToString();
            onChanged(v);
        }, 170);
        row.Children.Add(slider);
        if (label is not null)
        {
            label.VerticalAlignment = VerticalAlignment.Center;
            label.MinWidth = 44;
            row.Children.Add(label);
        }
        return Row(glyph, title, subtitle, row);
    }

    public static UIElement InputRow(string glyph, string title, string? subtitle, string value, Action<string> onChanged, double width = 180)
        => Row(glyph, title, subtitle, Input(value, onChanged, width));

    public static UIElement ActionRow(string glyph, string title, string? subtitle, string buttonText, Action onClick, bool primary = false)
    {
        var button = Btn(buttonText, onClick, primary);
        button.Margin = new Thickness(0);
        return Row(glyph, title, subtitle, button);
    }

    public static UIElement NavRow(string glyph, string title, string? subtitle, Action onClick)
    {
        var chevron = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24, FontSize = 12, Opacity = 0.6 };
        chevron.Margin = new Thickness(Tokens.Space6, 0, Tokens.Space2, 0);
        return Row(glyph, title, subtitle, chevron, onClick);
    }

    /// <summary>页面标题：28 SemiBold + 一句说明。</summary>
    public static UIElement PageHeader(string title, string? subtitle = null)
    {
        var stack = new StackPanel { Margin = new Thickness(Tokens.Space2, Tokens.Space8, 0, Tokens.Space20) };
        stack.Children.Add(new WpfTextBlock
        {
            Text = title,
            FontSize = Tokens.FontPageTitle,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new WpfTextBlock
            {
                Text = subtitle,
                FontSize = Tokens.FontDescription,
                Foreground = Theme.Current.SecondaryBrush,   // 同设置行：不再用 0.72 透明度
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Tokens.Space6, 0, 0)
            });
        }
        return stack;
    }

    /// <summary>左侧导航项（Fluent NavigationViewItem：自带图标、悬停、选中态）。</summary>
    public static NavigationViewItem NavButton(string glyph, string label, bool active, Action onClick)
    {
        var item = new NavigationViewItem
        {
            Content = label,
            Icon = new SymbolIcon { Symbol = Icons.SymbolFor(glyph) },
            IsActive = active,
            Margin = new Thickness(Tokens.Space4, 1, Tokens.Space4, 1),
            Cursor = Cursors.Hand
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>导航分组标题。</summary>
    public static NavigationViewItemHeader NavGroup(string label, double topMargin = 12)
        => new()
        {
            Text = label,
            Margin = new Thickness(Tokens.Space4, topMargin, Tokens.Space4, Tokens.Space2)
        };

    /// <summary>悬停/选中用的淡底（供少数自绘控件使用）。</summary>
    public static SolidColorBrush HoverBrush()
    {
        var c = Theme.Current.Foreground;
        return new SolidColorBrush(Color.FromArgb(Theme.Current.IsDark ? (byte)20 : (byte)12, c.R, c.G, c.B));
    }
}
