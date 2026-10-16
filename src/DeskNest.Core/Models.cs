namespace DeskNest.Core;

public enum BoxKind { Normal, Mapped, Drawer, Todo }
public enum StorageMode { Managed, CustomRoot, None }
public enum ItemKind { File, Folder, Shortcut, MappedRef, Url }
public enum WidgetKind { File, Todo, QuickNote, Weather, Music, SystemMonitor, Glance }
public enum PowerActionKind { Shutdown, Restart, Sleep }
public enum PowerTriggerKind { Clock, Countdown, Idle }
public enum StyleScope { Inherit, Override }

public sealed class BoxRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "新收纳盒";
    public BoxKind Kind { get; set; } = BoxKind.Normal;
    public StorageMode StorageMode { get; set; } = StorageMode.Managed;
    public string? StorageRoot { get; set; }
    public string? GroupId { get; set; }
    public string? MappedPath { get; set; }
    public string ViewMode { get; set; } = "Icon";
    public string SortMode { get; set; } = "Name";
    public bool SortDescending { get; set; }
    public int IconSize { get; set; } = 30;
    public double TextSize { get; set; } = 11.5;
    public int FileNameLines { get; set; } = 2;
    public double Opacity { get; set; } = 0.82;
    public string Material { get; set; } = "Acrylic";
    public double MaterialIntensity { get; set; } = 0.65;
    public int CornerRadius { get; set; } = 12;
    public StyleScope StyleScope { get; set; } = StyleScope.Inherit;
    public bool LockPosition { get; set; }
    public bool LockSize { get; set; }
    public bool RolledUp { get; set; }
    public bool IsHidden { get; set; }
    public bool AutoHide { get; set; }
    public bool HideFileName { get; set; }
    public bool ShowExtensions { get; set; }
    public bool ShowHoverButtons { get; set; } = true;
    /// <summary>
    /// 标题栏样式：Standard（标准）/ Compact（紧凑）/ Floating（悬浮，鼠标移上去才显形）/
    /// Hidden（隐藏，靠 Alt+拖动或快捷键操作）。
    /// </summary>
    public string TitleStyle { get; set; } = "Standard";
    public string MonitorKey { get; set; } = "";
    public string Anchor { get; set; } = "Free";
    public double MarginX { get; set; } = 200;
    public double MarginY { get; set; } = 120;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 420;
    public double CollapsedX { get; set; } = -1;
    public double CollapsedY { get; set; } = -1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ItemRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string BoxId { get; set; } = "";
    public ItemKind Kind { get; set; } = ItemKind.File;
    public string DisplayName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string? StoredPath { get; set; }
    public string Ext { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset? SourceModified { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public bool IsFavorite { get; set; }
    public string? Tags { get; set; }
    public int OrderIndex { get; set; }
    public string? StackKey { get; set; }
    public bool IsMissing { get; set; }
}

public sealed class BoxGroupRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "我的桌面";
    public string MemberIds { get; set; } = "";
    public string? ActiveMemberId { get; set; }
    public string NavStyle { get; set; } = "Click";
    public bool WheelSwitch { get; set; } = true;
    public bool HoverSwitch { get; set; }
    public string TitleDisplay { get; set; } = "Text";
    public string MonitorKey { get; set; } = "";
    public double MarginX { get; set; } = 200;
    public double MarginY { get; set; } = 120;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 420;
    public bool IsVisible { get; set; } = true;
}

public sealed class WidgetRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public WidgetKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? GroupId { get; set; }
    public string? BoxId { get; set; }
    public string MonitorKey { get; set; } = "";
    public double X { get; set; } = 300;
    public double Y { get; set; } = 300;
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 420;
    public double Opacity { get; set; } = 0.82;
    public string Material { get; set; } = "Acrylic";
    public double MaterialIntensity { get; set; } = 0.65;
    public int CornerRadius { get; set; } = 12;
    public StyleScope StyleScope { get; set; } = StyleScope.Inherit;
    public string AnimationEffect { get; set; } = "SlideFade";
    public string ViewMode { get; set; } = "Icon";
    public bool IsVisible { get; set; } = true;
    public bool IsCollapsed { get; set; }
    public string? ConfigJson { get; set; }
}

public sealed class TodoRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string ListName { get; set; } = "默认清单";
    public int Priority { get; set; } = 1;          // 0 低 / 1 中 / 2 高
    public string Repeat { get; set; } = "None";    // None/Daily/Weekly/Monthly/Lunar
    public DateTimeOffset? DueAt { get; set; }
    public int RemindMinutesBefore { get; set; } = 5;
    public bool IsDone { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>颜色标记（空＝无颜色），学 DeskBox 那排色块。</summary>
    public string Color { get; set; } = "";
    /// <summary>收藏（星标）。</summary>
    public bool IsStarred { get; set; }
    /// <summary>步骤（子任务），JSON：[{"title":"…","done":false}]。</summary>
    public string StepsJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class NoteRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Format { get; set; } = "Markdown";
    public bool IsPinned { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class RuleRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "新规则";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public string ConditionsJson { get; set; } = "{}";
    public string ActionsJson { get; set; } = "{}";
    public int HitCount { get; set; }
}

public sealed class OrganizeMove
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? BoxId { get; set; }
    public string? Reason { get; set; }
}

public sealed class OrganizeRunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Trigger { get; set; } = "Manual";
    public int MovedCount { get; set; }
    public bool Undone { get; set; }
    public string MovesJson { get; set; } = "[]";
}

public sealed class PetRecord
{
    public string Id { get; set; } = "default";
    public string? PackId { get; set; }
    public string Name { get; set; } = "小巢";
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    public double X { get; set; } = 1200;
    public double Y { get; set; } = 600;
    public string TopmostMode { get; set; } = "Always";
    public bool KeyboardFollow { get; set; }
    public bool ClickReaction { get; set; } = true;
    public bool SystemReaction { get; set; } = true;
    public bool IdleReaction { get; set; } = true;
    public bool TodoReminder { get; set; } = true;
    public bool PomodoroEnabled { get; set; }
    public bool AiAskEnabled { get; set; }
    public int FpsLimit { get; set; } = 30;
    public bool IsVisible { get; set; } = true;
}

public sealed class UsageSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string ProcessName { get; set; } = "";
    public string? WindowTitle { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public long DurationSeconds { get; set; }
}

public sealed class PowerPlanRecord
{
    public string Id { get; set; } = "default";
    public bool Enabled { get; set; }
    public PowerTriggerKind Trigger { get; set; } = PowerTriggerKind.Clock;
    public string ClockTime { get; set; } = "23:30";
    public bool WeekdaysOnly { get; set; } = true;
    public int CountdownMinutes { get; set; } = 120;
    public int IdleMinutes { get; set; } = 30;
    public int WarnMinutesBefore { get; set; } = 5;
    public PowerActionKind Action { get; set; } = PowerActionKind.Shutdown;
    public bool BackupBefore { get; set; } = true;
    public bool DeferOnActivity { get; set; } = true;
}

public sealed class ApiProviderRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? Model { get; set; }
    public string? KeyCipher { get; set; }
    public double? ManualQuota { get; set; }
    public double? QuotaWarnBelow { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ApiStatRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string ProviderId { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string Purpose { get; set; } = "classify";
    public string? ErrorKind { get; set; }
}
