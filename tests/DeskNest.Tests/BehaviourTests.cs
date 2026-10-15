using System.IO.Compression;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;
using Xunit;

namespace DeskNest.Tests;

public sealed class PetEngineTests
{
    [Fact]
    public void KeyboardFollow_IsOffByDefault()
    {
        var pet = new PetEngine();
        Assert.False(pet.KeyboardFollow);
        pet.OnKeyPress();
        Assert.Equal(PetState.Idle, pet.Current);   // 默认不响应键盘
    }

    [Fact]
    public void KeyboardFollow_EntersTypingState_WhenEnabled()
    {
        var pet = new PetEngine { KeyboardFollow = true };
        pet.OnKeyPress();
        Assert.Equal(PetState.Typing, pet.Current);
    }

    [Fact]
    public void Cooldown_DropsRapidRepeatEvents()
    {
        var pet = new PetEngine { ClickReaction = true };
        pet.OnClick();
        Assert.Equal(PetState.Click, pet.Current);
        pet.Tick();
        pet.OnClick();                       // 冷却期内重复点击不应改变状态
        Assert.Equal(PetState.Click, pet.Current);
    }

    [Fact]
    public void TodoReminder_RaisesBubbleWithActionPayload()
    {
        var pet = new PetEngine { TodoReminder = true };
        PetBubble? captured = null;
        pet.Bubble += b => captured = b;

        pet.RemindTodo("交策划书", "todo-1");

        Assert.NotNull(captured);
        Assert.Equal("todo", captured!.ActionKind);
        Assert.Equal("todo-1", captured.ActionPayload);
        Assert.Equal(PetState.Remind, pet.Current);
    }

    [Fact]
    public void HighCpu_TriggersTiredState_OnlyWhenSystemReactionEnabled()
    {
        var sample = new PerfSample(95, 40, 1, 2, 0, 0, null, null, null, DateTimeOffset.Now);
        var pet = new PetEngine { SystemReaction = false };
        pet.OnSystemSample(sample);
        Assert.NotEqual(PetState.Tired, pet.Current);

        pet.SystemReaction = true;
        pet.OnSystemSample(sample);
        Assert.Equal(PetState.Tired, pet.Current);
    }

    [Fact]
    public void Idle_TriggersSleepOnlyAfterTenMinutes()
    {
        var pet = new PetEngine { IdleReaction = true };
        pet.OnIdle(TimeSpan.FromMinutes(1));
        Assert.NotEqual(PetState.Sleep, pet.Current);
        pet.OnIdle(TimeSpan.FromMinutes(12));
        Assert.Equal(PetState.Sleep, pet.Current);
    }
}

public sealed class UsageStatsTests : IDisposable
{
    private readonly string _root;
    private readonly Db _db;
    private readonly Repository _repo;

    public UsageStatsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-usage-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Aggregate_SumsSessionsPerProcess()
    {
        var now = DateTimeOffset.Now;
        _repo.InsertUsage(new UsageSessionRecord { ProcessName = "Code", StartAt = now.AddMinutes(-30), EndAt = now.AddMinutes(-10), DurationSeconds = 1200 });
        _repo.InsertUsage(new UsageSessionRecord { ProcessName = "Code", StartAt = now.AddMinutes(-9), EndAt = now, DurationSeconds = 540 });
        _repo.InsertUsage(new UsageSessionRecord { ProcessName = "Chrome", StartAt = now.AddMinutes(-5), EndAt = now, DurationSeconds = 300 });

        var settings = new TestSettings();
        var service = new UsageStatsService(_repo, settings, () => null);
        var result = service.Aggregate(now.AddHours(-2), now);

        Assert.Equal(2, result.Count);
        Assert.Equal("Code", result[0].ProcessName);
        Assert.Equal(1740, result[0].Seconds);
        Assert.Equal(2, result[0].Sessions);
        Assert.True(result[0].Share > 60);
    }

    private sealed class TestSettings : Core.Services.SettingsService
    {
    }
}

