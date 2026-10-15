using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Native.Win32;

namespace DeskNest.App.Pages;

/// <summary>
/// 多显示器：先给一张"显示器示意图"（按真实相对位置缩放画出来），再逐屏给出
/// 当前盒子/组件数量与"全部移到此屏"。多屏用户最痛的两件事就是"盒子跑到看不见的地方"
/// 和"我不知道哪个盒子在哪块屏上"，这一页就是解决它们。
/// </summary>
public static class DisplaysPage
{
    public static UIElement Build(MainWindow host)
    {
        var monitors = Monitors.All();
        var col = UiKit.Col();
        col.Children.Add(UiKit.PageHeader("多显示器",
            "收纳盒跟着显示器走。换屏、拔掉副屏、改分辨率之后，可以用这里的按钮把窗口拉回看得见的地方。"));

        if (monitors.Count == 0)
        {
            col.Children.Add(UiKit.Banner(BannerKind.Warning, "没有检测到显示器信息，暂时无法管理屏幕布局。"));
            return col;
        }

        if (monitors.Count == 1)
        {
            col.Children.Add(UiKit.Banner(BannerKind.Info,
                $"当前只检测到 1 块显示器（{monitors[0].DeviceName}，{monitors[0].Resolution}）。接上第二块显示器后回到这一页即可看到布局。"));
        }

        col.Children.Add(UiKit.Section("屏幕布局", BuildMap(monitors)));

        var rows = new List<UIElement>();
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var index = i;
            var boxCount = CountOn(m);
            var label = (m.IsPrimary ? "主屏" : "副屏") + $" {Number(index + 1)}";
            var subtitle = $"{m.DeviceName} · {m.Resolution} · 工作区 {m.WorkX},{m.WorkY} · 当前 {boxCount} 个窗口";

            rows.Add(UiKit.Row(Icons.Grid, label, subtitle,
                UiKit.Row(
                    UiKit.Btn("全部移到此屏", () => MoveAllTo(m, host)),
                    UiKit.Btn("是这块屏？测试", () => Flash(m)))));
        }
        col.Children.Add(UiKit.Section("这块屏上有多少东西", rows.ToArray()));

        col.Children.Add(UiKit.Section("摆放与防丢",
            UiKit.ToggleRow(Icons.Pin, "新建收纳盒放在鼠标所在的屏幕",
                "双屏下最省事：在哪个屏幕上点新建，盒子就出现在那个屏幕",
                AppServices.Settings.Get("displays.newBoxOnCursor", true),
                v => AppServices.Settings.Set("displays.newBoxOnCursor", v)),
            UiKit.ToggleRow(Icons.Eye, "启动时把跑出屏幕的窗口拉回来",
                "拔掉副屏或改分辨率后，窗口可能停在看不见的坐标上；打开后每次启动自动检查",
                AppServices.Settings.Get("displays.rescueOffscreen", true),
                v => AppServices.Settings.Set("displays.rescueOffscreen", v)),
            UiKit.ActionRow(Icons.Refresh, "立即检查一次", "把所有不在任何显示器工作区内的盒子/组件搬回主屏",
                "检查", () =>
                {
                    var n = (Application.Current as App)?.Desktop.RescueOffscreen() ?? 0;
                    Ui.Notifications.Toast(n == 0 ? "没有发现跑出屏幕的窗口" : $"已拉回 {n} 个窗口", 4);
                    host.RefreshCurrent();
                })));

