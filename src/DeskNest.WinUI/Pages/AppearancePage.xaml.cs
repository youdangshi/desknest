using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 外观与主题：配色方案 + 圆角，写 Core 设置（theme.desktop.*，与 WPF 版同一批键，互通），
/// 改完立刻应用——不用重启，盒子窗口和主窗口当场变。
/// 透明度/材质在 WinUI 下属窗口级合成（不能只调根元素透明度，会连文字一起淡），P4 先不做，页面上明说。
/// </summary>
public sealed partial class AppearancePage : Page
{
    private const string KeyScheme = App.KeyScheme;
    private const string KeyCorner = App.KeyCorner;

    private ComboBox? _schemeBox;
    private Slider? _cornerSlider;
    private TextBlock? _cornerValue;
    private Border? _preview;

    /// <summary>
    /// 正在用设置值回填控件。回填会触发 SelectionChanged/ValueChanged，
    /// 不加这个闸门就会把"半路的值"（比如滑块初始 0）写回设置——P4 自验时圆角被写成了 2。
    /// </summary>
    private bool _syncing;

    public AppearancePage()
    {
        InitializeComponent();
        Build();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncFromSettings();
    }

    private void Build()
    {
        Host.Children.Add(PageKit.PageTitle("外观与主题"));
        Host.Children.Add(PageKit.PageIntro("配色与圆角即时生效，盒子窗口和主窗口一起变。设置与桌面版共用，换回桌面版也是同一套。"));
        Host.Children.Add(MakeSettingsCard());
        Host.Children.Add(MakeMaterialCard());
        Host.Children.Add(MakePreviewCard());
        Host.Children.Add(PageKit.Hint("材质由系统合成：Windows 11 支持亚克力与云母；Windows 10 上 WinUI 3 无法做窗口透明，会自动按纯色渲染。"));
    }

    private ComboBox? _materialBox;
    private Slider? _opacitySlider;
    private TextBlock? _opacityValue;

