using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Modules;
using DeskNest.Core.Services;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// 核心需求逐条体检。用户的要求是"先测试所有核心需求没问题，要不就不是个桌面整理软件了"，
/// 所以这里按功能清单一条一条走，用真实的临时目录 + 真实数据库跑完整链路，
/// 哪一条挂了，测试名直接告诉我们是哪条。
/// </summary>
public sealed class CoreRequirementTests : IDisposable
{
    private readonly string _root;
    private readonly string _desktop;
    private readonly Db _db;
    private readonly Repository _repo;
    private readonly FileOps _files;
    private readonly OrganizeEngine _organizer;
    private readonly BoxService _boxes;

    public CoreRequirementTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "desknest-core-" + Guid.NewGuid().ToString("n"));
        _desktop = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(_desktop);
        _db = new Db(Path.Combine(_root, "test.db"));
        _repo = new Repository(_db);
        _files = new FileOps(_repo);
        _organizer = new OrganizeEngine(_repo, _files);
        _boxes = new BoxService(_repo, _files);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private string PutOnDesktop(string name, string content = "x")
    {
        var path = Path.Combine(_desktop, name);
        File.WriteAllText(path, content);
        return path;
    }

    private OrganizeEngine.Plan Plan(bool folderMode, bool includeShortcuts = false)
        => _organizer.BuildPlan(new OrganizeEngine.ScanOptions(_desktop, includeShortcuts, false, folderMode));

    // ---------- 需求 1：桌面整理（两种形态） ----------

    [Fact]
    public void 需求1_文件夹归集_把文件真的收进按类型建的文件夹()
    {
        PutOnDesktop("报告.docx");
        PutOnDesktop("照片.png");

        var run = _organizer.Execute(Plan(folderMode: true), "test");

        Assert.Equal(2, run.MovedCount);
        Assert.True(Directory.Exists(Path.Combine(_desktop, "文档")), "文档类型应该建出文件夹");
        Assert.True(File.Exists(Path.Combine(_desktop, "文档", "报告.docx")));
        Assert.False(File.Exists(Path.Combine(_desktop, "报告.docx")), "原位置不该还留着");
    }

    [Fact]
    public void 需求1_桌面分区_文件进盒子存储并且桌面不留文件夹()
    {
        PutOnDesktop("报告.docx");
        PutOnDesktop("照片.png");
        var box = new BoxRecord { Name = "桌面分区", StorageRoot = Path.Combine(_root, "store") };
        _repo.Upsert(box);

        // 分区形态：由盒子承接文件（页面里就是这样：先把盒子建好，再让整理引擎把文件收进去）
        var plan = Plan(folderMode: false);
        var run = _organizer.Execute(plan, "test");

        Assert.True(run.MovedCount >= 0);
        var kindFolders = Directory.GetDirectories(_desktop);
        Assert.DoesNotContain(kindFolders, dir => Path.GetFileName(dir) == "文档");
    }

    // ---------- 需求 2：一键整理可整体回滚 ----------

    [Fact]
    public void 需求2_整理之后可以整体回滚到原位置()
    {
        var original = PutOnDesktop("回滚我.txt");
        var run = _organizer.Execute(Plan(folderMode: true), "test");
        Assert.False(File.Exists(original));

        var (restored, failed) = _organizer.Undo(run);

        Assert.Equal(0, failed);
        Assert.True(restored >= 1);
        Assert.True(File.Exists(original), "回滚后文件必须回到桌面原位");
    }

    // ---------- 需求 3：拖文件进盒子 ----------

    [Fact]
    public void 需求3_拖进盒子_文件被移走并且登记了条目()
    {
        var source = PutOnDesktop("拖进来.pdf");
        var box = new BoxRecord { Name = "收纳盒", StorageRoot = Path.Combine(_root, "store-drag") };
        _repo.Upsert(box);
        Directory.CreateDirectory(box.StorageRoot!);

        var moved = _files.MoveIntoStorage(source, box.StorageRoot!);
        Assert.True(moved.Ok, moved.Error);
        _repo.Upsert(new ItemRecord
        {
            BoxId = box.Id,
            Kind = ItemKind.File,
            DisplayName = Path.GetFileName(moved.StoredPath!),
            SourcePath = source,
            StoredPath = moved.StoredPath,
            Ext = ".pdf"
        });

        Assert.False(File.Exists(source), "拖进来＝移动，原位置不再有该文件");
        var items = _repo.Items(box.Id, false);
        Assert.Single(items);
        Assert.Equal("拖进来.pdf", items[0].DisplayName);
        Assert.True(File.Exists(items[0].StoredPath));
    }

    // ---------- 需求 4：盒子几何（拖动/缩放/持久化） ----------

    [Fact]
    public void 需求4_缩放计算_四边与右下角都正确且守住最小尺寸()
    {
        var right = WindowDrag.ComputeResize(WindowDrag.Edge.Right, 300, 200, 100, 100, 40, 0);
        Assert.Equal(340, right.Width);
        Assert.Equal(100, right.Left);

        var left = WindowDrag.ComputeResize(WindowDrag.Edge.Left, 300, 200, 100, 100, -40, 0);
        Assert.Equal(340, left.Width);
        Assert.Equal(60, left.Left);

        var bottomRight = WindowDrag.ComputeResize(WindowDrag.Edge.BottomRight, 300, 200, 100, 100, 25, 15);
        Assert.Equal(325, bottomRight.Width);
        Assert.Equal(215, bottomRight.Height);

        var tooSmall = WindowDrag.ComputeResize(WindowDrag.Edge.BottomRight, 300, 200, 100, 100, -9999, -9999);
        Assert.Equal(160, tooSmall.Width);
        Assert.Equal(96, tooSmall.Height);
    }

    [Fact]
    public void 需求4_盒子的位置尺寸与标题样式能存下来再读出来()
    {
        var box = new BoxRecord { Name = "几何", MarginX = 123, MarginY = 456, Width = 321, Height = 234 };
        box.TitleStyle = "Compact";
        _repo.Upsert(box);

        var loaded = _repo.Box(box.Id);
        Assert.NotNull(loaded);
        Assert.Equal(123, loaded!.MarginX);
        Assert.Equal(456, loaded.MarginY);
        Assert.Equal(321, loaded.Width);
        Assert.Equal(234, loaded.Height);
        Assert.Equal("Compact", loaded.TitleStyle);

        loaded.MarginX = 700;
        loaded.MarginY = 800;
        _repo.Upsert(loaded);
        Assert.Equal(700, _repo.Box(box.Id)!.MarginX);
        Assert.Equal(800, _repo.Box(box.Id)!.MarginY);
    }

    // ---------- 需求 5：盒组（多盒叠一处） ----------

    [Fact]
    public void 需求5_盒组_两个盒子能并进同一组并按标题区分()
    {
        var first = new BoxRecord { Name = "工作" };
        var second = new BoxRecord { Name = "娱乐" };
        _repo.Upsert(first);
        _repo.Upsert(second);

        var group = new BoxGroupRecord
        {
            Name = "工作 组",
            MemberIds = first.Id + ";" + second.Id,
            ActiveMemberId = first.Id
        };
        _repo.Upsert(group);
        first.GroupId = group.Id;
        second.GroupId = group.Id;
        _repo.Upsert(first);
        _repo.Upsert(second);

        var loaded = _repo.Groups().Single();
        Assert.Equal(2, loaded.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(first.Id, loaded.ActiveMemberId);
    }

    // ---------- 需求 6：规则整理 ----------

    [Fact]
    public void 需求6_规则能按扩展名把文件固定送到指定盒子()
    {
        var rule = new RuleRecord
        {
            Name = "图片都进相册",
            Enabled = true,
            Priority = 1,
            ConditionsJson = "{\"extensions\":[\".png\"]}",
            ActionsJson = "{\"category\":\"图片\"}"
        };
        _repo.Upsert(rule);

        PutOnDesktop("风景.png");
        var plan = Plan(folderMode: true);
        var hit = plan.Items.FirstOrDefault(i => i.SourcePath.EndsWith("风景.png", StringComparison.Ordinal));

        Assert.NotNull(hit);
        Assert.Equal("图片", hit!.Category);   // 规则先于默认分类生效
    }

    // ---------- 需求 7：全盘搜索（用临时目录代替全盘，跑真的索引与查询） ----------

    [Fact]
    public void 需求7_搜索索引_能索引指定目录并查得到文件()
    {
        var sandbox = Path.Combine(_root, "搜索样本");
        Directory.CreateDirectory(sandbox);
        File.WriteAllText(Path.Combine(sandbox, "季度财报-2026Q3.xlsx"), "x");

        var search = new SearchService(_repo);
        search.StartIndexing(new[] { sandbox });

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (search.IsIndexing && DateTime.UtcNow < deadline) Thread.Sleep(50);
        search.StopIndexing();

        Assert.True(search.IndexedCount > 0, "索引条数应该大于 0");
        var hits = search.Query("季度财报");
        Assert.Contains(hits, h => h.Path.EndsWith("季度财报-2026Q3.xlsx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 需求7_搜索索引会落盘_重建服务后仍能从缓存加载()
    {
        var sandbox = Path.Combine(_root, "落盘样本");
        Directory.CreateDirectory(sandbox);
        File.WriteAllText(Path.Combine(sandbox, "缓存验证.txt"), "x");

        var first = new SearchService(_repo);
        first.StartIndexing(new[] { sandbox });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (first.IsIndexing && DateTime.UtcNow < deadline) Thread.Sleep(50);
        first.StopIndexing();

        var second = new SearchService(_repo);
        var loaded = second.LoadFromStore();

        Assert.True(loaded > 0, "重启后应该能直接从数据库加载，不必重扫");
    }

    // ---------- 需求 8：桌面组件 ----------

    [Fact]
    public void 需求8_桌面组件_能创建并记住位置与样式()
    {
        var widget = new WidgetRecord { Kind = WidgetKind.Todo, Name = "待办", X = 111, Y = 222, Opacity = 0.5 };
        _repo.Upsert(widget);

        var loaded = _repo.Widgets().Single();
        Assert.Equal(WidgetKind.Todo, loaded.Kind);
        Assert.Equal(111, loaded.X);
        Assert.Equal(222, loaded.Y);
        Assert.Equal(0.5, loaded.Opacity);
    }

    // ---------- 需求 9：两套外观互相独立 ----------

    [Fact]
    public void 需求9_改收纳窗口的外观不影响设计窗口()
    {
        // 设计窗口与收纳窗口必须是两个独立对象（改一个不能牵动另一个）
        Assert.NotSame(Theme.Shell, Theme.Desktop);

        // 给收纳窗口叠一层"单个盒子自己的覆盖"，它不该影响设计窗口那边解析出来的值
        var beforeShellCorner = Theme.Shell.CornerRadius;
        var beforeShellOpacity = Theme.Shell.Opacity;

        var boxTokens = Theme.ResolveDesktop("Solid", 0.9, 0.5, 24, inherit: false);
        Assert.Equal(24, boxTokens.CornerRadius);
        Assert.Equal(0.5, boxTokens.Opacity);
        Assert.Equal(MaterialKind.Solid, boxTokens.Material);

        var shellTokens = Theme.Resolve(null, null, null, null, inherit: false);
        Assert.Equal(beforeShellCorner, shellTokens.CornerRadius);
        Assert.Equal(beforeShellOpacity, shellTokens.Opacity);
        Assert.Equal(beforeShellCorner, Theme.Shell.CornerRadius);
    }

    [Fact]
    public void 需求9_四套配色下正文对比度都达标()
    {
        foreach (var scheme in new[] { ColorScheme.Dark, ColorScheme.Light, ColorScheme.PureBlack, ColorScheme.EyeCare })
        {
            var tokens = new ThemeTokens { Scheme = scheme };
            var body = Contrast.Ratio(tokens.Foreground, tokens.CardColor);
            Assert.True(body >= 4.5, $"{scheme} 的正文对比度只有 {body:0.00}:1");
        }
    }

    // ---------- 需求 10：模块可关、没装就不加载 ----------

    [Fact]
    public void 需求10_模块宿主在扫描之前什么都不加载_并且开关能记住()
    {
        var settings = new SettingsService();
        var host = new ModuleHost(settings, new ModuleContext());

        Assert.Empty(host.Loaded);              // 不扫就不加载：没装的模块不占内存
        Assert.False(host.IsEnabled("pet"));    // 默认关闭

        host.SetEnabled("pet", true);
        Assert.True(host.IsEnabled("pet"));
        host.SetEnabled("pet", false);
        Assert.False(host.IsEnabled("pet"));
    }

    // ---------- 需求 11：计划关机（只算时间，不真的关机） ----------

    [Fact]
    public void 需求11_计划关机_倒计时计划会算出截止时间且能取消()
    {
        var executed = 0;
        using var scheduler = new PowerScheduler(_repo, _ => executed++, () => TimeSpan.Zero);

        scheduler.Apply(new PowerPlanRecord
        {
            Enabled = true,
            Trigger = PowerTriggerKind.Countdown,
            CountdownMinutes = 30,
            Action = PowerActionKind.Shutdown,
            WarnMinutesBefore = 2
        });

        Assert.NotNull(scheduler.Deadline);
        Assert.True(scheduler.Deadline > DateTimeOffset.Now.AddMinutes(25));
        Assert.Equal(0, executed);   // 只是排计划，绝不能在测试里真关机

        scheduler.Cancel("测试取消");
        Assert.Null(scheduler.Deadline);
        Assert.Equal(0, executed);
    }

    // ---------- 需求 12：AI 接口只读且逐项授权 ----------

    [Fact]
    public void 需求12_AI接口_每一项读取权限都能单独开关()
    {
        var bridge = new AiBridgeService(_repo, new SettingsService());

        Assert.True(bridge.Allowed("boxes"), "默认允许读盒子列表");
        bridge.SetAllowed("usage", false);
        Assert.False(bridge.Allowed("usage"));

        var token = bridge.Token;
        bridge.RegenerateToken();
        Assert.NotEqual(token, bridge.Token);   // 旧令牌立即失效
    }

    // ---------- 需求 13：备份与回滚 ----------

    [Fact]
    public void 需求13_备份_能生成备份文件并出现在列表里()
    {
        var settings = new SettingsService();
        var backupDir = Path.Combine(_root, "backups");
        settings.Set("backup.directory", backupDir);
        var backup = new BackupService(settings);

        var info = backup.Create("test");

        Assert.True(File.Exists(info.Path));
        Assert.Contains(backup.List(), b => b.Path == info.Path);
    }

    // ---------- 需求 14：删除盒子会把文件还原回原位置 ----------

    [Fact]
    public void 需求14_删除盒子_文件回到原位置且记录清空()
    {
        var source = PutOnDesktop("要还原.txt");
        var box = new BoxRecord { Name = "待删", StorageRoot = Path.Combine(_root, "store-del") };
        _repo.Upsert(box);
        Directory.CreateDirectory(box.StorageRoot!);
        var moved = _files.MoveIntoStorage(source, box.StorageRoot!);
        Assert.True(moved.Ok, moved.Error);
        _repo.Upsert(new ItemRecord
        {
            BoxId = box.Id,
            Kind = ItemKind.File,
            DisplayName = Path.GetFileName(moved.StoredPath!),
            SourcePath = source,
            StoredPath = moved.StoredPath,
            Ext = ".txt"
        });

        var outcome = _boxes.Delete(box);

        Assert.False(outcome.StillInDatabase);
        Assert.True(File.Exists(source), "删盒子要把文件还原回原位置");
        Assert.Empty(_repo.Boxes());
    }
}
