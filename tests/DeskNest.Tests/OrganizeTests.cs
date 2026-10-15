using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

public sealed class OrganizeEngineTests : IDisposable
{
    private readonly string _root;
    private readonly Db _db;
    private readonly Repository _repo;
    private readonly FileOps _ops;
    private readonly OrganizeEngine _engine;

    public OrganizeEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-org-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
        _ops = new FileOps(_repo);
        _engine = new OrganizeEngine(_repo, _ops);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Desktop()
    {
        var desktop = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(desktop);
        return desktop;
    }

    [Fact]
    public void BuildPlan_SkipsShortcutsByDefault_AndKeepsThemWithOptionOn()
    {
        var desktop = Desktop();
        File.WriteAllText(Path.Combine(desktop, "报告.txt"), "x");
        File.WriteAllText(Path.Combine(desktop, "微信.lnk"), "x");

        var planWithout = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, ManagedRoot: Path.Combine(_root, "managed")));
        Assert.Single(planWithout.Items);
        Assert.Single(planWithout.Skipped);
        Assert.Contains("快捷方式", planWithout.Skipped[0]);

        var planWith = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, IncludeShortcuts: true,
            ManagedRoot: Path.Combine(_root, "managed")));
        Assert.Equal(2, planWith.Items.Count);
    }

    [Fact]
    public void BuildPlan_ClassifiesByType_AndNeverTouchesHiddenFiles()
    {
        var desktop = Desktop();
        File.WriteAllText(Path.Combine(desktop, "a.png"), "x");
        File.WriteAllText(Path.Combine(desktop, "b.zip"), "x");
        File.WriteAllText(Path.Combine(desktop, "c.docx"), "x");
        var hidden = Path.Combine(desktop, "隐藏.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var plan = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, ManagedRoot: Path.Combine(_root, "managed")));

        Assert.Equal(3, plan.Items.Count);
        Assert.Contains(plan.Items, i => i.Category == "图片");
        Assert.Contains(plan.Items, i => i.Category == "压缩");
        Assert.Contains(plan.Items, i => i.Category == "文档");
        Assert.Contains(plan.Skipped, s => s.Contains("隐藏"));
    }

    [Fact]
    public void Execute_ThenUndo_RestoresEveryFileToOriginalPlace()
    {
        var desktop = Desktop();
        var files = new[] { "一.png", "二.png", "三.txt" };
        foreach (var f in files) File.WriteAllText(Path.Combine(desktop, f), f);

        var managed = Path.Combine(_root, "managed");
        var plan = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, ManagedRoot: managed));
        var run = _engine.Execute(plan, "test");

        Assert.Equal(3, run.MovedCount);
        foreach (var f in files) Assert.False(File.Exists(Path.Combine(desktop, f)));

        var (restored, failed) = _engine.Undo(run);

        Assert.Equal(3, restored);
        Assert.Equal(0, failed);
        foreach (var f in files) Assert.True(File.Exists(Path.Combine(desktop, f)), f + " 没有被还原");
    }

    [Fact]
    public void Undo_IsIdempotent()
    {
        var desktop = Desktop();
        File.WriteAllText(Path.Combine(desktop, "x.txt"), "x");
        var plan = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, ManagedRoot: Path.Combine(_root, "managed")));
        var run = _engine.Execute(plan, "test");

        var first = _engine.Undo(run);
        var second = _engine.Undo(run);

        Assert.Equal(1, first.Restored);
        Assert.Equal(0, second.Restored);   // 第二次什么都不做，也不报错
        Assert.Equal(0, second.Failed);
    }

    [Fact]
    public void Execute_RegistersItemsIntoExistingBox()
    {
        var desktop = Desktop();
        File.WriteAllText(Path.Combine(desktop, "图片样例.png"), "x");
        var box = new BoxRecord { Name = "图片", Kind = BoxKind.Normal };
        box.StorageRoot = Path.Combine(_root, "managed", "图片");
        _repo.Upsert(box);

        var plan = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, ManagedRoot: Path.Combine(_root, "managed")));
        var run = _engine.Execute(plan, "test");

        Assert.Equal(1, run.MovedCount);
        var items = _repo.Items(box.Id, false);
        Assert.Single(items);
        Assert.Equal("图片样例.png", items[0].DisplayName);
    }

    [Fact]
    public void FolderMode_CreatesTypeFoldersInsideSourceDirectory()
    {
        var desktop = Desktop();
        File.WriteAllText(Path.Combine(desktop, "歌.mp3"), "x");

        var plan = _engine.BuildPlan(new OrganizeEngine.ScanOptions(desktop, UseFolderMode: true));
        var run = _engine.Execute(plan, "test");

        Assert.Equal(1, run.MovedCount);
        Assert.True(File.Exists(Path.Combine(desktop, "音频", "歌.mp3")));
    }
}

public sealed class RulesEngineTests
{
    [Theory]
    [InlineData("a.png", FileCategory.Image)]
    [InlineData("a.JPG", FileCategory.Image)]
    [InlineData("a.zip", FileCategory.Archive)]
    [InlineData("a.docx", FileCategory.Document)]
    [InlineData("a.mp4", FileCategory.Video)]
    [InlineData("a.mp3", FileCategory.Audio)]
    [InlineData("a.exe", FileCategory.App)]
    [InlineData("a.lnk", FileCategory.Shortcut)]
    [InlineData("a.unknown", FileCategory.Other)]
    public void Classify_WorksByExtension(string file, FileCategory expected)
        => Assert.Equal(expected, RulesEngine.Classify(Path.Combine(Path.GetTempPath(), file)));

    [Fact]
    public void Matches_ByExtensionAndKeyword()
    {
        var file = Path.Combine(Path.GetTempPath(), "desknest-rule-" + Guid.NewGuid().ToString("n") + ".txt");
        File.WriteAllText(file, "hello");
        try
        {
            var conditions = new RuleConditions { Extensions = new List<string> { ".txt" }, NameContains = new List<string> { "desknest" } };
            Assert.True(RulesEngine.Matches(conditions, file, DateTimeOffset.Now));

            conditions.NameContains = new List<string> { "不存在的关键词" };
            Assert.False(RulesEngine.Matches(conditions, file, DateTimeOffset.Now));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Matches_RespectsSizeLimits()
    {
        var file = Path.Combine(Path.GetTempPath(), "desknest-size-" + Guid.NewGuid().ToString("n") + ".bin");
        File.WriteAllBytes(file, new byte[2048]);
        try
        {
            Assert.False(RulesEngine.Matches(new RuleConditions { MinSizeBytes = 4096 }, file, DateTimeOffset.Now));
            Assert.True(RulesEngine.Matches(new RuleConditions { MinSizeBytes = 1024 }, file, DateTimeOffset.Now));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ConditionsJson_RoundTrips()
    {
        var c = new RuleConditions { Extensions = new List<string> { ".png" }, OlderThanDays = 7 };
        var json = RulesEngine.ToJson(c);
        var back = RulesEngine.ParseConditions(json);
        Assert.Single(back.Extensions);
        Assert.Equal(7, back.OlderThanDays);
    }
}