        return col;
    }

    private static string Number(int n) => n switch { 1 => "①", 2 => "②", 3 => "③", 4 => "④", 5 => "⑤", 6 => "⑥", _ => n.ToString() };

    /// <summary>按真实相对位置缩放画示意图：多屏是横向还是纵向拼接，一眼就能对上现实。</summary>
    private static UIElement BuildMap(IReadOnlyList<MonitorInfo> monitors)
    {
        const double maxWidth = 620;
        const double maxHeight = 240;
        const double gap = 6;

        var left = monitors.Min(m => m.X);
        var top = monitors.Min(m => m.Y);
        var right = monitors.Max(m => m.Right);
        var bottom = monitors.Max(m => m.Bottom);
        var totalW = Math.Max(1, right - left);
        var totalH = Math.Max(1, bottom - top);

        var scale = Math.Min((maxWidth - gap * 2) / totalW, (maxHeight - gap * 2) / totalH);
        var canvasW = totalW * scale + gap * 2;
        var canvasH = totalH * scale + gap * 2;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, HorizontalAlignment = HorizontalAlignment.Left };
        var tokens = Theme.Current;

        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var w = m.Width * scale;
            var h = m.Height * scale;
            var x = (m.X - left) * scale + gap;
            var y = (m.Y - top) * scale + gap;

            var screen = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(m.IsPrimary ? 2 : 1),
                BorderBrush = m.IsPrimary ? tokens.AccentBrush : tokens.BorderBrush,
                Background = m.IsPrimary
                    ? new SolidColorBrush(tokens.Accent) { Opacity = tokens.IsDark ? 0.22 : 0.14 }
                    : new SolidColorBrush(tokens.Foreground) { Opacity = 0.06 },
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Number(i + 1),
                            FontSize = Math.Min(22, Math.Max(14, h / 5)),
                            Foreground = m.IsPrimary ? tokens.AccentBrush : tokens.ForegroundBrush,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = m.IsPrimary ? "主屏 " + m.Resolution : m.Resolution,
                            FontSize = 11,
                            Opacity = 0.75,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.NoWrap
                        }
                    }
                }
            };
            Canvas.SetLeft(screen, x);
            Canvas.SetTop(screen, y);
            ToolTipService.SetToolTip(screen, $"{m.DeviceName}\n{m.Resolution}\n左上角 {m.X},{m.Y}");
            canvas.Children.Add(screen);
        }

        return new Border { Padding = new Thickness(8, 4, 8, 12), Child = canvas };
    }

    /// <summary>数这块屏上有几个收纳盒/组件窗口（按窗口中心点归属，跨屏时算在中心点所在的那块）。</summary>
    private static int CountOn(MonitorInfo m)
    {
        var desktop = (Application.Current as App)?.Desktop;
        if (desktop is null) return 0;
        var count = 0;
        foreach (var win in desktop.Boxes.Values)
        {
            if (!win.IsVisible) continue;
            if (CenterIn(win, m)) count++;
        }
        foreach (var win in desktop.Widgets.Values)
        {
            if (!win.IsVisible) continue;
            if (CenterIn(win, m)) count++;
        }
        return count;
    }

    private static bool CenterIn(Window win, MonitorInfo m)
    {
        var cx = win.Left + win.Width / 2;
        var cy = win.Top + win.Height / 2;
        return cx >= m.X && cx < m.Right && cy >= m.Y && cy < m.Bottom;
    }

    private static void MoveAllTo(MonitorInfo m, MainWindow host)
    {
        var desktop = (Application.Current as App)?.Desktop;
        if (desktop is null) return;
        var n = desktop.ArrangeOn(m);
        Ui.Notifications.Toast(n == 0 ? "桌面上没有可见的收纳盒或组件" : $"已把 {n} 个窗口移到 {m.DeviceName}", 4);
        host.RefreshCurrent();
    }

    /// <summary>让目标显示器"闪一下"，方便用户确认① ② ③ 到底对应哪块屏。</summary>
    private static void Flash(MonitorInfo m)
    {
        try
        {
            var w = Math.Min(520, m.Width - 80);
            var h = Math.Min(320, m.Height - 80);
            var hint = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Theme.Current.Accent) { Opacity = 0.28 },
                ShowInTaskbar = false,
                Topmost = true,
                Width = w,
                Height = h,
                Left = m.X + (m.Width - w) / 2.0,
                Top = m.Y + (m.Height - h) / 2.0,
                Content = new TextBlock
                {
                    Text = "这是 " + m.DeviceName,
                    FontSize = 26,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            hint.Show();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
            timer.Tick += (_, _) => { timer.Stop(); try { hint.Close(); } catch { } };
            timer.Start();
        }
        catch (Exception ex) { Log.Warn("闪屏提示失败：" + ex.Message); }
    }
}
