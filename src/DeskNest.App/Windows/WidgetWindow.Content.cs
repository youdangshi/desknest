using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

namespace DeskNest.App.Windows;

public sealed partial class WidgetWindow
{
    private void BuildContent()
    {
        _content.Children.Clear();
        switch (_widget.Kind)
        {
            case WidgetKind.Todo: BuildTodo(); break;
            case WidgetKind.QuickNote: BuildNote(); break;
            case WidgetKind.Weather: BuildWeather(); break;
            case WidgetKind.Music: BuildMusic(); break;
            case WidgetKind.SystemMonitor: BuildSystemMonitor(); break;
            case WidgetKind.Glance: BuildGlance(); break;
            default: BuildFileWidget(); break;
        }
    }

    // ---------------- 待办 ----------------
    private void BuildTodo()
    {
        _content.Children.Clear();
        var input = new TextBox { FontSize = 12.5, Margin = new Thickness(0, 4, 0, 6) };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var title = input.Text.Trim();
            if (title.Length == 0) return;
            AppServices.Repo.Upsert(new TodoRecord { Title = title, CreatedAt = DateTimeOffset.Now });
            input.Text = "";
            BuildTodo();
        };
        _content.Children.Add(input);

        var todos = AppServices.Repo.Todos().Where(t => !t.IsDone).ToList();
        var done = AppServices.Repo.Todos().Count(t => t.IsDone);
        foreach (var todo in todos)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            check.Checked += (_, _) =>
            {
                todo.IsDone = true;
                todo.CompletedAt = DateTimeOffset.Now;
                AppServices.Repo.Upsert(todo);
                BuildTodo();
            };
            var text = new TextBlock
            {
                Text = todo.Title,
                FontSize = 12.5,
                Foreground = Theme.Desktop.ForegroundBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(80, Width - 90)
            };
            var prio = UiKit.Text(todo.Priority switch { 2 => "高", 0 => "低", _ => "中" }, 10, secondary: true);
            prio.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(check);
            row.Children.Add(text);
            row.Children.Add(prio);
            row.MouseRightButtonUp += (_, e) =>
            {
                var menu = new ContextMenu();
                menu.Items.Add(MenuAction("设为高优先级", () => { todo.Priority = 2; AppServices.Repo.Upsert(todo); BuildTodo(); }));
                menu.Items.Add(MenuAction("设为中优先级", () => { todo.Priority = 1; AppServices.Repo.Upsert(todo); BuildTodo(); }));
                menu.Items.Add(MenuAction("设为低优先级", () => { todo.Priority = 0; AppServices.Repo.Upsert(todo); BuildTodo(); }));
                menu.Items.Add(MenuAction("提醒（5 分钟后）", () =>
                {
                    todo.DueAt = DateTimeOffset.Now.AddMinutes(5);
                    todo.RemindMinutesBefore = 0;
                    AppServices.Repo.Upsert(todo);
                    Ui.Notifications.Toast("已设置 5 分钟后提醒");
                }));
                menu.Items.Add(MenuAction("删除", () => { AppServices.Repo.DeleteTodo(todo.Id); BuildTodo(); }));
                menu.IsOpen = true;
            };
            _content.Children.Add(row);
        }
        _status.Text = todos.Count == 0 ? "今天想完成什么？" : $"未完成 {todos.Count} · 已完成 {done}";
    }

    // ---------------- 随记 ----------------
    private void BuildNote()
    {
        _content.Children.Clear();
        var notes = AppServices.Repo.Notes().Take(20).ToList();
        var addBtn = UiKit.Btn("＋ 新建随记", () =>
        {
            var note = new NoteRecord { Title = "新随记", Body = "", UpdatedAt = DateTimeOffset.Now };
            AppServices.Repo.Upsert(note);
            BuildNote();
        });
        _content.Children.Add(addBtn);
        foreach (var note in notes)
        {
            var border = new Border
            {
                Background = Theme.Desktop.CardBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.Desktop.BorderBrush
            };
            var stack = new StackPanel();
            stack.Children.Add(UiKit.Text(note.Title, 12.5, bold: true));
            var body = new TextBox
            {
                Text = note.Body,
                FontSize = 12,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 50,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Theme.Desktop.SecondaryBrush
            };
            body.LostFocus += (_, _) =>
            {
                if (note.Body == body.Text) return;
                note.Body = body.Text;
                note.UpdatedAt = DateTimeOffset.Now;
                AppServices.Repo.Upsert(note);
                _status.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
            };
            stack.Children.Add(body);
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(UiKit.Btn(note.IsPinned ? "取消固定" : "固定", () =>
            {
                note.IsPinned = !note.IsPinned;
                AppServices.Repo.Upsert(note);
                BuildNote();
            }));
            tools.Children.Add(UiKit.Btn("删除", () => { AppServices.Repo.DeleteNote(note.Id); BuildNote(); }));
            stack.Children.Add(tools);
            border.Child = stack;
            _content.Children.Add(border);
        }
        _status.Text = notes.Count == 0 ? "随手记点什么（Markdown 可编辑）" : $"{notes.Count} 条随记";
    }

    // ---------------- 天气 ----------------
    private void BuildWeather()
    {
        _content.Children.Clear();
        var city = AppServices.Settings.Get("weather.city", "未设置");
        _content.Children.Add(UiKit.Text("☁  天气", 22));
        _content.Children.Add(UiKit.Text($"城市：{city}", 12.5, secondary: true));
        _content.Children.Add(UiKit.Text("数据源可选：Open-Meteo（免费、无需 Key）", 11.5, secondary: true));
        _content.Children.Add(UiKit.Row(
            UiKit.Btn("刷新", async () => await RefreshWeatherAsync()),
            UiKit.Btn("设置城市", () => SetCity())));
        _content.Children.Add(UiKit.Text(AppServices.Settings.Get("weather.summary", "还没有数据，点「刷新」获取"), 12));
        _status.Text = AppServices.Settings.Get("weather.updated", "尚未更新");
    }

    private void SetCity()
    {
        var input = UiKit.Input(AppServices.Settings.Get("weather.city", "深圳"), t => AppServices.Settings.Set("weather.city", t), 200);
        var win = new Window
        {
            Title = "天气城市",
            Width = 320,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Desktop.BackgroundBrush,
            Content = UiKit.Card(null, UiKit.Col(UiKit.Text("城市名称（中文即可）", 11.5, secondary: true), input,
                UiKit.Btn("保存并刷新", () => { }, true)))
        };
        var btn = (Button)((StackPanel)((Border)win.Content).Child).Children[2];
        btn.Click += async (_, _) => { win.Close(); await RefreshWeatherAsync(); };
        win.ShowDialog();
    }

    private async Task RefreshWeatherAsync()
    {
        try
        {
            var city = AppServices.Settings.Get("weather.city", "深圳");
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var geo = await http.GetStringAsync(
                "https://geocoding-api.open-meteo.com/v1/search?count=1&language=zh&name=" + Uri.EscapeDataString(city));
            using var doc = System.Text.Json.JsonDocument.Parse(geo);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                _status.Text = "找不到城市，换个名字试试";
                return;
            }
            var lat = results[0].GetProperty("latitude").GetDouble();
            var lon = results[0].GetProperty("longitude").GetDouble();
            var forecast = await http.GetStringAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=3");
            using var fdoc = System.Text.Json.JsonDocument.Parse(forecast);
            var cur = fdoc.RootElement.GetProperty("current");
            var temp = cur.GetProperty("temperature_2m").GetDouble();
            var wind = cur.GetProperty("wind_speed_10m").GetDouble();
            var daily = fdoc.RootElement.GetProperty("daily");
            var summary = $"{temp:0.#}°C · 风速 {wind:0.#} km/h\n";
            var maxs = daily.GetProperty("temperature_2m_max");
            var mins = daily.GetProperty("temperature_2m_min");
            var times = daily.GetProperty("time");
            for (var i = 0; i < Math.Min(3, times.GetArrayLength()); i++)
                summary += $"{times[i].GetString()}  {mins[i].GetDouble():0.#}~{maxs[i].GetDouble():0.#}°C\n";
            AppServices.Settings.Set("weather.summary", summary);
            AppServices.Settings.Set("weather.updated", "更新于 " + DateTime.Now.ToString("MM-dd HH:mm"));
            BuildWeather();
        }
        catch (Exception ex)
        {
            _status.Text = "更新失败：" + ex.Message;
        }
    }

    // ---------------- 音乐 ----------------
    private void BuildMusic()
    {
        _content.Children.Clear();
        _content.Children.Add(UiKit.Text("♫  媒体控制", 20));
        _content.Children.Add(UiKit.Text("按键控制当前播放器（不需要任何权限）", 11.5, secondary: true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        row.Children.Add(UiKit.Btn("⏮", () => MediaKey(0xB1)));
        row.Children.Add(UiKit.Btn("⏯", () => MediaKey(0xB3)));
        row.Children.Add(UiKit.Btn("⏭", () => MediaKey(0xB0)));
        _content.Children.Add(row);
        _content.Children.Add(UiKit.Text("提示：想显示封面与进度，可后续接入系统媒体会话。", 11, secondary: true));
        _status.Text = "媒体键模式";
    }

    private static void MediaKey(int vk)
    {
        try
        {
            Native.Win32.MediaKeys.Send(vk);
        }
        catch (Exception ex) { Log.Warn("媒体键失败：" + ex.Message); }
    }

    // ---------------- 系统监视 ----------------
    private readonly List<TextBlock> _perfLines = new();

    private void BuildSystemMonitor()
    {
        _content.Children.Clear();
        _perfLines.Clear();
        _perfRows.Clear();

        // 评审结论（oiloil-ui-ux-guide review 模式）：
        // P0 数字没有参照系、没有异常突出；P1 「布局」设置形同虚设、数值不对齐、GPU 缺数据时是开发者话术、
        // 全宽按钮在小盒子里占四分之一高度。下面按这两级问题重做。
        var layout = AppServices.Settings.Get("perf.layout", "紧凑横条");
        // 双击组件本体直接打开性能面板（原来是一个占满整行的大按钮，在小盒子里太浪费空间）
        _content.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (App.Current is App app) app.ShowMain("perf");
            e.Handled = true;
        };
        _content.ToolTip = "双击打开性能监视页";
        var shown = PerfMetrics().Where(m => AppServices.Settings.Get(m.SettingKey, true)).ToList();
        if (shown.Count == 0)
        {
            _content.Children.Add(UiKit.Text("没有勾选任何显示项（设置 → 性能监视）", 12, secondary: true));
            return;
        }

        if (layout == "纯文字")
        {
            foreach (var metric in shown)
            {
                var line = UiKit.Text("", 12);
                _perfLines.Add(line);
                _content.Children.Add(line);
            }
        }
        else if (layout == "竖条")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var metric in shown) row.Children.Add(BuildColumn(metric));
            _content.Children.Add(row);
        }
        else if (layout == "网格")
        {
            var wrap = new System.Windows.Controls.WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var metric in shown) wrap.Children.Add(BuildTile(metric));
            _content.Children.Add(wrap);
        }
        else
        {
            foreach (var metric in shown) _content.Children.Add(BuildBarRow(metric));
        }

        UpdateSystemMonitor();
    }

    private sealed record PerfMetric(string Key, string Label, string SettingKey);

    /// <summary>四个指标的定义（顺序固定：CPU、内存、网络、GPU）。</summary>
    private static List<PerfMetric> PerfMetrics() => new()
    {
        new PerfMetric("cpu", "CPU", "perf.show.cpu"),
        new PerfMetric("mem", "内存", "perf.show.mem"),
        new PerfMetric("net", "网络", "perf.show.net"),
        new PerfMetric("gpu", "GPU", "perf.show.gpu")
    };

    private readonly Dictionary<string, (Border Fill, Border Track, TextBlock Value, TextBlock? Sub)> _perfRows = new();

    /// <summary>一行：名称 + 圆角进度条 + 右对齐数值（进度条颜色随占用率变化，异常一眼可见）。</summary>
    private UIElement BuildBarRow(PerfMetric metric)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = UiKit.Text(metric.Label, 12, secondary: true);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);

        var track = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = Theme.Desktop.BorderBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var fill = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Theme.Desktop.AccentBrush
        };
        var barHost = new Grid();
        barHost.Children.Add(track);
        barHost.Children.Add(fill);
        barHost.SizeChanged += (_, e) => fill.Width = Math.Max(3, e.NewSize.Width * _perfFillRatio.GetValueOrDefault(metric.Key, 0));
        Grid.SetColumn(barHost, 1);

        var value = UiKit.Text("—", 12);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.TextAlignment = TextAlignment.Right;
        value.MinWidth = 46;
        value.Margin = new Thickness(Tokens.Space8, 0, 0, 0);
        Grid.SetColumn(value, 2);

        grid.Children.Add(label);
        grid.Children.Add(barHost);
        grid.Children.Add(value);
        _perfRows[metric.Key] = (fill, track, value, null);
        _perfBarHosts[metric.Key] = barHost;
        return grid;
    }

    private readonly Dictionary<string, Grid> _perfBarHosts = new();
    private readonly Dictionary<string, double> _perfFillRatio = new();

    /// <summary>一列：竖向柱 + 数值 + 名称。</summary>
    private UIElement BuildColumn(PerfMetric metric)
    {
        var col = new StackPanel { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom };
        var value = UiKit.Text("—", 12, bold: true);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        var track = new Border
        {
            Width = 18,
            Height = 90,
            CornerRadius = new CornerRadius(9),
            Background = Theme.Desktop.BorderBrush,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var fill = new Border
        {
            Width = 18,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Theme.Desktop.AccentBrush
        };
        var barHost = new Grid { Height = 90 };
        barHost.Children.Add(track);
        barHost.Children.Add(fill);
        col.Children.Add(value);
        col.Children.Add(barHost);
        col.Children.Add(UiKit.Text(metric.Label, 11.5, secondary: true));
        _perfRows[metric.Key] = (fill, track, value, null);
        _perfBarHosts[metric.Key] = barHost;
        return col;
    }

    /// <summary>一格：小卡片（名称 + 数值 + 细条）。</summary>
    private UIElement BuildTile(PerfMetric metric)
    {
        var card = new Border
        {
            Width = 104,
            Margin = new Thickness(4),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Desktop.BorderBrush,
            Background = Theme.Desktop.CardBrush
        };
        var stack = new StackPanel();
        stack.Children.Add(UiKit.Text(metric.Label, 11.5, secondary: true));
        var value = UiKit.Text("—", 16, bold: true);
        stack.Children.Add(value);
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Desktop.BorderBrush, Margin = new Thickness(0, 6, 0, 0) };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.Desktop.AccentBrush };
        var barHost = new Grid();
        barHost.Children.Add(track);
        barHost.Children.Add(fill);
        barHost.SizeChanged += (_, e) => fill.Width = Math.Max(2, e.NewSize.Width * _perfFillRatio.GetValueOrDefault(metric.Key, 0));
        stack.Children.Add(barHost);
        card.Child = stack;
        _perfRows[metric.Key] = (fill, track, value, null);
        _perfBarHosts[metric.Key] = barHost;
        return card;
    }

    private void UpdateSystemMonitor()
    {
        var s = AppServices.Perf.Latest;
        if (s is null) return;

        // 纯文字布局沿用老的 _perfLines
        if (_perfLines.Count >= 4 && _perfRows.Count == 0)
        {
            _perfLines[0].Text = $"CPU    {s.CpuPercent:0.#}%";
            _perfLines[1].Text = $"内存   {s.MemoryPercent:0.#}%  ({UiKit.Fmt(s.MemoryUsedBytes)} / {UiKit.Fmt(s.MemoryTotalBytes)})";
            _perfLines[2].Text = $"网络   ↑{FormatRate(s.NetUpBytesPerSec)}  ↓{FormatRate(s.NetDownBytesPerSec)}";
            _perfLines[3].Text = s.GpuPercent is { } g0 ? $"GPU    {g0}%" : "GPU    —";
            return;
        }

        Set("cpu", s.CpuPercent, $"{s.CpuPercent:0}%");
        Set("mem", s.MemoryPercent, $"{s.MemoryPercent:0}%");
        // 网络没有"百分比"这一说，用上下行吞吐里较大的那个做相对长度（对数刻度，避免被大流量压平）
        var throughput = new[] { s.NetUpBytesPerSec, s.NetDownBytesPerSec };
        var peak = Math.Max(throughput.Max(), 1);
        var netRatio = Math.Min(100, Math.Log10(peak + 1) / 6 * 100);   // 1 MB/s ≈ 100%
        // 网络上只有"多少流量"没有"占用率"，用 ↓ 明确方向，↑ 的数值放进提示里（行内放不下两组数）
        Set("net", netRatio, $"↓{FormatRate(s.NetDownBytesPerSec)}");
        if (_perfRows.TryGetValue("net", out var netRow))
            netRow.Value.ToolTip = $"下行 {FormatRate(s.NetDownBytesPerSec)}　上行 {FormatRate(s.NetUpBytesPerSec)}";
        Set("gpu", s.GpuPercent ?? 0, s.GpuPercent is { } gpu ? $"{gpu}%" : "—", s.GpuPercent is null ? 0 : null);

        void Set(string key, double percent, string text, double? forceRatio = null)
        {
            if (!_perfRows.TryGetValue(key, out var row)) return;
            var ratio = Math.Clamp((forceRatio ?? percent) / 100.0, 0, 1);
            _perfFillRatio[key] = ratio;
            row.Value.Text = text;
            System.Windows.Media.SolidColorBrush brush = percent switch
            {
                >= 85 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE2, 0x53, 0x53)),
                >= 60 => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x30)),
                _ => Theme.Desktop.AccentBrush as System.Windows.Media.SolidColorBrush
                     ?? new System.Windows.Media.SolidColorBrush(Theme.Desktop.Accent)
            };
            row.Fill.Background = brush;
            if (_perfBarHosts.TryGetValue(key, out var host))
            {
                var width = host.ActualWidth > 0 ? host.ActualWidth : 80;
                if (row.Fill.Width != width * ratio) row.Fill.Width = Math.Max(3, width * ratio);
            }
        }
    }

    private static string FormatRate(double bytesPerSec) => bytesPerSec switch
    {
        < 1024 => $"{bytesPerSec:0} B/s",
        < 1024 * 1024 => $"{bytesPerSec / 1024:0.#} KB/s",
        _ => $"{bytesPerSec / 1024 / 1024:0.##} MB/s"
    };

    // ---------------- 概览 ----------------
    private void BuildGlance()
    {
        _content.Children.Clear();
        _content.Children.Add(UiKit.Text(DateTime.Now.ToString("M月d日 dddd"), 15, bold: true));
        var openTodos = AppServices.Repo.Todos().Count(t => !t.IsDone);
        var boxes = AppServices.Repo.Boxes().Count;
        _content.Children.Add(UiKit.Text($"待办 {openTodos} 项 · 收纳盒 {boxes} 个", 12.5, secondary: true));
        _content.Children.Add(UiKit.Text($"今日已整理：{AppServices.Repo.Runs(50).Count(r => r.StartedAt.Date == DateTime.Today)} 次", 12, secondary: true));
        _content.Children.Add(UiKit.Row(
            UiKit.Btn("一键整理", () =>
            {
                if (App.Current is App app) app.ShowOrganizeWizard();
            }),
            UiKit.Btn("搜索", () =>
            {
                if (App.Current is App app) app.ShowSearch();
            })));
        _status.Text = "概览";
    }

    // ---------------- 文件组件 ----------------
    private void BuildFileWidget()
    {
        _content.Children.Clear();
        if (_widget.BoxId is { } boxId && AppServices.Repo.Box(boxId) is { } box)
        {
            _content.Children.Add(UiKit.Text(box.Name, 13, bold: true));
            var items = AppServices.Repo.Items(boxId, box.Kind == BoxKind.Mapped).Take(30).ToList();
            foreach (var item in items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand };
                row.Children.Add(new Image
                {
                    Width = 18,
                    Height = 18,
                    Source = IconCache.Get(item.StoredPath ?? item.SourcePath, item.Kind == ItemKind.Folder),
                    Stretch = Stretch.Uniform
                });
                row.Children.Add(new TextBlock
                {
                    Text = item.DisplayName,
                    FontSize = 12.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    Foreground = Theme.Desktop.ForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = Math.Max(80, Width - 80)
                });
                row.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount != 2) return;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.StoredPath ?? item.SourcePath)
                        { UseShellExecute = true });
                    }
                    catch (Exception ex) { Ui.Notifications.Toast("打开失败：" + ex.Message); }
                };
                _content.Children.Add(row);
            }
            if (items.Count == 0) _content.Children.Add(UiKit.Text("这个盒子还是空的", 12, secondary: true));
            _status.Text = $"{items.Count} 项";
            return;
        }
        _content.Children.Add(UiKit.Text("文件组件还没有绑定收纳盒", 12.5, secondary: true));
        _content.Children.Add(UiKit.Btn("去绑定", () =>
        {
            var box = AppServices.Repo.Boxes().FirstOrDefault();
            if (box is null) { Ui.Notifications.Toast("请先创建一个收纳盒"); return; }
            _widget.BoxId = box.Id;
            AppServices.Repo.Upsert(_widget);
            BuildContent();
        }));
    }
}
