using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>
/// 收纳盒的生命周期操作。抽出来单独放是为了两件事：
/// 一是"删盒子"这条路径能写测试（以前它和确认弹窗、界面刷新糊在一起，没法测），
/// 二是每一步都有日志——用户说"点了没反应"时，日志能直接告诉我们卡在哪一步。
/// </summary>
public sealed class BoxService
{
    private readonly Repository _repo;
    private readonly FileOps _files;

    public BoxService(Repository repo, FileOps files)
    {
        _repo = repo;
        _files = files;
    }

    /// <param name="Restored">成功还原回原位置的文件数。</param>
    /// <param name="Failed">还原失败的文件数（见日志）。</param>
    /// <param name="StillInDatabase">删完之后记录居然还在（说明写入被回滚了，属于严重问题）。</param>
    public sealed record DeleteResult(int Restored, int Failed, bool StillInDatabase);

    /// <summary>
    /// 删除盒子：先把盒内文件逐个还原到原位置（原位置不存在则回退桌面），再删掉数据库记录。
    /// 映射盒只存路径，本来就没搬动过文件，所以不还原。
    /// </summary>
    public DeleteResult Delete(BoxRecord box)
    {
        Log.Info($"开始删除盒子「{box.Name}」({box.Id})，类型={box.Kind}");

        var restored = 0;
        var failed = 0;
        if (box.Kind != BoxKind.Mapped)
        {
            foreach (var item in _repo.Items(box.Id, false))
            {
                var path = item.StoredPath ?? item.SourcePath;
                var result = _files.RestoreToOriginal(path, item.SourcePath);
                if (result.Ok) restored++;
                else
                {
                    failed++;
                    Log.Warn($"还原失败：{path} → {item.SourcePath}（{result.Error}）");
                }
            }
        }

        _repo.DeleteBox(box.Id);

        // 删完立刻回读一次：如果记录还在，说明这次写入被别的事务回滚了，
        // 界面必须把这件事说出来，而不是"看起来删了、刷新又回来"。
        var stillThere = _repo.Box(box.Id) is not null;
        Log.Info($"删除盒子「{box.Name}」结束：还原 {restored} 项，失败 {failed} 项，记录仍在={stillThere}");
        return new DeleteResult(restored, failed, stillThere);
    }

    /// <summary>
    /// 把 <paramref name="other"/> 并进 <paramref name="keep"/> 所在的组：两个盒子占同一块位置，
    /// 用标题栏标签切换。抽出来是为了能写测试——合并涉及三处写入（组、本盒、被并入的盒），
    /// 顺序错了就会出现"两个盒子叠在一起、谁也点不到"。
    /// </summary>
    public BoxGroupRecord MergeInto(BoxRecord keep, BoxRecord other)
    {
        if (keep.Id == other.Id) throw new InvalidOperationException("不能把盒子并进它自己");

        var group = keep.GroupId is null
            ? null
            : _repo.Groups().FirstOrDefault(g => g.Id == keep.GroupId);
        if (group is null)
        {
            group = new BoxGroupRecord
            {
                Name = keep.Name + " 组",
                MemberIds = keep.Id,
                ActiveMemberId = keep.Id,
                MarginX = keep.MarginX,
                MarginY = keep.MarginY,
                Width = keep.Width,
                Height = keep.Height
            };
            keep.GroupId = group.Id;
            _repo.Upsert(keep);
        }

        var members = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!members.Contains(other.Id)) members.Add(other.Id);
        group.MemberIds = string.Join(";", members);
        if (string.IsNullOrEmpty(group.ActiveMemberId) || !members.Contains(group.ActiveMemberId))
            group.ActiveMemberId = keep.Id;
        _repo.Upsert(group);

        // 被并入的盒子挪到组的位置与尺寸上，这样切换时不会跳位
        other.GroupId = group.Id;
        other.MarginX = group.MarginX;
        other.MarginY = group.MarginY;
        if (group.Width > 0) other.Width = group.Width;
        if (group.Height > 0) other.Height = group.Height;
        _repo.Upsert(other);

