using System.Text.Json;

namespace DeskNest.Core.Services;

public enum PetState { Idle, Typing, Click, Drag, Sleep, Think, Happy, Tired, Remind }

public sealed record PetBubble(string Text, string? ActionKind = null, string? ActionPayload = null);

/// <summary>
/// 桌宠状态机（不含界面）：输入事件 → 优先级 + 冷却 → 状态；提醒类事件产生气泡。
/// 一切触发源默认关闭或可关；键盘只取"有按键"这一事实，不记录字符。
/// </summary>
public sealed class PetEngine
{
    private readonly Dictionary<PetState, DateTimeOffset> _lastEntered = new();
    private readonly object _gate = new();
    private DateTimeOffset _stateUntil;

    public PetEngine() { Current = PetState.Idle; }

    public PetState Current { get; private set; }
    public event Action<PetState>? StateChanged;
    public event Action<PetBubble>? Bubble;

    // 可配置开关（由设置页写入）
    public bool KeyboardFollow { get; set; }
    public bool ClickReaction { get; set; } = true;
    public bool SystemReaction { get; set; } = true;
    public bool IdleReaction { get; set; } = true;
    public bool TodoReminder { get; set; } = true;

    private bool CooldownOk(PetState s, TimeSpan cd)
    {
        if (!_lastEntered.TryGetValue(s, out var last)) return true;
        return DateTimeOffset.Now - last >= cd;
    }

    private void Enter(PetState s, TimeSpan hold)
    {
        lock (_gate)
        {
            _lastEntered[s] = DateTimeOffset.Now;
            Current = s;
            _stateUntil = DateTimeOffset.Now + hold;
        }
        StateChanged?.Invoke(s);
    }

    /// <summary>键盘事件：只表示"有人按了键"，不含键值。</summary>
    public void OnKeyPress()
    {
        if (!KeyboardFollow) return;
        if (!CooldownOk(PetState.Typing, TimeSpan.FromMilliseconds(60))) return;
        Enter(PetState.Typing, TimeSpan.FromMilliseconds(420));
    }

    public void OnClick()
    {
        if (!ClickReaction) return;
        if (!CooldownOk(PetState.Click, TimeSpan.FromMilliseconds(300))) return;
        Enter(PetState.Click, TimeSpan.FromMilliseconds(600));
    }

    public void OnDragStart() => Enter(PetState.Drag, TimeSpan.FromMinutes(10));
    public void OnDragEnd() => Enter(PetState.Idle, TimeSpan.Zero);
    public void OnHover() { if (Current == PetState.Sleep) Enter(PetState.Idle, TimeSpan.Zero); }

    public void OnPomodoroDone() => Enter(PetState.Happy, TimeSpan.FromSeconds(3));
    public void OnPomodoroStart() => Enter(PetState.Think, TimeSpan.FromSeconds(2));

    /// <summary>系统状态联动：CPU 持续高 → 变"累"。</summary>
    public void OnSystemSample(PerfSample s, int cpuThreshold = 85)
    {
        if (!SystemReaction) return;
        if (s.CpuPercent >= cpuThreshold)
        {
            if (CooldownOk(PetState.Tired, TimeSpan.FromMinutes(5)))
                Enter(PetState.Tired, TimeSpan.FromSeconds(6));
        }
    }

    public void OnIdle(TimeSpan idle)
    {
        if (!IdleReaction) return;
        if (idle >= TimeSpan.FromMinutes(10) && CooldownOk(PetState.Sleep, TimeSpan.FromMinutes(10)))
            Enter(PetState.Sleep, TimeSpan.FromMinutes(30));
    }

    /// <summary>待办提醒：举手 + 气泡（带"完成/稍后"动作）。</summary>
    public void RemindTodo(string title, string todoId)
    {
        if (!TodoReminder) return;
        Enter(PetState.Remind, TimeSpan.FromSeconds(8));
        Bubble?.Invoke(new PetBubble($"该做啦：{title}", "todo", todoId));
    }

    public void Say(string text, string? actionKind = null, string? actionPayload = null)
    {
        Enter(PetState.Think, TimeSpan.FromSeconds(3));
        Bubble?.Invoke(new PetBubble(text, actionKind, actionPayload));
    }

    /// <summary>由界面定时器调用：到时间自动回到 idle。</summary>
    public void Tick()
    {
        bool expired;
        lock (_gate) expired = DateTimeOffset.Now >= _stateUntil;
        if (expired && Current is not (PetState.Idle or PetState.Sleep))
            Enter(PetState.Idle, TimeSpan.Zero);
    }
}