public sealed class PowerSchedulerTests
{
    [Fact]
    public void CountdownPlan_SetsDeadline_AndCancelClearsIt()
    {
        using var db = new Db(Path.Combine(Path.GetTempPath(), "desknest-power-" + Guid.NewGuid().ToString("n") + ".db"));
        var repo = new Repository(db);
        var executed = false;
        var scheduler = new PowerScheduler(repo, _ => executed = true, () => TimeSpan.Zero);

        scheduler.Apply(new PowerPlanRecord { Enabled = true, Trigger = PowerTriggerKind.Countdown, CountdownMinutes = 30 });
        Assert.NotNull(scheduler.Deadline);
        Assert.False(executed);

        scheduler.Cancel("测试取消");
        Assert.Null(scheduler.Deadline);
        Assert.False(executed);
    }

    [Fact]
    public void ClockPlan_ComputesNextOccurrenceInFuture()
    {
        using var db = new Db(Path.Combine(Path.GetTempPath(), "desknest-power2-" + Guid.NewGuid().ToString("n") + ".db"));
        var repo = new Repository(db);
        var scheduler = new PowerScheduler(repo, _ => { }, () => TimeSpan.Zero);

        scheduler.Apply(new PowerPlanRecord { Enabled = true, Trigger = PowerTriggerKind.Clock, ClockTime = "23:59", WeekdaysOnly = false });

        Assert.NotNull(scheduler.Deadline);
        Assert.True(scheduler.Deadline!.Value > DateTimeOffset.Now);
    }
}

public sealed class SearchServiceTests : IDisposable
{
    private readonly string _root;

    public SearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-search-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "策划书草稿.md"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "子目录"));
        File.WriteAllText(Path.Combine(_root, "子目录", "素材包.petpack"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void IndexingAndQuery_FindsFilesByName()
    {
        using var search = new SearchService();
        search.StartIndexing(new[] { _root });

        var deadline = DateTime.Now.AddSeconds(10);
        while (search.IndexedCount < 2 && DateTime.Now < deadline) Thread.Sleep(50);

        var hits = search.Query("策划书");
        Assert.Contains(hits, h => h.Title == "策划书草稿.md");

        var sub = search.Query("petpack");
        Assert.Contains(sub, h => h.Title == "素材包.petpack");
    }

    [Fact]
    public void Query_WithSlash_ReturnsCommands()
    {
        using var search = new SearchService();
        var hits = search.Query("/整理");
        Assert.Contains(hits, h => h.Kind == SearchItemKind.Command && h.Path == "cmd://organize");
    }

    [Fact]
    public void EmptyQuery_ReturnsCommandsOnly()
    {
        using var search = new SearchService();
        var hits = search.Query("");
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal(SearchItemKind.Command, h.Kind));
    }
}

public sealed class PetPackTests : IDisposable
{
    private readonly string _root;

    public PetPackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-petpack-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Import_RejectsArchiveWithoutManifest()
    {
        var zip = Path.Combine(_root, "bad.petpack");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = z.CreateEntry("idle.png");
            using var s = entry.Open();
            s.WriteByte(1);
        }

        var (ok, message) = PetPackStore.Import(zip);

        Assert.False(ok);
        Assert.Contains("manifest", message);
    }

    [Fact]
    public void Import_AcceptsValidPack_AndRejectsPathTraversal()
    {
        var zip = Path.Combine(_root, "good.petpack");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var manifest = z.CreateEntry("manifest.json");
            using (var w = new StreamWriter(manifest.Open()))
                w.Write("{\"format\":\"petpack/1\",\"id\":\"test-pack\",\"name\":\"测试素材包\",\"author\":\"me\",\"license\":\"MIT\"}");
            var evil = z.CreateEntry("../evil.txt");
            using (var w = new StreamWriter(evil.Open())) w.Write("nope");
        }

        var (ok, message) = PetPackStore.Import(zip);

        Assert.True(ok, message);
        Assert.Contains("测试素材包", message);
        var escaped = Path.Combine(AppPaths.PetsRoot, "evil.txt");
        Assert.False(File.Exists(escaped));   // 目录穿越被拦住
        PetPackStore.Remove("test-pack");
    }
}
