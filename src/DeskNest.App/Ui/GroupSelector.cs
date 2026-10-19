namespace DeskNest.App.Ui;

/// <summary>组标题栏「成员选择器」里的一个成员条目（纯数据，不含控件，便于冒烟测试）。</summary>
public sealed record GroupSelectorEntry(string BoxId, string Name, bool IsActive, int Index);

/// <summary>
/// 组导航的纯逻辑：按 DeskBox 的规范，一个组在标题栏里只有**一个**成员选择器
/// （`[图标] 当前成员 2/4 ▾`），点开是一个成员列表，而不是外置标签条。
/// 这里只算「列表里有哪些项、当前是第几个」，画界面的事交给 BoxWindow。
/// </summary>
public static class GroupSelector
{
    /// <summary>
    /// 按组成员顺序生成条目；成员记录已不存在时**跳过而不占位**（否则会出现空标签、计数也不对）。
    /// </summary>
    public static IReadOnlyList<GroupSelectorEntry> Build(
        string? memberIds, string? activeId, Func<string, string?> nameOf)
    {
        var result = new List<GroupSelectorEntry>();
        if (string.IsNullOrWhiteSpace(memberIds)) return result;

        var index = 0;
        foreach (var id in memberIds.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = nameOf(id);
            if (string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new GroupSelectorEntry(id, name, id == activeId, index++));
        }
        return result;
    }

    /// <summary>“2/4” 这种计数。当前成员不在列表里时给 `–/N`，不撒谎说"第一个"。</summary>
    public static string Counter(IReadOnlyList<GroupSelectorEntry> entries, string? activeId)
    {
        if (entries.Count == 0) return "";
        var idx = -1;
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].BoxId == activeId) { idx = i; break; }
        return idx < 0 ? $"–/{entries.Count}" : $"{idx + 1}/{entries.Count}";
    }

    /// <summary>组里只有 1 个成员时不显示选择器（没有可切的东西，露出来只会让人以为界面坏了）。</summary>
    public static bool ShouldShow(IReadOnlyList<GroupSelectorEntry> entries) => entries.Count >= 2;

    /// <summary>下一个/上一个成员 id（首尾循环）；只有一个成员时返回原值。</summary>
    public static string? Step(IReadOnlyList<GroupSelectorEntry> entries, string? activeId, int direction)
    {
        if (entries.Count == 0) return null;
        var idx = -1;
        for (var i = 0; i < entries.Count; i++)
            if (entries[i].BoxId == activeId) { idx = i; break; }
        if (idx < 0) return entries[0].BoxId;
        var next = (idx + Math.Sign(direction) + entries.Count) % entries.Count;
        return entries[next].BoxId;
    }
}
