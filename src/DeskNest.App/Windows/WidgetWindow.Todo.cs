using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Data;

namespace DeskNest.App.Windows;

/// <summary>
/// 待办组件：学 DeskBox 的两页结构——
/// ① 列表页：全部 / 今天 / 重要 / 已完成 四个页签 + 颜色筛选 + 「＋ 添加待办」+ 清除已完成；
/// ② 编辑页：标题、截止日期 / 提醒 / 重复 / 颜色 四个胶囊、步骤（子任务）、备注，顶部 返回 / 保存 / 收藏 / 删除。
/// </summary>
public sealed partial class WidgetWindow
{
    /// <summary>DeskBox 那排色块用的颜色（空字符串＝无颜色）。</summary>
    private static readonly (string Name, string Hex)[] TodoColors =
    {
        ("红", "#E25353"), ("橙", "#E0A030"), ("黄", "#E5C42E"), ("绿", "#3FB950"),
        ("青", "#2FA9A0"), ("蓝", "#4C8BF5"), ("紫", "#8B6BE0"), ("粉", "#E060A8")
    };

    private string _todoTab = "全部";
    private string _todoColorFilter = "";
    private TodoRecord? _editing;

    /// <summary>待办页的入口（列表页或编辑页）。</summary>
    private void BuildTodoPage()
    {
        _content.Children.Clear();
        if (_editing is not null) BuildTodoEditor(_editing);
        else BuildTodoList();
    }

    // ---------------- 列表页 ----------------

