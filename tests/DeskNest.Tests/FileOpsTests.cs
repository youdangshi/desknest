using DeskNest.Core.Data;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

/// <summary>文件安全操作的测试：这是整个产品最不能出错的部分。</summary>
public sealed class FileOpsTests : IDisposable
{
    private readonly string _root;
    private readonly Db _db;
    private readonly Repository _repo;
    private readonly FileOps _ops;

    public FileOpsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
        _ops = new FileOps(_repo);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string NewFile(string name, string content = "hello")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void UniquePath_AppendsCounter_OnConflict()
    {
        var dir = Path.Combine(_root, "box");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "1");
        var second = FileOps.UniquePath(dir, "a.txt");
        Assert.Equal("a (1).txt", Path.GetFileName(second));
        File.WriteAllText(second, "2");
        var third = FileOps.UniquePath(dir, "a.txt");
        Assert.Equal("a (2).txt", Path.GetFileName(third));
    }

    [Fact]
    public void MoveIntoStorage_MovesFile_AndReportsNewPath()
    {
        var source = NewFile("桌面文件.txt");
        var storage = Path.Combine(_root, "storage");

        var result = _ops.MoveIntoStorage(source, storage);

        Assert.True(result.Ok, result.Error);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(result.StoredPath!));
        Assert.Equal("桌面文件.txt", Path.GetFileName(result.StoredPath!));
    }

    [Fact]
    public void MoveIntoStorage_RefusesPathAlreadyInsideStorage()
    {
        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(storage);
        var inside = Path.Combine(storage, "already.txt");
        File.WriteAllText(inside, "x");

        var result = _ops.MoveIntoStorage(inside, storage);

        Assert.False(result.Ok);
        Assert.True(File.Exists(inside));
    }

    [Fact]
    public void RestoreToOriginal_PutsFileBack_AndKeepsSourceIntactWhenTargetExists()
    {
        var source = NewFile("原始位置.txt", "old");
        var storage = Path.Combine(_root, "storage");
        var moved = _ops.MoveIntoStorage(source, storage);
        Assert.True(moved.Ok);

        // 原位置又出现同名文件 → 还原时必须自动改名，不能覆盖
        NewFile("原始位置.txt", "new");
        var restored = _ops.RestoreToOriginal(moved.StoredPath!, source);

        Assert.True(restored.Ok, restored.Error);
        Assert.Equal("old", File.ReadAllText(restored.StoredPath!));
        Assert.Equal("new", File.ReadAllText(source));
        Assert.Contains("(1)", Path.GetFileName(restored.StoredPath!));
    }

    [Fact]
    public void RestoreToOriginal_FallsBackToDesktop_WhenOriginalDirectoryMissing()
    {
        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(storage);
        var moved = Path.Combine(storage, "孤儿文件.txt");
        File.WriteAllText(moved, "x");
        var missingOriginal = Path.Combine(_root, "不存在的目录", "孤儿文件.txt");

        var restored = _ops.RestoreToOriginal(moved, missingOriginal);

        Assert.True(restored.Ok, restored.Error);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Assert.StartsWith(desktop, restored.StoredPath!, StringComparison.OrdinalIgnoreCase);
        File.Delete(restored.StoredPath!);   // 清理测试产物
    }

    [Fact]
    public void MoveTo_RefusesNonExistingSource()
    {
        var result = _ops.MoveTo(Path.Combine(_root, "没有这个文件.txt"), Path.Combine(_root, "dest"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void IsUnder_DetectsNestedPaths()
    {
        Assert.True(FileOps.IsUnder(Path.Combine(_root, "a", "b.txt"), _root));
        Assert.False(FileOps.IsUnder(Path.Combine(_root + "-other", "b.txt"), _root));
    }
}