    /// <summary>材质 + 不透明度：盒子与组件共用（theme.desktop.material / opacity）。</summary>
    private UIElement MakeMaterialCard()
    {
        var col = PageKit.Column(14);

        _materialBox = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in new[]
                 {
                     ("磨砂（模拟·壁纸模糊，Win10 可用）", Windows.WindowLook.MaterialGlass),
                     ("亚克力（系统材质，需 Win11）", Windows.WindowLook.MaterialAcrylic),
                     ("云母（系统材质，需 Win11）", Windows.WindowLook.MaterialMica),
                     ("纯色（不透明）", Windows.WindowLook.MaterialSolid)
                 })
        {
            _materialBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
        _materialBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_materialBox.SelectedItem is not ComboBoxItem it) return;
            var value = it.Tag as string ?? Windows.WindowLook.MaterialAcrylic;
            if (App.Settings?.Get(App.KeyMaterial, Windows.WindowLook.MaterialAcrylic) == value) return;
            App.Settings?.Set(App.KeyMaterial, value);
            ApplyNow($"材质 → {it.Content}");
        };
        col.Children.Add(PageKit.Label("材质"));
        col.Children.Add(_materialBox);

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _opacitySlider = new Slider { Minimum = 0.35, Maximum = 1.0, StepFrequency = 0.05, VerticalAlignment = VerticalAlignment.Center };
        _opacityValue = new TextBlock { FontSize = 12.5, MinWidth = 40, TextAlignment = TextAlignment.Right, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        _opacitySlider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            var value = Math.Round(e.NewValue, 2);
            if (_opacityValue is not null) _opacityValue.Text = $"{value:0.00}";
            if (Math.Abs((App.Settings?.Get(App.KeyOpacity, 0.95) ?? 0.95) - value) < 0.001) return;
            App.Settings?.Set(App.KeyOpacity, value);
            ApplyNow($"不透明度 → {value:0.00}");
        };
        row.Children.Add(_opacitySlider);
        Grid.SetColumn(_opacityValue, 1);
        row.Children.Add(_opacityValue);
        col.Children.Add(PageKit.Label("不透明度（盒子与组件一起变淡）"));
        col.Children.Add(row);

        if (!Windows.WindowLook.IsTransparencySupported)
        {
            col.Children.Add(PageKit.Hint(
                "当前系统是 Windows 10：亚克力/云母只在 Windows 11 生效（WinUI 3 无窗口级透明，" +
                "桌面版 WPF 的 AllowsTransparency 没有等价能力）。想要毛玻璃观感请选「磨砂（模拟）」——" +
                "它把窗口背后的壁纸模糊后铺成底色，任何系统都能用。"));
        }

        return PageKit.Card("材质与不透明度", col);
    }

    private UIElement MakeSettingsCard()
    {
        var col = PageKit.Column(14);

        // 配色
        _schemeBox = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in new[]
                 {
                     ("跟随系统", "FollowSystem"),
                     ("深色", "Dark"),
                     ("浅色", "Light"),
                     ("纯黑（OLED 友好）", "PureBlack"),
                     ("护眼（米黄）", "EyeCare")
                 })
        {
            _schemeBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
        _schemeBox.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_schemeBox.SelectedItem is not ComboBoxItem it) return;
            var value = it.Tag as string ?? "FollowSystem";
            if (App.Settings?.Get(KeyScheme, "FollowSystem") == value) return;   // 防重入
            App.Settings?.Set(KeyScheme, value);
            ApplyNow($"配色 → {it.Content}");
        };
        col.Children.Add(PageKit.Label("配色"));
        col.Children.Add(_schemeBox);

        // 圆角
        var cornerRow = new Grid { ColumnSpacing = 12 };
        cornerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cornerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _cornerSlider = new Slider { Minimum = 0, Maximum = 28, StepFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        _cornerValue = new TextBlock { FontSize = 12.5, MinWidth = 36, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, TextAlignment = TextAlignment.Right };
        _cornerSlider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            var value = (int)Math.Round(e.NewValue);
            if (_cornerValue is not null) _cornerValue.Text = value.ToString();
            if (App.Settings?.Get(KeyCorner, 12) == value) return;
            App.Settings?.Set(KeyCorner, value);
            ApplyNow($"圆角 → {value}");
        };
        cornerRow.Children.Add(_cornerSlider);
        Grid.SetColumn(_cornerValue, 1);
        cornerRow.Children.Add(_cornerValue);

        col.Children.Add(PageKit.Label("圆角（0 = 直角，28 = 圆润）"));
        col.Children.Add(cornerRow);

        return PageKit.Card("配色与形状", col);
    }

    /// <summary>小预览卡：跟着配色/圆角实时变。</summary>
    private UIElement MakePreviewCard()
    {
        _preview = new Border
        {
            Height = 96,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "示例收纳盒", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12.5 },
                    new TextBlock { Text = "圆角与配色会同时应用到这个标题栏", FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        PageKit.SetThemedBrush(_preview, "DnCardBg", b => _preview!.Background = b);
        PageKit.SetThemedBrush(_preview, "DnCardStroke", b => _preview!.BorderBrush = b);
        return PageKit.Card("预览", _preview);
    }

    private void SyncFromSettings()
    {
        _syncing = true;
        try
        {
            var scheme = App.Settings?.Get(KeyScheme, "FollowSystem") ?? "FollowSystem";
            if (_schemeBox is not null)
            {
                foreach (var item in _schemeBox.Items.OfType<ComboBoxItem>())
                {
                    if ((item.Tag as string) == scheme) { _schemeBox.SelectedItem = item; break; }
                }
                if (_schemeBox.SelectedItem is null && _schemeBox.Items.Count > 0) _schemeBox.SelectedIndex = 0;
            }

            var corner = App.Settings?.Get(KeyCorner, 12) ?? 12;
            if (_cornerSlider is not null)
            {
                _cornerSlider.Value = corner;
                if (_cornerValue is not null) _cornerValue.Text = corner.ToString();
            }
            if (_preview is not null) _preview.CornerRadius = new CornerRadius(corner);

            // 材质 / 不透明度
            var material = App.Settings?.Get(App.KeyMaterial, Windows.WindowLook.MaterialAcrylic) ?? Windows.WindowLook.MaterialAcrylic;
            if (_materialBox is not null)
            {
                foreach (var item in _materialBox.Items.OfType<ComboBoxItem>())
                {
                    if ((item.Tag as string) == material) { _materialBox.SelectedItem = item; break; }
                }
                if (_materialBox.SelectedItem is null && _materialBox.Items.Count > 0) _materialBox.SelectedIndex = 0;
            }
            var opacity = App.Settings?.Get(App.KeyOpacity, 0.95) ?? 0.95;
            if (_opacitySlider is not null)
            {
                _opacitySlider.Value = opacity;
                if (_opacityValue is not null) _opacityValue.Text = $"{opacity:0.00}";
            }
        }
        finally { _syncing = false; }
    }

    private void ApplyNow(string what)
    {
        try
        {
            App.ApplyLookToAllBoxes();
            if (_preview is not null) _preview.CornerRadius = new CornerRadius(App.CurrentCorner);
            App.Log($"外观页：{what}");
        }
        catch (Exception ex) { App.Log("应用外观失败：" + ex.Message); }
    }
}
