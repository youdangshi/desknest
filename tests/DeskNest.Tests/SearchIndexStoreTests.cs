using DeskNest.Core.Data;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 搜索索引落盘的回归：曾经出现过"库里的 326 万条被 28 万条顶掉"，
/// 以及并发保存时的 "cannot start a transaction within a transaction"。
/// </summary>
public sealed class SearchIndexStoreTests : IDisposable
{
    private readonly string _root;
    private readonly Db _db;
    private readonly Repository _repo;

    public SearchIndexStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-idx-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static List<SearchService.IndexEntry> Make(int count, string prefix = "file")
        => Enumerable.Range(0, count)
            .Select(i => new SearchService.IndexEntry($"C:\\test\\{prefix}{i}.txt", false, i, DateTime.Now))
            .ToList();

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        _repo.SaveSearchIndex(Make(2500));
        var loaded = _repo.LoadSearchIndex();
        Assert.Equal(2500, loaded.Count);
        Assert.Contains(loaded, e => e.Path.EndsWith("file0.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void SavingEmpty_DoesNotWipeExistingIndex()
    {
        _repo.SaveSearchIndex(Make(120));
        _repo.SaveSearchIndex(Array.Empty<SearchService.IndexEntry>());
        // 空集合绝不能把表清空——那等于用户重启后搜索"什么都没了"
        Assert.Equal(120, _repo.LoadSearchIndex().Count);
    }

    [Fact]
    public void SavingTwice_ReplacesInsteadOfAppending()
    {
        _repo.SaveSearchIndex(Make(100, "a"));
        _repo.SaveSearchIndex(Make(60, "b"));
        var loaded = _repo.LoadSearchIndex();
        Assert.Equal(60, loaded.Count);
        Assert.All(loaded, e => Assert.Contains("\\b", e.Path));
    }

    [Fact]
    public void ConcurrentSaves_DoNotNestTransactions()
    {
        // 旧实现里 BEGIN/COMMIT 是两条独立语句，两个线程同时保存会撞出
        // "cannot start a transaction within a transaction"，进而丢掉整份索引
        var errors = new List<Exception>();
        var threads = Enumerable.Range(0, 3).Select(n => new Thread(() =>
        {
            try { _repo.SaveSearchIndex(Make(1500, "t" + n)); }
            catch (Exception ex) { lock (errors) errors.Add(ex); }
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.True(errors.Count == 0, "并发保存抛异常：" + string.Join(" / ", errors.Select(e => e.Message)));
        Assert.Equal(1500, _repo.LoadSearchIndex().Count);
    }
}
