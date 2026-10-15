using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;

namespace DeskNest.App.Ui;

public enum BannerKind { Info, Warning, Success }

/// <summary>
/// 从 DeskBox 截图里学到的几种"信息条/可展开行/面包屑"，补齐我们的设置页观感：
/// - Banner：黄色警示条（如"当前在 C 盘，整理文件不会释放 C 盘空间"）、蓝色说明条
/// - Expander：一行里含标题 + 开关 + 折叠箭头，展开后露出子行（如"开机自动启动 → 静默启动"）
/// - Breadcrumb：下钻页顶部"常规 › 性能与资源"
/// - HelpTitle：标题后的「?」帮助点
/// </summary>
public static partial class UiKit
{
    /// <summary>信息条：改用 WPF-UI 的 InfoBar（自带图标、配色、圆角，跟随主题）。</summary>
    public static UIElement Banner(BannerKind kind, string text, string? title = null)
    {
        return new InfoBar
        {
            Severity = kind switch
            {
                BannerKind.Warning => InfoBarSeverity.Warning,
                BannerKind.Success => InfoBarSeverity.Success,
                _ => InfoBarSeverity.Informational
            },
            Title = title ?? "",
            Message = text,
            IsOpen = true,
            IsClosable = false,
            Margin = new Thickness(0, 0, 0, Tokens.Space8)
        };
    }

    /// <summary>标题 + 帮助点（悬停显示说明）。</summary>
    public static StackPanel TitleWithHelp(string title, string? help)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = Tokens.FontSettingTitle,
            Foreground = Theme.Current.ForegroundBrush
        });
        if (!string.IsNullOrWhiteSpace(help))
        {
        var q = new SymbolIcon { Symbol = SymbolRegular.QuestionCircle24, FontSize = 14, Opacity = 0.7 };
            q.Margin = new Thickness(Tokens.Space6, 0, 0, 0);
            q.ToolTip = help;
            panel.Children.Add(q);
        }
        return panel;
    }

    /// <summary>
    /// 可展开行：标题 + 右侧控件（可选）+ 折叠箭头；展开后显示 childRows。
    /// 用于"开机自动启动 → 静默启动""全局快捷键 → 子项"这种层级。
    /// </summary>
    public static UIElement ExpanderRow(string glyph, string title, string? subtitle, UIElement? rightControl,
        params UIElement[] childRows)
    {
        var host = new StackPanel();

        var chevron = new SymbolIcon { Symbol = SymbolRegular.ChevronDown24, FontSize = 12, Opacity = 0.7 };
        chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        chevron.RenderTransform = new RotateTransform(0);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (rightControl is not null)
        {
            if (rightControl is FrameworkElement rightElement)
                rightElement.Margin = new Thickness(0, 0, Tokens.Space8, 0);
            right.Children.Add(rightControl);
        }
        right.Children.Add(chevron);

        var childrenHost = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, Tokens.Space4, 0, Tokens.Space8) };
        foreach (var row in childRows) childrenHost.Children.Add(row);

        var header = Row(glyph, title, subtitle, right, () =>
        {
            var open = childrenHost.Visibility != Visibility.Visible;
            childrenHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.RenderTransform = new RotateTransform(open ? 180 : 0);
        });

        host.Children.Add(header);
        host.Children.Add(childrenHost);
        return host;
    }

    /// <summary>面包屑：返回箭头 + "常规 › 性能与资源"。</summary>
    public static StackPanel Breadcrumb(string parent, string current, Action onBack)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(Tokens.Space2, 0, 0, Tokens.Space12) };

        var back = new Button
        {
            Content = Icons.Vector("arrow_minimize", 14, Theme.Current.SecondaryColor),
            Margin = new Thickness(0, 0, Tokens.Space8, 0),
            ToolTip = "返回"
        };
        back.Style = ControlStyles.IconButton();
        back.Click += (_, _) => onBack();

        panel.Children.Add(back);
        panel.Children.Add(new TextBlock
        {
            Text = parent,
            FontSize = Tokens.FontSubsectionTitle,
            Foreground = Theme.Current.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "  ›  ",
            FontSize = Tokens.FontSubsectionTitle,
            Foreground = Theme.Current.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = current,
            FontSize = Tokens.FontSubsectionTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Current.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

}
