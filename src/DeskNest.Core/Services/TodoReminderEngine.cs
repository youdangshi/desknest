using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskNest.Core.Services;

/// <summary>
/// 待办到点提醒 + 重复任务的**纯逻辑**（不碰 UI、不碰数据库，便于冒烟测试）。
///
/// 由来：HANDOFF 里挂了很久的缺口——`PetEngine.RemindTodo` 只有演示调用，
/// 没有定时器按 `DueAt - RemindMinutesBefore` 触发；`Repeat` 也不会自动生成下一条。
/// 这里把"哪些该提醒、完成后下一轮是什么"抽成纯函数，界面只负责调度与呈现。
/// </summary>
public sealed class TodoReminderEngine
{
    /// <summary>
    /// 已到提醒点后最多再过多久仍算"该提醒"（用户关机/休眠错过后仍能补一次）。
    /// 必须显著大于扫描周期 60 秒：早先用 1 分钟，准点提醒的窗口正好等于扫描间隔，
    /// 相位稍偏就会整段落在两次扫描之间，永远不提醒。
    /// </summary>
    public static readonly TimeSpan ReminderGrace = TimeSpan.FromHours(12);

    /// <summary>
    /// 同一 (id, dueAt) 只提醒一次——**状态去重，不依赖时间窗**。
    /// 早先用"1 分钟窗口 + now-last"去重，窗口恰等于调度器的 60 秒扫描周期，
    /// 而实际扫描只会更晚，条件几乎恒为 false，导致同一条被反复弹。
    /// </summary>
    private readonly HashSet<string> _reminded = new();

    /// <summary>
    /// 扫描出此刻该提醒的待办：`DueAt - 提前量 &lt;= now &lt;= DueAt + ReminderGrace`，
    /// 未完成、未归档、有截止、设置了提醒（分钟数 &gt;= 0）。
    /// 同一 (id, dueAt) 只返回一次，直到完成生成下一轮（新 dueAt）或跨天 Prune。
    /// </summary>
    public List<TodoRecord> DueNow(IEnumerable<TodoRecord> todos, DateTimeOffset now)
    {
        var due = new List<TodoRecord>();
        foreach (var t in todos)
        {
            if (t.IsDone || t.IsArchived || t.DueAt is null || t.RemindMinutesBefore < 0) continue;
            var fireAt = t.DueAt.Value - TimeSpan.FromMinutes(t.RemindMinutesBefore);
            if (now < fireAt || now > t.DueAt.Value + ReminderGrace) continue;

            var key = Key(t);
            if (!_reminded.Add(key)) continue; // 已提醒过：与扫描周期无关，绝不重复
            due.Add(t);
        }
        return due;
    }

    /// <summary>
    /// 完成一条重复待办后，生成下一轮的"新待办"；不重复（Repeat=None）返回 null。
    /// 截止日从 DueAt 向前推进到 > now 的最近一轮，避免用户隔很久才完成时补一堆过期任务。
    /// </summary>
    public static TodoRecord? NextOccurrence(TodoRecord completed, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(completed.Repeat) || completed.Repeat == "None") return null;
        var baseDue = completed.DueAt ?? now;

        var next = completed.Repeat switch
        {
            "Daily" => AdvanceDays(baseDue, now, 1),
            "Weekly" => AdvanceDays(baseDue, now, 7),
            "Monthly" => AdvanceMonths(baseDue, now, 1),
            // 农历没有引入历法库，沿用项目既有约定：用 29 天近似一个朔望月。
            "Lunar" => AdvanceDays(baseDue, now, 29),
            _ => baseDue
        };

        return new TodoRecord
        {
            Title = completed.Title,
            Note = completed.Note,
            ListName = completed.ListName,
            Priority = completed.Priority,
            Repeat = completed.Repeat,
            DueAt = next,
            RemindMinutesBefore = completed.RemindMinutesBefore,
            Color = completed.Color,
            IsStarred = completed.IsStarred,
            StepsJson = completed.StepsJson,
            CreatedAt = now
        };
    }

    private static DateTimeOffset AdvanceDays(DateTimeOffset from, DateTimeOffset now, int step)
    {
        var next = from;
        // 最多推进 4000 轮，防止 due_at 是远古日期时死循环。
        for (var i = 0; i < 4000 && next <= now; i++) next = next.AddDays(step);
        return next;
    }

    private static DateTimeOffset AdvanceMonths(DateTimeOffset from, DateTimeOffset now, int step)
    {
        var next = from;
        for (var i = 0; i < 4000 && next <= now; i++) next = next.AddMonths(step);
        return next;
    }

    /// <summary>清理已提醒记录（例如跨天时调用，避免字典无限增长）。</summary>
    public void Prune(DateTimeOffset now)
    {
        // key 尾部是 dueAt 的 ISO 串，解析出来按天清理，避免集合无限增长。
        foreach (var key in _reminded.ToList())
        {
            var bar = key.LastIndexOf('|');
            if (bar < 0) { _reminded.Remove(key); continue; }
            if (DateTimeOffset.TryParse(key[(bar + 1)..], out var due) && now - due > TimeSpan.FromDays(2))
                _reminded.Remove(key);
        }
    }

    private static string Key(TodoRecord t) => $"{t.Id}|{t.DueAt:o}";
}
