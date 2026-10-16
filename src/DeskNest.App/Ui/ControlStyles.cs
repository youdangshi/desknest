using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace DeskNest.App.Ui;

/// <summary>
/// 控件样式：用真实的 ControlTemplate 替换 WPF 默认外观（默认外观是界面"出戏"的主因）。
/// 度量取自 Fluent：按钮 34 高 / 最小宽 104 / 圆角 6，图标按钮 30×30 圆角 5，
/// 输入框圆角 6 + 细分描边，开关 40×20 圆角 10。
/// 样式是"每次创建控件时按当前主题生成"，所以切换主题后新建的控件自动跟随。
/// </summary>
public static class ControlStyles
{
    private static Style BuildButton(bool accent)
    {
        var style = new Style(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Root");
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(FrameworkElement.Tag))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(accent ? Darken(Theme.Current.Accent) : Lighten(Theme.Current.CardBrush)), "Root"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Darken(Theme.Current.Accent)), "Root"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Root"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(disabled);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontSizeProperty, Tokens.FontBody));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, Tokens.ButtonHeight));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, Tokens.ButtonHeight));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, Tokens.ButtonMinWidth));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Tokens.Space12, 0, Tokens.Space12, 0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, accent ? new Thickness(0) : new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.TagProperty, new CornerRadius(Tokens.CornerControl)));
        // 主按钮：一点竖向渐变 + 极淡投影，让它从一堆描边按钮里"立"起来；
        // 次要按钮保持纯色描边，避免整页都在发光。
        style.Setters.Add(new Setter(Control.BackgroundProperty, accent ? AccentFill() : Theme.Current.CardBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, accent ? Brushes.White : Theme.Current.ForegroundBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Current.BorderBrush));
        if (accent)
        {
            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 1.5,
                Direction = 270,
                Opacity = 0.28,
                Color = Darken(Theme.Current.Accent)
            };
            style.Setters.Add(new Setter(UIElement.EffectProperty, shadow));
        }
        return style;
    }

    /// <summary>主按钮底色：强调色的浅渐变（上浅下深），比纯色更有质感。</summary>
    private static Brush AccentFill()
    {
        var accent = Theme.Current.Accent;
        var light = Color.FromRgb(
            (byte)Math.Min(255, accent.R + 18),
            (byte)Math.Min(255, accent.G + 18),
            (byte)Math.Min(255, accent.B + 18));
        return new LinearGradientBrush(light, accent, new Point(0.5, 0), new Point(0.5, 1));
    }

    private static Color Darken(Color c) => Color.FromRgb(
        (byte)Math.Max(0, c.R - 24), (byte)Math.Max(0, c.G - 24), (byte)Math.Max(0, c.B - 24));

    private static Color Lighten(Brush brush)
    {
        if (brush is not SolidColorBrush solid) return Colors.Transparent;
        var c = solid.Color;
        var delta = Theme.Current.IsDark ? 18 : -10;
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + delta, 0, 255),
            (byte)Math.Clamp(c.G + delta, 0, 255),
            (byte)Math.Clamp(c.B + delta, 0, 255));
    }

    public static Style Button() => BuildButton(accent: false);
    public static Style AccentButton() => BuildButton(accent: true);

    /// <summary>
    /// 导航项样式：完全扁平（没有默认按钮的边框/底色/悬停蓝块），
    /// 背景由调用方传入（选中态用强调色淡底），悬停时在同色系上加深一点。
    /// 不套这个样式时，WPF 默认按钮模板会画出自己的浅蓝悬停块——就是导航里那块"多出来的半段"。
    /// </summary>
    public static Style NavButtonStyle()
    {
        var style = new Style(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Tokens.CornerSmall));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Tint(0.10), "Bd"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Tint(0.18), "Bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }

    /// <summary>强调色淡底（导航选中态用）。</summary>
    public static SolidColorBrush AccentTint(double opacity)
    {
        var c = Theme.Current.Accent;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B));
    }

    private static SolidColorBrush Tint(double opacity)
    {
        var c = Theme.Current.Foreground;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B));
    }

    /// <summary>图标按钮：30×30、圆角 5、透明底（悬停出现淡底）。</summary>
    public static Style IconButton()
    {
        var style = BuildButton(accent: false);
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, Tokens.IconButtonSize));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, Tokens.IconButtonSize));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, Tokens.IconButtonSize));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(FrameworkElement.TagProperty, new CornerRadius(Tokens.CornerSmall)));
        return style;
    }

    /// <summary>文本输入框：圆角 6、卡片底、细分描边。</summary>
    public static Style TextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Current.CardBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Current.ForegroundBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Theme.Current.BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty,
            new Thickness(Tokens.Space12, Tokens.Space6, Tokens.Space12, Tokens.Space6)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, Tokens.FontBody));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildTextBoxTemplate()));
        return style;
    }

    /// <summary>输入框模板：带占位文本（占位内容放在控件 Tag 里，没有内容时显示）。</summary>
    private static ControlTemplate BuildTextBoxTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Tokens.CornerControl));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        scroll.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(Control.Padding))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.AppendChild(scroll);
        var placeholder = new FrameworkElementFactory(typeof(TextBlock), "Placeholder");
        placeholder.SetBinding(TextBlock.TextProperty, new Binding(nameof(FrameworkElement.Tag))
        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        placeholder.SetValue(TextBlock.ForegroundProperty, Theme.Current.TertiaryBrush);
        placeholder.SetValue(TextBlock.FontSizeProperty, Tokens.FontBody);
        placeholder.SetValue(TextBlock.MarginProperty, new Thickness(Tokens.Space12 + 1, 0, 0, 0));
        placeholder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        placeholder.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(border);
        grid.AppendChild(placeholder);

        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = grid };
        // 占位文本：默认隐藏，只有内容为空时显示（之前的写法反了，导致搜索框是个空块）
        placeholder.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        var empty = new Trigger { Property = TextBox.TextProperty, Value = "" };
        empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Placeholder"));
        template.Triggers.Add(empty);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Current.AccentBrush, "Bd"));
        template.Triggers.Add(focused);
        return template;
    }

    /// <summary>下拉框：统一圆角与字号。</summary>
    public static Style ComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.FontSizeProperty, Tokens.FontBody));
        style.Setters.Add(new Setter(Control.PaddingProperty,
            new Thickness(Tokens.Space12, Tokens.Space4, Tokens.Space8, Tokens.Space4)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Current.CardBrush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Current.ForegroundBrush));
        return style;
    }

    /// <summary>滑杆：更细的轨道与更小的滑块，避免默认样式看起来"很工具"。</summary>
    public static Style SliderStyle()
    {
        var style = new Style(typeof(Slider));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        style.Setters.Add(new Setter(Slider.IsSnapToTickEnabledProperty, false));
        return style;
    }
}
