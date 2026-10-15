using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

public enum PowerEventKind { Warn, Execute, Cancelled, Postponed }
public sealed record PowerEvent(PowerEventKind Kind, PowerActionKind Action, string Message, int SecondsLeft);

/// <summary>
/// 计划关机：定时/倒计时/空闲触发；提前 N 分钟提醒，可顺延或取消；
/// 执行动作交给外部委托（Windows 关机/重启/睡眠），本类只做时间与状态机。
/// </summary>
public sealed class PowerScheduler : IDisposable
{
    private readonly Repository _repo;
    private readonly Action<PowerActionKind> _execute;
    private readonly Func<TimeSpan> _idleProvider;
    private readonly System.Threading.Timer _timer;
    private DateTimeOffset? _deadline;
    private bool _warned;
    private readonly object _gate = new();

    public PowerScheduler(Repository repo, Action<PowerActionKind> execute, Func<TimeSpan> idleProvider)
    {
        _repo = repo;
        _execute = execute;
        _idleProvider = idleProvider;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public PowerPlanRecord Plan => _repo.PowerPlan();
    public DateTimeOffset? Deadline { get { lock (_gate) return _deadline; } }
    public event Action<PowerEvent>? Event;

    public void Apply(PowerPlanRecord plan)
    {
        _repo.SavePowerPlan(plan);
        lock (_gate)
        {
            _warned = false;
            _deadline = null;
            if (plan.Enabled)
            {
                switch (plan.Trigger)
                {
                    case PowerTriggerKind.Clock:
                        if (TimeSpan.TryParse(plan.ClockTime, out var t))
                        {
                            var now = DateTime.Now;
                            var target = now.Date.Add(t);
                            if (target <= now) target = target.AddDays(1);
                            if (plan.WeekdaysOnly)
                                while (target.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) target = target.AddDays(1);
                            _deadline = new DateTimeOffset(target);
                        }
                        break;
                    case PowerTriggerKind.Countdown:
                        _deadline = DateTimeOffset.Now.AddMinutes(Math.Max(1, plan.CountdownMinutes));
                        break;
                    case PowerTriggerKind.Idle:
                        _deadline = null;   // 空闲触发靠 tick 检查
                        break;
                }
            }
        }
        _timer.Change(0, 5000);
        Log.Info($"计划关机已应用：enabled={plan.Enabled} trigger={plan.Trigger} deadline={_deadline}");
    }

    public void Cancel(string reason = "用户取消")
    {
        lock (_gate)
        {
            _deadline = null;
            _warned = false;
        }
        Event?.Invoke(new PowerEvent(PowerEventKind.Cancelled, Plan.Action, reason, 0));
        Log.Info("计划关机已取消：" + reason);
    }

    public void Postpone(int minutes)
    {
        lock (_gate)
        {
            var basis = _deadline ?? DateTimeOffset.Now;
            _deadline = basis.AddMinutes(minutes);
            _warned = false;
        }
        Event?.Invoke(new PowerEvent(PowerEventKind.Postponed, Plan.Action, $"已顺延 {minutes} 分钟", minutes * 60));
    }

    private void Tick()
    {
        try
        {
            var plan = Plan;
            if (!plan.Enabled) return;

            if (plan.Trigger == PowerTriggerKind.Idle)
            {
                var idle = _idleProvider();
                if (idle.TotalMinutes >= plan.IdleMinutes && idle.TotalMinutes < plan.IdleMinutes + 5)
                {
                    lock (_gate) _deadline = DateTimeOffset.Now;
                }
                else return;
            }

            DateTimeOffset? deadline;
            lock (_gate) deadline = _deadline;
            if (deadline is null) return;

            var left = deadline.Value - DateTimeOffset.Now;
            if (left <= TimeSpan.Zero)
            {
                _execute(plan.Action);
                Event?.Invoke(new PowerEvent(PowerEventKind.Execute, plan.Action, "正在执行计划动作", 0));
                return;
            }

            if (!_warned && left <= TimeSpan.FromMinutes(Math.Max(1, plan.WarnMinutesBefore)))
            {
                _warned = true;
                Event?.Invoke(new PowerEvent(PowerEventKind.Warn, plan.Action,
                    $"还有 {(int)left.TotalMinutes} 分钟将" + ActionText(plan.Action), (int)left.TotalSeconds));
            }
        }
        catch (Exception ex) { Log.Warn("计划关机检查异常：" + ex.Message); }
    }

    private static string ActionText(PowerActionKind a) => a switch
    {
        PowerActionKind.Restart => "重启电脑",
        PowerActionKind.Sleep => "进入睡眠",
        _ => "关机"
    };

    public void Dispose() => _timer.Dispose();
}
