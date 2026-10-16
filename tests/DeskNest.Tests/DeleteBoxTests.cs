using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 「删除收纳盒」这条链路的回归：删库里的行、顺手把盒内文件还原回原位置。
/// 用户报过"点了删除但格子还在"，所以这里把每一层都钉住。
/// </summary>
public sealed class DeleteBoxTests : IDisposable
{
    private readonly string _root;
    private readonly Db _db;
    private readonly Repository _repo;
    private readonly FileOps _ops;
    private readonly BoxService _boxes;

    public DeleteBoxTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-del-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
        _ops = new FileOps(_repo);
        _boxes = new BoxService(_repo, _ops);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void DeleteBox_RemovesBoxRow_AndItsItems()
    {
        var box = new BoxRecord { Name = "待删除", StorageRoot = Path.Combine(_root, "store") };
        _repo.Upsert(box);
        Directory.CreateDirectory(box.StorageRoot!);
        var stored = Path.Combine(box.StorageRoot!, "报告.txt");
        File.WriteAllText(stored, "x");
        _repo.Upsert(new ItemRecord
        {
            BoxId = box.Id,
            Kind = ItemKind.File,
            DisplayName = "报告.txt",
            SourcePath = Path.Combine(_root, "源自桌面", "报告.txt"),
            StoredPath = stored,
            Ext = ".txt"
        });

        Assert.Single(_repo.Boxes());
        Assert.Single(_repo.Items(box.Id, false));

        _repo.DeleteBox(box.Id);

        // 盒子必须真的从库里消失，否则界面刷新之后它又会冒出来
        Assert.Empty(_repo.Boxes());
        Assert.Null(_repo.Box(box.Id));
        Assert.Empty(_repo.Items(box.Id, false));
    }

    [Fact]
    public void DeleteBox_DoesNotTouchOtherBoxes()
    {
        var keep = new BoxRecord { Name = "保留的盒子" };
        var drop = new BoxRecord { Name = "要删的盒子" };
        _repo.Upsert(keep);
        _repo.Upsert(drop);

        _repo.DeleteBox(drop.Id);

        var remaining = _repo.Boxes();
        Assert.Single(remaining);
        Assert.Equal(keep.Id, remaining[0].Id);
    }

    [Fact]
    public void DeleteBox_AfterDelete_SurvivesReload()
    {
        // 模拟"删完再读一次"：如果 DELETE 被别人的事务回滚掉，这一步就会露馅
        var box = new BoxRecord { Name = "待删除" };
        _repo.Upsert(box);
        _repo.DeleteBox(box.Id);

        for (var i = 0; i < 3; i++)
            Assert.DoesNotContain(_repo.Boxes(), b => b.Id == box.Id);
    }

    [Fact]
    public void DeleteBox_RestoresFilesFirst()
    {
        // 页面里"删除盒子"的顺序是：先还原文件，再删记录
        var desktop = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(desktop);
        var original = Path.Combine(desktop, "照片.png");

        var box = new BoxRecord { Name = "待删除", StorageRoot = Path.Combine(_root, "store2") };
        _repo.Upsert(box);
        Directory.CreateDirectory(box.StorageRoot!);
        var moved = _ops.MoveIntoStorage(Path.Combine(desktop, "种子.png"), box.StorageRoot!);
        if (!moved.Ok)
        {
            // 没有种子文件时手工造一个，保证测试独立可跑
            File.WriteAllText(original, "x");
            moved = _ops.MoveIntoStorage(original, box.StorageRoot!);
        }
        Assert.True(moved.Ok, moved.Error);

        var item = new ItemRecord
        {
            BoxId = box.Id,
            Kind = ItemKind.File,
            DisplayName = Path.GetFileName(moved.StoredPath!),
            SourcePath = original,
            StoredPath = moved.StoredPath,
            Ext = ".png"
        };
        _repo.Upsert(item);

        foreach (var each in _repo.Items(box.Id, false))
        {
            var restored = _ops.RestoreToOriginal(each.StoredPath ?? each.SourcePath, each.SourcePath);
            Assert.True(restored.Ok, restored.Error);
        }
        _repo.DeleteBox(box.Id);

        Assert.True(File.Exists(original), "文件应该回到原来的位置");
        Assert.Empty(_repo.Boxes());
    }

