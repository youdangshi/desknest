using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskNest.WinUI.Pages;

/// <summary>
/// 页面内统一的小组件：卡片、标题、统计块、信息行、操作行。
/// 目的只有一个——5 个页面长得一样、间距一样，别再每人一套边距。
/// </summary>
internal static class PageKit
{
    private static readonly SolidColorBrush Transparent = new(Microsoft.UI.Colors.Transparent);

    /// <summary>
    /// 按"当前实际主题"取主题资源笔刷。
    /// WinUI 的 ThemeResource 只在 XAML 里生效；代码里 `Application.Current.Resources[key]`
    /// 拿到的是**应用当前主题**的固定值，切深色后不会变——深色模式白字压浅底就是这么来的。
    /// 所以这里手动查 ThemeDictionaries（Dark 走 Default，Light 走 Light）。
    /// </summary>
    /// <summary>
    /// 按"当前实际主题"取我们自己的主题笔刷（App.xaml 的 Dn* 字典）。
    /// WinUI 的 ThemeResource 只在 XAML 里生效；代码里 `Application.Current.Resources[key]`
    /// 拿到的是固定值，切深色后不会变——深色模式白字压浅底就是这么来的。
    /// </summary>
    private static Brush Res(string key, ElementTheme theme)
    {
        var dictName = theme == ElementTheme.Light ? "Light" : "Default";
        try
        {
            if (Application.Current.Resources.ThemeDictionaries.TryGetValue(dictName, out var dictObj)
                && dictObj is ResourceDictionary dict
                && dict.TryGetValue(key, out var v) && v is Brush b)
            {
                return b;
            }
            // 兜底：键名对不上时退回应用级资源（至少不会空白）
            if (Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush fb) return fb;
            App.Log($"PageKit: 主题字典缺 {key}（{dictName}）");
        }
        catch (Exception ex) { App.Log("PageKit.Res 失败 " + key + "：" + ex.Message); }
        return Transparent;
    }

    /// <summary>按元素当前主题取一支笔刷（悬停/临时底色这类一次性用）。</summary>
    public static Brush ThemedBrush(FrameworkElement element, string key) => Res(key, element.ActualTheme);

    /// <summary>
    /// 给元素设主题笔刷，并在它所在主题变化时自动重算（切深浅色不用重建页面）。
    /// </summary>
    public static void SetThemedBrush(FrameworkElement element, string key, Action<Brush> apply)
    {
        apply(Res(key, element.ActualTheme));
        element.ActualThemeChanged += (s, _) => apply(Res(key, s.ActualTheme));
    }

    /// <summary>标准卡片：浅底 + 细边框 + 圆角。</summary>
    public static Border Card(string? title, UIElement content, string? subtitle = null)
    {
        var stack = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(title))
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        if (!string.IsNullOrEmpty(subtitle))
            stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(content);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Child = stack
        };
        SetThemedBrush(border, "DnCardBg", b => border.Background = b);
        SetThemedBrush(border, "DnCardStroke", b => border.BorderBrush = b);
        return border;
    }

    /// <summary>统计块：大数字 + 说明。</summary>
    public static FrameworkElement Stat(string value, string label)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Opacity = 0.65 });
        return stack;
    }

    /// <summary>一行统计块（自动等宽）。</summary>
    public static FrameworkElement StatRow(params FrameworkElement[] stats)
    {
        var grid = new Grid { ColumnSpacing = 24 };
        for (var i = 0; i < stats.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(stats[i], i);
            grid.Children.Add(stats[i]);
        }
        return grid;
    }

    /// <summary>信息行：左边标签，右边值（值可选中复制）。</summary>
    public static UIElement InfoRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var l = new TextBlock { Text = label, FontSize = 12.5, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(l);

        var v = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    /// <summary>操作行：左侧标题/说明，右侧控件（通常是按钮，也可以是一排按钮）。</summary>
    public static UIElement ActionRow(string title, string? subtitle, FrameworkElement rightControl)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12.5 });
        if (!string.IsNullOrEmpty(subtitle))
            stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(stack);

        rightControl.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(rightControl, 1);
        grid.Children.Add(rightControl);
        return grid;
    }

    /// <summary>分隔线（卡片之间的视觉分组）。</summary>
    public static Border Divider()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
        SetThemedBrush(b, "DnDivider", brush => b.Background = brush);
        return b;
    }

    public static Button Btn(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), MinWidth = 0 };
        if (primary) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += onClick;
        return b;
    }

    public static TextBlock Hint(string text) =>
        new() { Text = text, FontSize = 11, Opacity = 0.55, TextWrapping = TextWrapping.Wrap };

    /// <summary>小节标签（设置项上方的小字）。</summary>
    public static TextBlock Label(string text) =>
        new() { Text = text, FontSize = 12.5, Opacity = 0.72 };

    public static TextBlock PageTitle(string text) =>
        new() { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold };

    public static TextBlock PageIntro(string text) =>
        new() { Text = text, Opacity = 0.72, FontSize = 13, TextWrapping = TextWrapping.Wrap };

    public static StackPanel Column(double spacing = 16) => new() { Spacing = spacing };
}
