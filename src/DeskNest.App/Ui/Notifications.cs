using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskNest.App.Ui;

/// <summary>轻量提示：不弹模式对话框，右下角浮出一条 4 秒自动消失的卡片。</summary>
public static class Notifications
{
    private static Window? _host;
    private static readonly List<Window> _active = new();

    public static void Toast(string message, int seconds = 4, Action? action = null, string actionText = "撤销")
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;
            app.Dispatcher.Invoke(() =>
            {
                var panel = UiKit.Col(UiKit.Text(message, 13));
                if (action is not null) panel.Children.Add(UiKit.Btn(actionText, () => action(), true));
                var border = new Border
                {
                    Background = Theme.Current.CardBrush,
                    BorderBrush = Theme.Current.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    Effect = UiKit.SoftShadow(),
                    Child = panel,
                    Opacity = 0.98
                };

                var w = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Content = border
                };

                var area = SystemParameters.WorkArea;
                w.Left = area.Right - 380;
                w.Top = area.Bottom - 90 - _active.Count * 70;
                w.Show();
                _active.Add(w);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try { w.Close(); } catch { }
                    _active.Remove(w);
                };
                timer.Start();
            });
        }
        catch { }
    }
}