    [Fact]
    public void BoxService_Delete_ReportsRestoredCount_AndLeavesNothingBehind()
    {
        var desktop = Path.Combine(_root, "desk");
        Directory.CreateDirectory(desktop);
        var source = Path.Combine(desktop, "笔记.md");
        File.WriteAllText(source, "hello");

        var box = new BoxRecord { Name = "整理盒", StorageRoot = Path.Combine(_root, "store3") };
        _repo.Upsert(box);
        Directory.CreateDirectory(box.StorageRoot!);
        var moved = _ops.MoveIntoStorage(source, box.StorageRoot!);
        Assert.True(moved.Ok, moved.Error);
        _repo.Upsert(new ItemRecord
        {
            BoxId = box.Id,
            Kind = ItemKind.File,
            DisplayName = Path.GetFileName(moved.StoredPath!),
            SourcePath = source,
            StoredPath = moved.StoredPath,
            Ext = ".md"
        });

        var outcome = _boxes.Delete(box);

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(0, outcome.Failed);
        Assert.False(outcome.StillInDatabase);   // 这条是"点了删除没反应"的直接体检项
        Assert.Null(_repo.Box(box.Id));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void BoxService_Delete_KeepsOtherBoxesIntact()
    {
        var keep = new BoxRecord { Name = "别动我" };
        var drop = new BoxRecord { Name = "删我" };
        _repo.Upsert(keep);
        _repo.Upsert(drop);

        var outcome = _boxes.Delete(drop);

        Assert.False(outcome.StillInDatabase);
        Assert.Equal(keep.Id, Assert.Single(_repo.Boxes()).Id);
    }

    // ---------- 合并盒子（多盒叠一处） ----------

    [Fact]
    public void 合并盒子_建组_两个盒子都进组_并且只显示选中的那个()
    {
        var main = new BoxRecord { Name = "工作", MarginX = 100, MarginY = 120, Width = 320, Height = 420 };
        var second = new BoxRecord { Name = "娱乐", MarginX = 800, MarginY = 600, Width = 200, Height = 200 };
        _repo.Upsert(main);
        _repo.Upsert(second);

        var group = _boxes.MergeInto(main, second);

        // 组里两个成员，且当前显示的是发起合并的那个盒子
        Assert.Equal(2, group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(main.Id, group.ActiveMemberId);
        Assert.Equal(group.Id, _repo.Box(second.Id)!.GroupId);
        Assert.Equal(group.Id, _repo.Box(main.Id)!.GroupId);

        // 被并入的盒子要挪到组的位置与尺寸上（否则切换成员时会跳位）
        var moved = _repo.Box(second.Id)!;
        Assert.Equal(100, moved.MarginX);
        Assert.Equal(120, moved.MarginY);
        Assert.Equal(320, moved.Width);

        // 关键：同一个组里**只能有一个盒子处于显示状态**，否则两个盒子叠在一起谁也点不到
        Assert.Equal(main.Id, _boxes.ActiveMemberOf(group.Id));
        Assert.Null(_boxes.ActiveMemberOf(null));
    }

    [Fact]
    public void 合并盒子_切换成员后_显示状态跟着变()
    {
        var main = new BoxRecord { Name = "工作" };
        var second = new BoxRecord { Name = "娱乐" };
        _repo.Upsert(main);
        _repo.Upsert(second);
        var group = _boxes.MergeInto(main, second);

        group.ActiveMemberId = second.Id;
        _repo.Upsert(group);

        Assert.Equal(second.Id, _boxes.ActiveMemberOf(group.Id));
    }

    [Fact]
    public void 合并盒子_重复合并同一个盒子不会出现重复成员()
    {
        var main = new BoxRecord { Name = "工作" };
        var second = new BoxRecord { Name = "娱乐" };
        _repo.Upsert(main);
        _repo.Upsert(second);

        _boxes.MergeInto(main, second);
        var again = _boxes.MergeInto(main, second);

        Assert.Equal(2, again.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void 合并盒子_不能把自己并进自己()
    {
        var main = new BoxRecord { Name = "工作" };
        _repo.Upsert(main);
        Assert.Throws<InvalidOperationException>(() => _boxes.MergeInto(main, main));
    }
}
