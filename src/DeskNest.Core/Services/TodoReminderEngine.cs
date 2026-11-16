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
    /// <summary>提醒后多久之内不再重复弹（防止每分钟扫描反复提醒同一条）。</summary>
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _remindedAt = new();

    /// <summary>
    /// 扫描出此刻该提醒的待办：`now >= DueAt - 提前量` 且 `now &lt; DueAt + 宽限`，
    /// 未完成、未归档、有截止、设置了提醒（分钟数 &gt;= 0）。
    /// 同一 (id, dueAt) 在 ReminderWindow 内只返回一次。
    /// </summary>
    public List<TodoRecord> DueNow(IEnumerable<TodoRecord> todos, DateTimeOffset now)
    {
        var due = new List<TodoRecord>();
        foreach (var t in todos)
        {
            if (t.IsDone || t.IsArchived || t.DueAt is null || t.RemindMinutesBefore < 0) continue;
            var fireAt = t.DueAt.Value - TimeSpan.FromMinutes(t.RemindMinutesBefore);
            if (now < fireAt || now > t.DueAt.Value + ReminderWindow) continue;

            var key = Key(t);
            if (_remindedAt.TryGetValue(key, out var last) && now - last < ReminderWindow) continue;

            _remindedAt[key] = now;
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
        foreach (var key in _remindedAt.Where(kv => now - kv.Value > TimeSpan.FromDays(2)).Select(kv => kv.Key).ToList())
            _remindedAt.Remove(key);
    }

    private static string Key(TodoRecord t) => $"{t.Id}|{t.DueAt:o}";
}