    private void BuildTodoList()
    {
        var all = AppServices.Repo.Todos().Where(t => !t.IsArchived).ToList();
        var today = all.Where(t => !t.IsDone && t.DueAt is { } due && due.LocalDateTime.Date <= DateTime.Today).ToList();
        var important = all.Where(t => !t.IsDone && (t.Priority >= 2 || t.IsStarred)).ToList();
        var done = all.Where(t => t.IsDone).ToList();

        // 四个页签（带数量），学 DeskBox 的排版
        var tabs = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        foreach (var _ in new[] { 0, 1, 2, 3 }) tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tabDefs = new (string Label, int Count)[]
        {
            ("全部", all.Count(t => !t.IsDone)), ("今天", today.Count), ("重要", important.Count), ("已完成", done.Count)
        };
        for (var i = 0; i < tabDefs.Length; i++)
        {
            var (label, count) = tabDefs[i];
            var active = _todoTab == label;
            var block = new StackPanel { Cursor = Cursors.Hand };
            block.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = active ? Theme.Desktop.ForegroundBrush : Theme.Desktop.SecondaryBrush,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
            });
            block.Children.Add(new TextBlock
            {
                Text = count.ToString(), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Theme.Desktop.TertiaryBrush
            });
            block.Children.Add(new Border
            {
                Height = 2, Margin = new Thickness(10, 4, 10, 0),
                CornerRadius = new CornerRadius(1),
                Background = active ? Theme.Desktop.AccentBrush : Brushes.Transparent
            });
            block.MouseLeftButtonUp += (_, _) => { _todoTab = label; BuildTodoPage(); };
            Grid.SetColumn(block, i);
            tabs.Children.Add(block);
        }
        _content.Children.Add(tabs);

        // 颜色筛选（点一下只看这个颜色，再点取消）
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (name, hex) in TodoColors)
        {
            var dot = new Border
            {
                Width = 14, Height = 10, CornerRadius = new CornerRadius(5),
                Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(_todoColorFilter == hex ? 2 : 0),
                BorderBrush = Theme.Desktop.ForegroundBrush,
                ToolTip = name
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _todoColorFilter = _todoColorFilter == hex ? "" : hex;
                BuildTodoPage();
            };
            colors.Children.Add(dot);
        }
        _content.Children.Add(colors);

        // ＋ 添加待办
        var addRow = new Border
        {
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 2, 0, 6),
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            Background = Theme.Desktop.CardBrush,
            BorderThickness = new Thickness(1), BorderBrush = Theme.Desktop.BorderBrush,
            Cursor = Cursors.Hand,
            Child = UiKit.Text("＋ 添加待办", 12.5)
        };
        addRow.MouseLeftButtonUp += (_, _) =>
        {
            _editing = new TodoRecord { Title = "", CreatedAt = DateTimeOffset.Now };
            BuildTodoPage();
        };
        _content.Children.Add(addRow);

        // 列表
        var list = _todoTab switch
        {
            "今天" => today,
            "重要" => important,
            "已完成" => done,
            _ => all.Where(t => !t.IsDone).ToList()
        };
        if (_todoColorFilter.Length > 0) list = list.Where(t => t.Color == _todoColorFilter).ToList();

        var scroll = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel();
        if (list.Count == 0)
            stack.Children.Add(UiKit.Text(_todoTab == "已完成" ? "还没有完成的待办" : "这里空着——点上面的「＋ 添加待办」", 12, secondary: true));
        foreach (var todo in list) stack.Children.Add(BuildTodoRow(todo));
        scroll.Content = stack;
        _content.Children.Add(scroll);

        if (done.Count > 0)
        {
            var clear = UiKit.Text("清除已完成", 11.5, secondary: true);
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Margin = new Thickness(0, 6, 2, 0);
            clear.Cursor = Cursors.Hand;
            clear.MouseLeftButtonUp += (_, _) =>
            {
                foreach (var todo in done) AppServices.Repo.DeleteTodo(todo.Id);
                Ui.Notifications.Toast($"已清除 {done.Count} 条已完成", 3);
                BuildTodoPage();
            };
            _content.Children.Add(clear);
        }
    }

    private UIElement BuildTodoRow(TodoRecord todo)
    {
        var row = new Border
        {
            Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            Background = Theme.Desktop.CardBrush,
            BorderThickness = new Thickness(1), BorderBrush = Theme.Desktop.BorderBrush
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 勾选
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 颜色点

        var check = new CheckBox { IsChecked = todo.IsDone, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        check.Click += (_, _) =>
        {
            todo.IsDone = check.IsChecked == true;
            todo.CompletedAt = todo.IsDone ? DateTimeOffset.Now : null;
            AppServices.Repo.Upsert(todo);
            BuildTodoPage();
        };
        Grid.SetColumn(check, 0);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = (todo.IsStarred ? "★ " : "") + todo.Title,
            FontSize = 12.5,
            Foreground = Theme.Desktop.ForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = todo.IsDone ? TextDecorations.Strikethrough : null
        });
        var meta = new List<string>();
        if (todo.DueAt is { } due) meta.Add("截止 " + due.LocalDateTime.ToString("MM-dd HH:mm"));
        if (todo.Repeat is not ("None" or "")) meta.Add("重复 " + todo.Repeat switch
        {
            "Daily" => "每天", "Weekly" => "每周", "Monthly" => "每月", "Lunar" => "农历", _ => todo.Repeat
        });
        var steps = ParseSteps(todo);
        if (steps.Count > 0) meta.Add($"步骤 {steps.Count(s => s.Done)}/{steps.Count}");
        if (meta.Count > 0)
            text.Children.Add(new TextBlock { Text = string.Join(" · ", meta), FontSize = 10.5, Foreground = Theme.Desktop.TertiaryBrush });
        var hit = new Border { Child = text, Background = Brushes.Transparent, Padding = new Thickness(2) };
        hit.MouseLeftButtonUp += (_, e) => { _editing = todo; BuildTodoPage(); e.Handled = true; };
        Grid.SetColumn(hit, 1);

        if (todo.Color.Length > 0)
        {
            var dot = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(todo.Color))
            };
            Grid.SetColumn(dot, 2);
            grid.Children.Add(dot);
        }
        grid.Children.Add(check);
        grid.Children.Add(hit);
        row.Child = grid;
        // 右键直接删
        var menu = new ContextMenu();
        menu.Items.Add(UiKit.Text("", 1));
        menu.Items.Clear();
        var del = new MenuItem { Header = "删除这条待办" };
        del.Click += (_, _) => { AppServices.Repo.DeleteTodo(todo.Id); BuildTodoPage(); };
        menu.Items.Add(del);
        row.ContextMenu = menu;
        return row;
    }

    // ---------------- 编辑页 ----------------

    private void BuildTodoEditor(TodoRecord todo)
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        top.Children.Add(UiKit.IconBtn(Icons.Left, () => { _editing = null; BuildTodoPage(); }, "返回列表"));
        var save = UiKit.IconBtn(Icons.Check, () =>
        {
            var title = (_titleInput?.Text ?? "").Trim();
            if (title.Length == 0) { Ui.Notifications.Toast("标题不能为空", 3); return; }
            todo.Title = title;
            todo.Note = _noteInput?.Text ?? "";
            todo.StepsJson = SerializeSteps();
            if (!AppServices.Repo.Todos().Any(t => t.Id == todo.Id)) AppServices.Repo.Upsert(todo);
            else AppServices.Repo.Upsert(todo);
            _editing = null;
            BuildTodoPage();
            Ui.Notifications.Toast($"已保存「{title}」", 3);
        }, "保存");
        top.Children.Add(save);
        var star = UiKit.IconBtn(todo.IsStarred ? Icons.Star : Icons.Star, () =>
        {
            todo.IsStarred = !todo.IsStarred;
            AppServices.Repo.Upsert(todo);
            BuildTodoPage();
        }, "收藏");
        star.Margin = new Thickness(6, 0, 0, 0);
        top.Children.Add(star);
        var remove = UiKit.IconBtn(Icons.Delete, () =>
        {
            if (!string.IsNullOrWhiteSpace(todo.Title)) AppServices.Repo.DeleteTodo(todo.Id);
            _editing = null;
            BuildTodoPage();
            Ui.Notifications.Toast("已删除", 3);
        }, "删除");
        remove.Margin = new Thickness(6, 0, 0, 0);
        top.Children.Add(remove);
        _content.Children.Add(top);

        // 标题
        var titleBox = new TextBox
        {
            Text = todo.Title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 8, 8, 8), Margin = new Thickness(0, 2, 0, 8),
            AcceptsReturn = false
        };
        _titleInput = titleBox;
        _content.Children.Add(new Border
        {
            Background = Theme.Desktop.CardBrush, CornerRadius = new CornerRadius(Tokens.CornerCard),
            BorderThickness = new Thickness(1), BorderBrush = Theme.Desktop.BorderBrush,
            Child = titleBox
        });

        // 四个胶囊：截止日期 / 提醒 / 重复 / 颜色
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        chips.Children.Add(Chip(todo.DueAt is { } d ? "截止 " + d.LocalDateTime.ToString("MM-dd HH:mm") : "截止日期", () =>
        {
            var input = UiKit.Input(todo.DueAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? DateTime.Today.ToString("yyyy-MM-dd") + " 18:00", _ => { }, 200);
            var dialog = SmallDialog("截止日期", "格式：2026-10-20 18:00（留空＝不设）", input, () =>
            {
                var text = input.Text.Trim();
                if (text.Length == 0) { todo.DueAt = null; }
                else if (DateTime.TryParse(text, out var parsed)) todo.DueAt = new DateTimeOffset(parsed);
                else { Ui.Notifications.Toast("日期格式看不懂，示例：2026-10-20 18:00", 4); return; }
                AppServices.Repo.Upsert(todo);
                BuildTodoPage();
            });
            dialog.Show();
        }));
        chips.Children.Add(Chip(ReminderLabel(todo), () =>
        {
            var options = new[] { -1, 0, 5, 30, 60, 1440 };
            var current = Array.IndexOf(options, todo.RemindMinutesBefore);
            todo.RemindMinutesBefore = options[(current + 1 + options.Length) % options.Length];
            AppServices.Repo.Upsert(todo);
            BuildTodoPage();
        }));
        chips.Children.Add(Chip(RepeatLabel(todo), () =>
        {
            var options = new[] { "None", "Daily", "Weekly", "Monthly" };
            var current = Array.IndexOf(options, todo.Repeat);
            todo.Repeat = options[(current + 1 + options.Length) % options.Length];
            AppServices.Repo.Upsert(todo);
            BuildTodoPage();
        }));
        chips.Children.Add(Chip(todo.Color.Length == 0 ? "无颜色" : "已选颜色", () =>
        {
            var list = new List<string> { "" };
            list.AddRange(TodoColors.Select(c => c.Hex));
            var current = list.IndexOf(todo.Color);
            todo.Color = list[(current + 1 + list.Count) % list.Count];
            AppServices.Repo.Upsert(todo);
            BuildTodoPage();
        }, todo.Color.Length > 0 ? todo.Color : null));
        _content.Children.Add(chips);

        // 步骤
        var steps = ParseSteps(todo);
        _stepList = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var (title, isDone) in steps)
        {
            var captured = (title, isDone);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var check = new CheckBox { IsChecked = captured.isDone, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            check.Click += (_, _) =>
            {
                var currentSteps = ParseSteps(todo);
                var index = currentSteps.FindIndex(s => s.Title == captured.title && s.Done == captured.isDone);
                if (index >= 0) currentSteps[index] = (captured.title, check.IsChecked == true);
                todo.StepsJson = JsonSerializer.Serialize(currentSteps.Select(s => new { title = s.Title, done = s.Done }));
                AppServices.Repo.Upsert(todo);
                BuildTodoPage();
            };
            row.Children.Add(check);
            row.Children.Add(UiKit.Text(captured.title, 12.5));
            _stepList.Children.Add(row);
        }
        _content.Children.Add(UiKit.Text("步骤", 12, secondary: true));
        _content.Children.Add(_stepList);
        var addStep = UiKit.Text("＋ 添加步骤", 12, secondary: true);
        addStep.Cursor = Cursors.Hand;
        addStep.Margin = new Thickness(0, 4, 0, 8);
        addStep.MouseLeftButtonUp += (_, _) =>
        {
            var input = UiKit.Input("", _ => { }, 200);
            var dialog = SmallDialog("添加步骤", "把这一条拆成一小步，做起来更轻松", input, () =>
            {
                var text = input.Text.Trim();
                if (text.Length == 0) return;
                var currentSteps = ParseSteps(todo);
                currentSteps.Add((text, false));
                todo.StepsJson = JsonSerializer.Serialize(currentSteps.Select(s => new { title = s.Title, done = s.Done }));
                AppServices.Repo.Upsert(todo);
                BuildTodoPage();
            });
            dialog.Show();
            input.Focus();
        };
        _content.Children.Add(addStep);

        // 备注
        _content.Children.Add(UiKit.Text("备注（支持 Markdown 写法）", 12, secondary: true));
        var noteBox = new TextBox
        {
            Text = todo.Note ?? "",
            Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5, Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 8)
        };
        _noteInput = noteBox;
        _content.Children.Add(new Border
        {
            Background = Theme.Desktop.CardBrush, CornerRadius = new CornerRadius(Tokens.CornerCard),
            BorderThickness = new Thickness(1), BorderBrush = Theme.Desktop.BorderBrush,
            Child = noteBox
        });

        _content.Children.Add(UiKit.Text("创建于 " + todo.CreatedAt.LocalDateTime.ToString("yyyy/M/d HH:mm"), 10.5, secondary: true));
    }

    private TextBox? _titleInput;
    private TextBox? _noteInput;
    private StackPanel? _stepList;

    private static string ReminderLabel(TodoRecord todo) => todo.RemindMinutesBefore switch
    {
        < 0 => "不提醒", 0 => "准点提醒", 5 => "提前 5 分钟", 30 => "提前 30 分钟", 60 => "提前 1 小时", 1440 => "提前 1 天",
        _ => $"提前 {todo.RemindMinutesBefore} 分钟"
    };

    private static string RepeatLabel(TodoRecord todo) => todo.Repeat switch
    {
        "Daily" => "每天", "Weekly" => "每周", "Monthly" => "每月", "Lunar" => "农历", _ => "不重复"
    };

    /// <summary>一个胶囊按钮（图标 + 文字），有颜色时左侧带一个色点。</summary>
    private static Border Chip(string label, Action onClick, string? colorHex = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (colorHex is not null)
            content.Children.Add(new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex))
            });
        content.Children.Add(UiKit.Text(label, 11.5));
        var chip = new Border
        {
            Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(Tokens.CornerCard),
            Background = Theme.Desktop.CardBrush,
            BorderThickness = new Thickness(1), BorderBrush = Theme.Desktop.BorderBrush,
            Cursor = Cursors.Hand, Child = content
        };
        chip.MouseLeftButtonUp += (_, _) => onClick();
        return chip;
    }

    /// <summary>通用的"小窗 + 一个输入框 + 确定/取消"（步骤、日期都用它）。</summary>
    private Window SmallDialog(string title, string hint, UIElement input, Action onOk)
    {
        var ok = UiKit.Btn("确定", () => { }, true);
        var cancel = UiKit.Btn("取消", () => { });
        var dialog = new Window
        {
            Title = title, Width = 400, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Theme.Current.BackgroundBrush, Foreground = Theme.Current.ForegroundBrush
        };
        dialog.Content = UiKit.Card(null, UiKit.Col(
            UiKit.Text(title, 14, bold: true),
            UiKit.Text(hint, 11.5, secondary: true),
            input,
            UiKit.Row(ok, cancel)));
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) => { onOk(); dialog.Close(); };
        return dialog;
    }

    private static List<(string Title, bool Done)> ParseSteps(TodoRecord todo)
    {
        if (string.IsNullOrWhiteSpace(todo.StepsJson)) return new List<(string, bool)>();
        try
        {
            using var doc = JsonDocument.Parse(todo.StepsJson);
            return doc.RootElement.EnumerateArray()
                .Select(e => (
                    e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    e.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True))
                .Where(s => s.Item1.Length > 0)
                .ToList();
        }
        catch { return new List<(string, bool)>(); }
    }

    private string SerializeSteps()
    {
        // 编辑页里步骤是即时落盘的，这里只保证字段是合法 JSON
        return _editing?.StepsJson ?? "";
    }
}
