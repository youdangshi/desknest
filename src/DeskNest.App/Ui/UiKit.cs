using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfButton = Wpf.Ui.Controls.Button;
using WpfTextBox = Wpf.Ui.Controls.TextBox;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace DeskNest.App.Ui;

/// <summary>界面小零件工厂：让各页面用几行代码拼出统一观感。</summary>
public static partial class UiKit
{
    public static SolidColorBrush Brush(Color c, double opacity = 1) => new(c) { Opacity = opacity };
    public static SolidColorBrush Alice(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        return new SolidColorBrush(c);
    }

    public static Border Card(string? title = null, UIElement? content = null, int radius = 12, double padding = 12)
    {
        var panel = new StackPanel();
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Current.ForegroundBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }
        if (content is not null) panel.Children.Add(content);
        return new Border
        {
            Background = Theme.Current.CardBrush,
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padding),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Current.BorderBrush,
            Child = panel
        };
    }

    public static TextBlock Text(string text, double size = 13, bool secondary = false, bool bold = false)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = secondary ? Theme.Current.SecondaryBrush : Theme.Current.ForegroundBrush,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    public static Button Btn(string text, Action onClick, bool primary = false)
    {
        var b = new WpfButton
        {
            Content = text,
            Margin = new Thickness(0, 0, Tokens.Space8, 0),
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>图标按钮（30×30，透明底，悬停出现淡底）。</summary>
    public static Button IconBtn(string glyphCode, Action onClick, string? tooltip = null)
    {
        var b = new Button
        {
            Content = Icons.Glyph(glyphCode, 14),
            Margin = new Thickness(Tokens.Space2, 0, 0, 0),
            ToolTip = tooltip
        };
        b.Style = ControlStyles.IconButton();
        b.Click += (_, _) => onClick();
        return b;
    }

    public static CheckBox Check(string text, bool value, Action<bool> onChanged)
    {
        var c = new CheckBox
        {
            Content = text,
            IsChecked = value,
            FontSize = 12.5,
            Foreground = Theme.Current.ForegroundBrush,
            Margin = new Thickness(0, 4, 0, 4)
        };
        c.Checked += (_, _) => onChanged(true);
        c.Unchecked += (_, _) => onChanged(false);
        return c;
    }

    public static Slider Slider(double min, double max, double value, Action<double> onChanged, double width = 180)
    {
        var s = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = width,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 10, 2)
        };
        s.ValueChanged += (_, e) => onChanged(e.NewValue);
        return s;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static ComboBox Combo(IEnumerable<string> items, string selected, Action<string> onChanged, double width = 150)
    {
        var cb = new ComboBox
        {
            Width = width,
            Margin = new Thickness(0, Tokens.Space4, Tokens.Space8, Tokens.Space4),
            FontFamily = new FontFamily(Tokens.UiFontFamily)
        };
        cb.Style = ControlStyles.ComboBoxStyle();
        foreach (var i in items) cb.Items.Add(i);
        cb.SelectedItem = selected;
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string s) onChanged(s); };
        return cb;
    }

    public static TextBox Input(string text, Action<string> onChanged, double width = 200, bool multiline = false)
    {
        var tb = new WpfTextBox
        {
            Text = text,
            Width = width,
            Margin = new Thickness(0, Tokens.Space4, Tokens.Space8, Tokens.Space4),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 32
        };
        tb.TextChanged += (_, _) => onChanged(tb.Text);
        return tb;
    }

    /// <summary>带占位提示的输入框（用 WPF-UI TextBox 自带的 PlaceholderText）。</summary>
    public static TextBox InputWithPlaceholder(string placeholder, Action<string> onChanged, double width = 200)
    {
        var tb = (WpfTextBox)Input("", onChanged, width);
        tb.PlaceholderText = placeholder;
        tb.ClearButtonEnabled = true;
        return tb;
    }

    public static ScrollViewer Scroll(UIElement content)
        => new()
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // 禁用横向滚动条：内容稍宽就冒横条，是观感"出戏"的元凶之一
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, Tokens.Space8, 0),
            Focusable = false
        };

    public static string Fmt(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    public static string FmtSecs(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分"
            : $"{ts.Minutes} 分 {ts.Seconds} 秒";
    }

    public static DropShadowEffect SoftShadow() => new()
    {
        BlurRadius = 18,
        ShadowDepth = 2,
        Opacity = 0.28,
        Color = Colors.Black
    };

    public static void SetTooltip(FrameworkElement el, string text) => el.ToolTip = text;

    public static string Culture => CultureInfo.CurrentCulture.Name;
}