        Log.Info($"已把「{other.Name}」并进「{group.Name}」（成员 {members.Count} 个，当前显示 {group.ActiveMemberId}）");
        return group;
    }

    /// <summary>组里当前该显示的盒子 id（组没配置或成员不在组里时返回空）。</summary>
    public string? ActiveMemberOf(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return null;
        var group = _repo.Groups().FirstOrDefault(g => g.Id == groupId);
        if (group is null) return null;
        var members = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (members.Length == 0) return null;
        return members.Contains(group.ActiveMemberId) ? group.ActiveMemberId : members[0];
    }

    /// <summary>
    /// 把两个**桌面组件**（性能 / 待办 / 随记…）合并到同一个位置，用滚轮或标签切换。
    /// 与盒子共用同一套"组"结构（成员 + 当前成员 + 位置尺寸）。
    /// </summary>
    public BoxGroupRecord MergeWidgets(WidgetRecord keep, WidgetRecord other)
    {
        if (keep.Id == other.Id) throw new InvalidOperationException("不能把组件并进它自己");

        var group = keep.GroupId is null
            ? null
            : _repo.Groups().FirstOrDefault(g => g.Id == keep.GroupId);
        if (group is null)
        {
            group = new BoxGroupRecord
            {
                Name = keep.Name + " 组",
                MemberIds = keep.Id,
                ActiveMemberId = keep.Id,
                MarginX = keep.X,
                MarginY = keep.Y,
                Width = keep.Width,
                Height = keep.Height
            };
            keep.GroupId = group.Id;
            _repo.Upsert(keep);
        }

        var members = group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!members.Contains(other.Id)) members.Add(other.Id);
        group.MemberIds = string.Join(";", members);
        if (string.IsNullOrEmpty(group.ActiveMemberId) || !members.Contains(group.ActiveMemberId))
            group.ActiveMemberId = keep.Id;
        _repo.Upsert(group);

        other.GroupId = group.Id;
        other.X = group.MarginX;
        other.Y = group.MarginY;
        if (group.Width > 0) other.Width = group.Width;
        if (group.Height > 0) other.Height = group.Height;
        _repo.Upsert(other);

        Log.Info($"已把组件「{other.Name}」并进「{group.Name}」（成员 {members.Count} 个）");
        return group;
    }

    /// <summary>
    /// 解散盒子：盒内文件**全部还到桌面**（不管它原来在哪），然后删掉盒子记录。
    /// 与「删除盒子（逐个还原到原位置）」的区别：这个的结果只有一个——桌面，
    /// 用户不用去想"它当初是从哪来的"。
    /// </summary>
    public DeleteResult DissolveToDesktop(BoxRecord box, string? desktopOverride = null)
    {
        var desktop = desktopOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktop);
        var moved = 0;
        var failed = 0;
        if (box.Kind != BoxKind.Mapped)
        {
            foreach (var item in _repo.Items(box.Id, false))
            {
                var path = item.StoredPath ?? item.SourcePath;
                var result = _files.MoveTo(path, desktop);
                if (result.Ok) moved++;
                else
                {
                    failed++;
                    Log.Warn($"解散时还原失败：{path} → 桌面（{result.Error}）");
                }
            }
        }
        _repo.DeleteBox(box.Id);
        var stillThere = _repo.Box(box.Id) is not null;
        Log.Info($"解散盒子「{box.Name}」：{moved} 项回到桌面，失败 {failed} 项，记录仍在={stillThere}");
        return new DeleteResult(moved, failed, stillThere);
    }

    /// <summary>存储目录里的文件是否属于"我们自己管的"（桌面分区模式搬进去的必须登记进盒子）。</summary>
    public static bool IsUnderManagedRoot(string path)
    {
        try
        {
            var root = Path.GetFullPath(AppPaths.DefaultManagedRoot);
            var target = Path.GetFullPath(path);
            return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
