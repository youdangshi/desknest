using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core.Services;

namespace DeskNest.App.Windows;

/// <summary>Spotlight 式快捷搜索面板（全盘索引 + 命令 + 设置项提示）。</summary>
public sealed class SearchPanel
{
    private Window? _window;
    private TextBox? _input;
    private ListBox? _list;
    private TextBlock? _hint;

    public void ShowPanel()
    {
        EnsureWindow();
        if (_window is null) return;
        _window.Show();
        _window.Activate();
        _input?.Focus();
        _input?.SelectAll();
        RefreshResults();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;

        _input = new TextBox
        {
            FontSize = 16,
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Theme.Current.ForegroundBrush,
            CaretBrush = Theme.Current.AccentBrush
        };
        _input.TextChanged += (_, _) => RefreshResults();
        _input.KeyDown += OnInputKeyDown;

        _list = new ListBox
        {
            Height = 320,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Theme.Current.ForegroundBrush,
            FontSize = 13
        };
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += OnInputKeyDown;

        _hint = UiKit.Text("↑↓ 选择 · Enter 打开 · Ctrl+Enter 所在位置 · Esc 关闭 · 输入 / 使用命令", 11, secondary: true);
        _hint.Margin = new Thickness(12, 4, 12, 8);

        var stack = new StackPanel();
        stack.Children.Add(_input);
        stack.Children.Add(new Border { Height = 1, Background = Theme.Current.BorderBrush, Margin = new Thickness(10, 0, 10, 6) });
        stack.Children.Add(_list);
        stack.Children.Add(_hint);

        _window = new Window
        {
            Icon = AppIcon.WindowIcon,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Width = 680,
            Height = 460,
            Content = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.Current.BorderBrush,
                Background = Theme.Current.CardBrush,
                Effect = UiKit.SoftShadow(),
                Child = stack
            },
        };
        _window.Deactivated += (_, _) => _window?.Hide();

        var area = SystemParameters.WorkArea;
        _window.Left = area.Left + (area.Width - _window.Width) / 2;
        _window.Top = area.Top + area.Height * 0.22;
        _window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) _window!.Hide();
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _window?.Hide();
                e.Handled = true;
                break;
            case Key.Down:
                if (_list!.Items.Count > 0) _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                if (_list!.Items.Count > 0) _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter:
                OpenSelected(Keyboard.Modifiers == ModifierKeys.Control);
                e.Handled = true;
                break;
        }
    }

    private void RefreshResults()
    {
        if (_list is null || _input is null) return;
        var hits = AppServices.Search.Query(_input.Text, 50);
        _list.Items.Clear();
        foreach (var hit in hits)
        {
            var row = new Grid { Margin = new Thickness(2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var kindText = hit.Kind switch
            {
                SearchItemKind.Command => "命令",
                SearchItemKind.Folder => "文件夹",
                SearchItemKind.App => "应用",
                _ => "文件"
            };
            var badge = new Border
            {
                Background = Theme.Current.AccentBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = kindText, FontSize = 10.5, Foreground = Brushes.White }
            };
            Grid.SetColumn(badge, 0);
            var title = new TextBlock
            {
                Text = hit.Title,
                FontSize = 13,
                Foreground = Theme.Current.ForegroundBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);
            var sub = new TextBlock
            {
                Text = hit.Kind == SearchItemKind.Command ? hit.Subtitle : hit.Path,
                FontSize = 11,
                Foreground = Theme.Current.TertiaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(sub, 2);
            row.Children.Add(badge);
            row.Children.Add(title);
            row.Children.Add(sub);
            _list.Items.Add(new ListBoxItem { Content = row, Tag = hit, Padding = new Thickness(6, 4, 6, 4) });
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        if (_hint is not null)
            _hint.Text = AppServices.Search.IsIndexing
                ? $"正在建立索引…已收录 {AppServices.Search.IndexedCount} 条 · ↑↓ 选择 · Enter 打开 · Esc 关闭"
                : $"已收录 {AppServices.Search.IndexedCount} 条 · ↑↓ 选择 · Enter 打开 · Ctrl+Enter 所在位置 · 输入 / 用命令";
    }

    private void OpenSelected(bool reveal = false)
    {
        if (_list?.SelectedItem is not ListBoxItem { Tag: SearchHit hit }) return;
        try
        {
            if (hit.Kind == SearchItemKind.Command)
            {
                Commands.Execute(hit.Path);
                _window?.Hide();
                return;
            }
            if (reveal)
            {
                if (Directory.Exists(hit.Path)) System.Diagnostics.Process.Start("explorer.exe", $"\"{hit.Path}\"");
                else System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{hit.Path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hit.Path) { UseShellExecute = true });
            }
            AppServices.Settings.Set("search.lastQuery", hit.Title);
            _window?.Hide();
        }
        catch (Exception ex)
        {
            Ui.Notifications.Toast("打开失败：" + ex.Message);
        }
    }
}
