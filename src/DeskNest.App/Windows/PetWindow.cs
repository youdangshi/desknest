using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Services;
using Path = System.Windows.Shapes.Path;

namespace DeskNest.App.Windows;

/// <summary>
/// 桌宠窗口（工作伙伴型）：状态机驱动表情/动作，带气泡提醒与右键菜单。
/// 默认不读取键盘；素材包可插拔（.petpack），内置形象为自绘几何图形。
/// </summary>
public sealed class PetWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Ellipse _body;
    private readonly Ellipse _eyeL;
    private readonly Ellipse _eyeR;
    private readonly Path _mouth;
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private readonly StackPanel _bubbleActions;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _bubbleTimer;
    private PetRecord _pet;
    private PetState _state = PetState.Idle;
    private string _pomodoroId = "";

    public PetWindow()
    {
        _pet = AppServices.Repo.Pet();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = _pet.TopmostMode == "Always";
        ResizeMode = ResizeMode.NoResize;
        Width = 170;
        Height = 210;
        Left = _pet.X;
        Top = _pet.Y;
        Opacity = _pet.Opacity;

        _body = new Ellipse
        {
            Width = 96,
            Height = 88,
            Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0xA8, 0xF0)),
            Stroke = Theme.Desktop.BorderBrush,
            StrokeThickness = 1
        };
        Canvas.SetLeft(_body, 34);
        Canvas.SetTop(_body, 68);

        _eyeL = Eye(48);
        _eyeR = Eye(88);
        _mouth = new Path
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Data = Geometry.Parse("M 68,116 Q 82,128 96,116")
        };

        _bubbleText = UiKit.Text("", 12);
        _bubbleActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _bubble = new Border
        {
            Background = Theme.Desktop.CardBrush,
            BorderBrush = Theme.Desktop.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            MaxWidth = 260,
            Visibility = Visibility.Collapsed,
            Effect = UiKit.SoftShadow(),
            Child = UiKit.Col(_bubbleText, _bubbleActions)
        };
        Canvas.SetLeft(_bubble, 4);
        Canvas.SetTop(_bubble, 0);

        _canvas.Children.Add(_body);
        _canvas.Children.Add(_eyeL);
        _canvas.Children.Add(_eyeR);
        Canvas.SetLeft(_eyeL, 56);
        Canvas.SetTop(_eyeL, 96);
        Canvas.SetLeft(_eyeR, 92);
        Canvas.SetTop(_eyeR, 96);
        _canvas.Children.Add(_mouth);
        _canvas.Children.Add(_bubble);
        Content = _canvas;

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonUp += (_, e) => { ShowMenu(); e.Handled = true; };
        MouseEnter += (_, _) =>
        {
            AppServices.Pet.OnHover();
            _body.Fill = new SolidColorBrush(Color.FromRgb(0x8F, 0xB8, 0xFF));
        };
        MouseLeave += (_, _) => _body.Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0xA8, 0xF0));
        LocationChanged += (_, _) => PersistPosition();

        AppServices.Pet.StateChanged += OnStateChanged;
        AppServices.Pet.Bubble += ShowBubble;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(5, _pet.FpsLimit)) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _bubbleTimer.Tick += (_, _) => { _bubble.Visibility = Visibility.Collapsed; _bubbleTimer.Stop(); };

        Closed += (_, _) =>
        {
            _timer.Stop();
            AppServices.Pet.StateChanged -= OnStateChanged;
            AppServices.Pet.Bubble -= ShowBubble;
        };
    }

    private static Ellipse Eye(double left) => new()
    {
        Width = 14,
        Height = 16,
        Fill = Brushes.White,
        RenderTransform = new TranslateTransform(),
        Tag = left
    };

    private void Tick()
    {
        AppServices.Pet.Tick();
        var bob = _state == PetState.Typing ? 2.5 : 1.0;
        var phase = DateTime.Now.Millisecond / 1000.0 * Math.PI * 2;
        var offset = Math.Sin(phase) * bob;
        Canvas.SetTop(_body, 68 + offset);
        Canvas.SetTop(_eyeL, 96 + offset);
        Canvas.SetTop(_eyeR, 96 + offset);
        Canvas.SetTop(_mouth, 110 + offset);

        // 系统状态联动
        if (AppServices.Perf.Latest is { } sample) AppServices.Pet.OnSystemSample(sample);
        if (AppServices.Pet.IdleReaction) AppServices.Pet.OnIdle(Native.Win32.SystemMetrics.IdleTime());
    }

    private void OnStateChanged(PetState state)
    {
        _state = state;
        var t = Theme.Current;
        switch (state)
        {
            case PetState.Sleep:
                _body.Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x86, 0xB8));
                _eyeL.Height = 4;
                _eyeR.Height = 4;
                break;
            case PetState.Tired:
                _body.Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x8E, 0x6B));
                _eyeL.Height = 6;
                _eyeR.Height = 6;
                break;
            case PetState.Happy:
            case PetState.Click:
                _body.Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0xE0, 0xA8));
                _eyeL.Height = 16;
                _eyeR.Height = 16;
                break;
            case PetState.Remind:
                _body.Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0xC4, 0x6B));
                _eyeL.Height = 16;
                _eyeR.Height = 16;
                break;
            default:
                _body.Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0xA8, 0xF0));
                _eyeL.Height = 16;
                _eyeR.Height = 16;
                break;
        }
        _mouth.Data = Geometry.Parse(state is PetState.Happy or PetState.Click
            ? "M 66,112 Q 82,132 98,112"
            : "M 70,116 Q 82,124 94,116");
    }

    public void ShowBubble(PetBubble bubble)
    {
        _bubbleText.Text = bubble.Text;
        _bubbleActions.Children.Clear();
        if (bubble.ActionKind == "todo" && bubble.ActionPayload is { } todoId)
        {
            _bubbleActions.Children.Add(UiKit.Btn("完成", () =>
            {
                try
                {
                    var todos = AppServices.Repo.Todos();
                    var todo = todos.FirstOrDefault(t => t.Id == todoId);
                    if (todo is not null)
                    {
                        todo.IsDone = true;
                        todo.CompletedAt = DateTimeOffset.Now;
                        AppServices.Repo.Upsert(todo);
                        Ui.Notifications.Toast($"已完成：{todo.Title}");
                    }
                }
                catch { }
                _bubble.Visibility = Visibility.Collapsed;
            }, true));
            _bubbleActions.Children.Add(UiKit.Btn("稍后 10 分钟", () =>
            {
                _bubble.Visibility = Visibility.Collapsed;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    AppServices.Pet.RemindTodo("刚才的待办", todoId);
                };
                timer.Start();
            }));
        }
        _bubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private bool _dragging;
    private Point _dragOffset;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        AppServices.Pet.OnClick();
        _dragging = true;
        _dragOffset = e.GetPosition(this);
        AppServices.Pet.OnDragStart();
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var screen = PointToScreen(e.GetPosition(this));
        Left = screen.X - _dragOffset.X;
        Top = screen.Y - _dragOffset.Y;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
        AppServices.Pet.OnDragEnd();
        PersistPosition();
    }

    private void PersistPosition()
    {
        try
        {
            _pet = AppServices.Repo.Pet();
            _pet.X = Left;
            _pet.Y = Top;
            AppServices.Repo.SavePet(_pet);
        }
        catch { }
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuAction("待办速览", ShowTodos));
        menu.Items.Add(MenuAction("番茄钟 25 分钟", () => StartPomodoro(25)));
        menu.Items.Add(MenuAction("番茄钟 45 分钟", () => StartPomodoro(45)));
        menu.Items.Add(MenuAction("休息一下（5 分钟）", () => StartBreak(5)));
        menu.Items.Add(new Separator());
        if (AppServices.Repo.Providers().Any(p => p.Enabled))
            menu.Items.Add(MenuAction("问我一句…", AskAi));
        else
            menu.Items.Add(MenuAction("问我一句…（需先在主窗口配置 API）", () => Ui.Notifications.Toast("请先在 主窗口 → API 面板 里配置一个模型")));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction(_pet.IsVisible ? "隐藏桌宠" : "显示桌宠", () =>
        {
            _pet = AppServices.Repo.Pet();
            _pet.IsVisible = !_pet.IsVisible;
            AppServices.Repo.SavePet(_pet);
            if (_pet.IsVisible) Show(); else Hide();
        }));
        menu.Items.Add(MenuAction("桌宠设置…", () =>
        {
            if (App.Current is App app) app.ShowMain("pet");
        }));
        menu.IsOpen = true;
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    private void ShowTodos()
    {
        var todos = AppServices.Repo.Todos().Where(t => !t.IsDone).Take(5).ToList();
        if (todos.Count == 0)
        {
            ShowBubble(new PetBubble("现在没有待办，休息一下吧"));
            return;
        }
        ShowBubble(new PetBubble(string.Join("\n", todos.Select(t => $"{t.Title}（{t.ListName}）")),
            "todo", todos[0].Id));
    }

    private void StartPomodoro(int minutes)
    {
        _pomodoroId = Guid.NewGuid().ToString("n");
        AppServices.Pet.OnPomodoroStart();
        ShowBubble(new PetBubble($"{minutes} 分钟专注开始，我陪着你"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            AppServices.Pet.OnPomodoroDone();
            ShowBubble(new PetBubble($"专注 {minutes} 分钟结束，起来走两步吧"));
        };
        timer.Start();
    }

    private void StartBreak(int minutes)
    {
        ShowBubble(new PetBubble($"休息 {minutes} 分钟，我盯着时间"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ShowBubble(new PetBubble("休息结束，继续加油"));
        };
        timer.Start();
    }

    private async void AskAi()
    {
        try
        {
            var provider = AppServices.Repo.Providers().FirstOrDefault(p => p.Enabled);
            if (provider is null)
            {
                ShowBubble(new PetBubble("还没有可用的模型，先去 API 面板配置一个吧"));
                return;
            }
            var ask = new TextBox { Width = 240, FontSize = 12.5 };
            var askBtn = UiKit.Btn("提问", () => { }, true);
            var win = new Window
            {
                Title = "问桌宠一句",
                Width = 320,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Desktop.BackgroundBrush,
                Content = UiKit.Card(null, UiKit.Col(
                    UiKit.Text("它会结合你的待办一起回答", 11.5, secondary: true), ask, askBtn))
            };
            var answerText = UiKit.Text("", 12);
            var answerWin = new Window
            {
                Title = "桌宠的回答",
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Desktop.BackgroundBrush,
                Content = UiKit.Card(null, UiKit.Scroll(answerText))
            };
            askBtn.Click += async (_, _) =>
            {
                var question = ask.Text.Trim();
                if (question.Length == 0) return;
                win.Close();
                answerText.Text = "思考中…";
                answerWin.Show();
                var todos = string.Join("；", AppServices.Repo.Todos().Where(t => !t.IsDone).Take(10).Select(t => t.Title));
                var boxes = string.Join("；", AppServices.Repo.Boxes().Take(10).Select(b => $"{b.Name}({AppServices.Repo.Items(b.Id, false).Count}项)"));
                var prompt = $"我的待办：{todos}\n我的收纳盒：{boxes}\n问题：{question}";
                var (ok, content, err, _) = await AppServices.Api.ChatAsync(provider,
                    "你是桌面上的一只桌宠，回答要简短、口语化、可执行。", prompt);
                answerText.Text = ok ? content : "（调用失败：" + err + "）";
            };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowBubble(new PetBubble("出错了：" + ex.Message));
        }
    }
}
