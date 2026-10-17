using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskNest.App.Ui;

/// <summary>
/// 轻量提示：右下角浮出的紧凑卡片，4 秒自动消失。
/// 设计约束（2026-10-17 重做）：
/// ① **尺寸跟着内容走**，绝不出现一大块空白（以前用 SizeToContent 仍被撑成 500×300）；
/// ② 最多同时 3 条，从下往上叠，超出就顶掉最旧的；
/// ③ 三种语气（成功/提示/警告）用图标颜色区分，但都不刺眼；
/// ④ 带「关闭」，有动作（如撤销）时是紧凑文字按钮，不是大蓝块。
/// </summary>
public static class Notifications
{
    private const int MaxToasts = 3;
    private const double CardMaxWidth = 340;
    private const double Gap = 8;

    private static readonly List<Window> _active = new();

    public enum Kind { Info, Success, Warning }

    public static void Toast(string message, int seconds = 4, Action? action = null, string actionText = "撤销")
        => Show(message, Kind.Info, seconds, action, actionText);

    public static void Success(string message, int seconds = 3) => Show(message, Kind.Success, seconds, null, null);
    public static void Warn(string message, int seconds = 6) => Show(message, Kind.Warning, seconds, null, null);

    private static void Show(string message, Kind kind, int seconds, Action? action, string? actionText)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;
            app.Dispatcher.Invoke(() =>
            {
                var t = Theme.Current;
                var (glyph, tint) = kind switch
                {
                    Kind.Success => ("\uE73E", Color.FromRgb(0x3F, 0xB9, 0x50)),   // 勾
                    Kind.Warning => ("\uE7BA", Color.FromRgb(0xE0, 0xA0, 0x30)),   // 叹号
                    _ => ("\uE946", Color.FromRgb(0x4C, 0x8B, 0xF5))              // 信息
                };

                var icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(tint),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };

                var text = new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    Foreground = t.ForegroundBrush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 250,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(icon);
                row.Children.Add(text);

                if (action is not null && !string.IsNullOrEmpty(actionText))
                {
                    var link = new TextBlock
                    {
                        Text = actionText,
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(tint),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 0, 0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    row.Children.Add(link);
                    var act = action;
                    link.MouseLeftButtonUp += (_, _) => { try { act(); } catch { } };
                }

                var close = new TextBlock
                {
                    Text = "\uE8BB",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Foreground = t.TertiaryBrush,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "关闭"
                };

                var host = new StackPanel { Orientation = Orientation.Horizontal };
                host.Children.Add(row);
                host.Children.Add(close);

                var card = new Border
                {
                    Background = t.CardBrush,
                    BorderBrush = t.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 11, 10, 11),
                    MaxWidth = CardMaxWidth,
                    Effect = UiKit.SoftShadow(),
                    Child = host,
                    Tag = "toast"
                };

                // 先量出真实尺寸，再决定窗口位置 —— 这样不会出现"卡片被撑大"或叠出屏幕
                card.Measure(new Size(CardMaxWidth, double.PositiveInfinity));
                var w = Math.Min(CardMaxWidth, card.DesiredSize.Width);
                var h = card.DesiredSize.Height;

                var win = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Width = w,
                    Height = h + 2,
                    Content = card,
                    Tag = "toast"
                };

                // 最多 3 条：多了就顶掉最早的那条
                while (_active.Count >= MaxToasts)
                {
                    var oldest = _active[0];
                    _active.RemoveAt(0);
                    try { oldest.Close(); } catch { }
                }

                var area = SystemParameters.WorkArea;
                var bottomStack = (h + Gap) * _active.Count;
                win.Left = Math.Max(area.Left + 12, area.Right - w - 16);
                win.Top = Math.Max(area.Top + 12, area.Bottom - h - 16 - bottomStack);

                win.Show();
                _active.Add(win);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try { win.Close(); } catch { }
                    _active.Remove(win);
                };
                timer.Start();

                close.MouseLeftButtonUp += (_, _) =>
                {
                    timer.Stop();
                    try { win.Close(); } catch { }
                    _active.Remove(win);
                };
            });
        }
        catch { }
    }
}
