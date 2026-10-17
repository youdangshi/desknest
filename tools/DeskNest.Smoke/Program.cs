using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Core.Services;

// 冒烟验证器：不依赖 dotnet test 的宿主（这台机器上 VSTest 的 testhost 现在会 Access Denied），
// 用签名宿主（dotnet exec）直接跑核心逻辑的断言，专门用来快速验证"收纳盒合并"这类易错流程。
var root = Path.Combine(Path.GetTempPath(), "desknest-smoke-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(root);
var failures = new List<string>();
var passed = 0;

void Check(string name, Func<bool> assert)
{
    try
    {
        if (assert()) { passed++; Console.WriteLine($"  PASS  {name}"); }
        else { failures.Add(name); Console.WriteLine($"  FAIL  {name}"); }
    }
    catch (Exception ex)
    {
        failures.Add(name + "（异常：" + ex.Message + "）");
        Console.WriteLine($"  FAIL  {name} —— 异常：{ex.Message}");
    }
}

using var db = new Db(Path.Combine(root, "smoke.db"));
var repo = new Repository(db);
var files = new FileOps(repo);
var boxes = new BoxService(repo, files);

Console.WriteLine("== 收纳盒合并 ==");
var main = new BoxRecord { Name = "工作", MarginX = 100, MarginY = 120, Width = 320, Height = 420 };
var second = new BoxRecord { Name = "娱乐", MarginX = 800, MarginY = 600, Width = 200, Height = 200 };
repo.Upsert(main);
repo.Upsert(second);

var group = boxes.MergeInto(main, second);
Check("合并后两个盒子都在同一个组里",
    () => repo.Box(main.Id)!.GroupId == group.Id && repo.Box(second.Id)!.GroupId == group.Id);
Check("组里正好两个成员",
    () => group.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length == 2);
Check("当前显示的是发起合并的那个盒子", () => group.ActiveMemberId == main.Id);
Check("被并入的盒子挪到了组的位置", () => repo.Box(second.Id)!.MarginX == 100 && repo.Box(second.Id)!.MarginY == 120);
Check("被并入的盒子尺寸跟随组（切换时不跳位）", () => repo.Box(second.Id)!.Width == 320);
Check("同一组只认一个可见成员", () => boxes.ActiveMemberOf(group.Id) == main.Id);
Check("没有组的盒子没有可见成员（不会被误判成隐藏）", () => boxes.ActiveMemberOf(null) is null);

group.ActiveMemberId = second.Id;
repo.Upsert(group);
Check("切换成员后可见成员跟着变", () => boxes.ActiveMemberOf(group.Id) == second.Id);

var again = boxes.MergeInto(main, second);
Check("重复合并不会产生重复成员",
    () => again.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length == 2);
Check("不能把盒子并进它自己",
    () => { try { boxes.MergeInto(main, main); return false; } catch (InvalidOperationException) { return true; } });

Console.WriteLine("== 删除盒子（回归） ==");
var throwaway = new BoxRecord { Name = "临时" };
repo.Upsert(throwaway);
var outcome = boxes.Delete(throwaway);
Check("删除后记录不再存在", () => !outcome.StillInDatabase && repo.Box(throwaway.Id) is null);

Console.WriteLine("== 拖拽合并的落点判定 ==");
var self = new BoxHit("me", "本盒", 0, 0, 400, 500, null, true);
var other = new BoxHit("other", "另一个", 600, 100, 320, 420, null, true);
var sameGroup = new BoxHit("same", "同组盒", 600, 100, 320, 420, "g1", true);
var hidden = new BoxHit("hidden", "隐藏盒", 600, 100, 320, 420, null, false);
var small = new BoxHit("small", "小盒", 620, 120, 100, 100, null, true);
var otherGrouped = new BoxHit("other2", "别组盒", 600, 100, 320, 420, "g2", true);

var targets = new[] { other, hidden };
Check("落在另一个盒子上会被命中", () => BoxDropTarget.Pick(700, 300, targets, "me", null)?.Id == "other");
Check("落在自己身上不命中", () => BoxDropTarget.Pick(200, 250, new[] { self }, "me", null) is null);
Check("落在空白处不命中", () => BoxDropTarget.Pick(1500, 900, targets, "me", null) is null);
Check("隐藏的盒子不会被当成落点", () => BoxDropTarget.Pick(700, 300, new[] { hidden }, "me", null) is null);
Check("已经在同一组里的盒子不再提示合并", () => BoxDropTarget.Pick(700, 300, new[] { sameGroup }, "me", "g1") is null);
Check("对方在别的组里时仍然可合并（不会因为自己没组就跳过）",
    () => BoxDropTarget.Pick(700, 300, new[] { otherGrouped }, "me", null)?.Id == "other2");
Check("边缘外 20 像素仍算命中（放宽 24）", () => BoxDropTarget.Pick(612, 300, targets, "me", null)?.Id == "other");
Check("边缘外 40 像素不算命中（宽容度是 24）", () => BoxDropTarget.Pick(560, 300, targets, "me", null) is null);
Check("大盒子里套小盒子时选小的", () => BoxDropTarget.Pick(660, 160, new[] { other, small }, "me", null)?.Id == "small");

Console.WriteLine("== 组件合并（性能 / 待办 等桌面组件） ==");
var w1 = new WidgetRecord { Kind = WidgetKind.SystemMonitor, Name = "性能", X = 100, Y = 100, Width = 240, Height = 150 };
var w2 = new WidgetRecord { Kind = WidgetKind.Todo, Name = "待办", X = 700, Y = 500, Width = 300, Height = 400 };
repo.Upsert(w1);
repo.Upsert(w2);
var widgetGroup = boxes.MergeWidgets(w1, w2);
Check("组件合并后两个组件在同一个组里",
    () => repo.Widgets().First(w => w.Id == w1.Id).GroupId == widgetGroup.Id
       && repo.Widgets().First(w => w.Id == w2.Id).GroupId == widgetGroup.Id);
Check("组件组里两个成员、当前显示发起合并的那个",
    () => widgetGroup.MemberIds.Split(';', StringSplitOptions.RemoveEmptyEntries).Length == 2
       && widgetGroup.ActiveMemberId == w1.Id);
Check("被并入的组件挪到同一位置与尺寸",
    () => repo.Widgets().First(w => w.Id == w2.Id).X == 100 && repo.Widgets().First(w => w.Id == w2.Id).Width == 240);

Console.WriteLine("== 拖动吸附对齐 ==");
var snapA = new DeskNest.Core.Services.WindowSnap.Rect(300, 200, 320, 420);
var near = DeskNest.Core.Services.WindowSnap.Snap(new DeskNest.Core.Services.WindowSnap.Rect(305, 204, 200, 200), new[] { snapA }, 8);
Check("左边缘差 5 像素会被吸齐", () => near.SnappedX && Math.Abs(near.X - 300) < 0.01);
Check("上边缘差 4 像素会被吸齐", () => near.SnappedY && Math.Abs(near.Y - 200) < 0.01);
var farAway = DeskNest.Core.Services.WindowSnap.Snap(new DeskNest.Core.Services.WindowSnap.Rect(500, 600, 200, 200), new[] { snapA }, 8);
Check("离得远不动它", () => !farAway.SnappedX && !farAway.SnappedY);
var rightEdge = DeskNest.Core.Services.WindowSnap.Snap(new DeskNest.Core.Services.WindowSnap.Rect(617, 200, 200, 200), new[] { snapA }, 8);
Check("右边缘差 3 像素会被吸齐（左贴对方右）", () => rightEdge.SnappedX && Math.Abs(rightEdge.X - 620) < 0.01);
var screenSnap = DeskNest.Core.Services.WindowSnap.Snap(new DeskNest.Core.Services.WindowSnap.Rect(4, 3, 200, 200),
    new[] { new DeskNest.Core.Services.WindowSnap.Rect(0, 0, 2048, 1152) }, 8);
Check("屏幕边也能吸（贴左上角）", () => screenSnap.SnappedX && screenSnap.SnappedY);

Console.WriteLine("== 解散盒子：文件回桌面 ==");
var deskForTest = Path.Combine(root, "desktop");
Directory.CreateDirectory(deskForTest);
var boxToDissolve = new BoxRecord { Name = "待解散", StorageRoot = Path.Combine(root, "store-dis") };
repo.Upsert(boxToDissolve);
Directory.CreateDirectory(boxToDissolve.StorageRoot!);
var fileInBox = Path.Combine(boxToDissolve.StorageRoot!, "会被还回来的.txt");
File.WriteAllText(fileInBox, "x");
repo.Upsert(new ItemRecord
{
    BoxId = boxToDissolve.Id, Kind = ItemKind.File, DisplayName = "会被还回来的.txt",
    SourcePath = Path.Combine(deskForTest, "会被还回来的.txt"), StoredPath = fileInBox, Ext = ".txt"
});
var dissolved = boxes.DissolveToDesktop(boxToDissolve, deskForTest);
Check("解散后盒子记录消失", () => !dissolved.StillInDatabase && repo.Box(boxToDissolve.Id) is null);
Check("文件确实回到了桌面目录", () => File.Exists(Path.Combine(deskForTest, "会被还回来的.txt")));

Console.WriteLine("== 老数据库升级（新增列不能把页面读崩） ==");
var legacyDir = Path.Combine(root, "legacy");
Directory.CreateDirectory(legacyDir);
var legacyDb = Path.Combine(legacyDir, "old.db");
using (var old = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + legacyDb))
{
    old.Open();
    // 模拟"加列之前"的库：todos 只有老列，且有一条老记录
    using var cmd = old.CreateCommand();
    cmd.CommandText = """
      CREATE TABLE todos(id TEXT PRIMARY KEY, title TEXT, note TEXT, list_name TEXT, priority INTEGER,
        repeat TEXT, due_at TEXT, remind_minutes_before INTEGER, is_done INTEGER, is_archived INTEGER,
        created_at TEXT, completed_at TEXT);
      INSERT INTO todos VALUES('t1','老待办','','默认清单',1,'None',NULL,5,0,0,'2026-01-01T00:00:00+08:00',NULL);
      """;
    cmd.ExecuteNonQuery();
}
Check("老库能被打开并自动补列，读待办不抛异常", () =>
{
    using var migrated = new Db(legacyDb);
    var legacyRepo = new Repository(migrated);
    var list = legacyRepo.Todos();
    return list.Count == 1 && list[0].Title == "老待办" && !list[0].IsStarred && list[0].Color.Length == 0;
});

Console.WriteLine("== 任何颜色下都要看得清（人眼可读性） ==");
// 1) 填色底（主按钮/徽标/选中项）上的文字：必须按底色亮度自动取白或近黑
var accents = new[]
{
    System.Windows.Media.Color.FromRgb(0x4C, 0x8B, 0xF5),   // 默认蓝
    System.Windows.Media.Color.FromRgb(0xFF, 0xD4, 0x00),   // 浅黄（最容易白底白字）
    System.Windows.Media.Color.FromRgb(0x8E, 0xE0, 0x8E),   // 浅绿
    System.Windows.Media.Color.FromRgb(0xF5, 0xB0, 0xC8),   // 浅粉
    System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A),   // 近黑
    System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)    // 纯白
};
foreach (var accent in accents)
{
    var name = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
    var ratio = DeskNest.App.Ui.Contrast.Ratio(DeskNest.App.Ui.Contrast.BestForegroundOn(accent), accent);
    Check($"强调色 {name} 上的文字对比度 ≥ 4.5:1（实际 {ratio:0.0}:1）", () => ratio >= 4.5);
}

// 2) 半透明卡片压在任意壁纸上：颜色本身保证不了时要能识别出来（界面会给文字加描边兜底）
var darkCard = System.Windows.Media.Color.FromRgb(0x28, 0x28, 0x2E);
var lightText = System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5);
var whiteWall = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
var blackWall = System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00);
Check("深色卡片 30% 压在亮壁纸上会被判定为需要描边",
    () => DeskNest.App.Ui.Contrast.NeedsTextBackdrop(lightText, darkCard, 0.3, whiteWall));
Check("深色卡片 95% 压在亮壁纸上不再需要描边",
    () => !DeskNest.App.Ui.Contrast.NeedsTextBackdrop(lightText, darkCard, 0.95, whiteWall));

// 3) 每一套配色都要"深底浅字 / 浅底深字"（2026-10-17 白底白字的回归门禁）
foreach (var scheme in new[] { DeskNest.App.Ui.ColorScheme.Light, DeskNest.App.Ui.ColorScheme.Dark,
                               DeskNest.App.Ui.ColorScheme.PureBlack, DeskNest.App.Ui.ColorScheme.EyeCare,
                               DeskNest.App.Ui.ColorScheme.FollowSystem })
{
    var tokens = new DeskNest.App.Ui.ThemeTokens { Scheme = scheme };
    var r1 = DeskNest.App.Ui.Contrast.Ratio(tokens.Foreground, tokens.Background);
    var r2 = DeskNest.App.Ui.Contrast.Ratio(tokens.SecondaryColor, tokens.CardColor);
    Check($"{scheme}：正文与背景对比度 ≥ 7:1（实际 {r1:0.0}:1）", () => r1 >= 7);
    Check($"{scheme}：次要文字与卡片对比度 ≥ 3.5:1（实际 {r2:0.0}:1）", () => r2 >= 3.5);
}
Check("深色卡片 30% 压在暗壁纸上不需要描边",
    () => !DeskNest.App.Ui.Contrast.NeedsTextBackdrop(lightText, darkCard, 0.3, blackWall));

Console.WriteLine("== 卡片颜色 / 材质真的参与绘制（改外观参数，盒子必须变色） ==");
// 缺陷：「改外观参数盒子颜色不变」——Material / MaterialIntensity 被收下却从没参与绘制。
// 这里直接对"算出来的像素"断言：颜色、材质、材质强度、透明度，改任何一个都必须让像素或者透明度变。
var themeBase = new DeskNest.App.Ui.ThemeTokens
{
    Scheme = DeskNest.App.Ui.ColorScheme.Dark,
    Material = DeskNest.App.Ui.MaterialKind.Acrylic,
    MaterialIntensity = 0.65,
    Opacity = 0.82
};
var paintBase = themeBase.ResolveCardPaint();

var thicker = themeBase.Clone(); thicker.MaterialIntensity = 1.0;
Check("材质强度 0.65 → 1.0 画出来的像素真的变了", () => thicker.ResolveCardPaint() != paintBase);
var thinner = themeBase.Clone(); thinner.MaterialIntensity = 0.1;
Check("材质强度调低后更透（alpha 变小）", () => thinner.ResolveCardPaint().A < paintBase.A);

var redCard = themeBase.Clone();
redCard.CardColorOverride = System.Windows.Media.Color.FromRgb(0xC0, 0x20, 0x20);
Check("换成红色卡片后 RGB 真的变了", () =>
{
    var p = redCard.ResolveCardPaint();
    return p.R != paintBase.R || p.G != paintBase.G || p.B != paintBase.B;
});
Check("换颜色不会顺带改透明度", () => redCard.ResolveCardPaint().A == paintBase.A);

var solidTokens = themeBase.Clone(); solidTokens.Material = DeskNest.App.Ui.MaterialKind.Solid;
var micaTokens = themeBase.Clone(); micaTokens.Material = DeskNest.App.Ui.MaterialKind.Mica;
Check("三种材质画出来互不相同（纯色 / 亚克力 / 云母）", () =>
{
    var s = solidTokens.ResolveCardPaint(); var m = micaTokens.ResolveCardPaint();
    return s != paintBase && paintBase != m && s != m;
});
Check("亚克力比纯色更透（同样透明度下 alpha 更小）", () => paintBase.A < solidTokens.ResolveCardPaint().A);
Check("云母比亚克力更透（更贴壁纸）", () => micaTokens.ResolveCardPaint().A < paintBase.A);

var lightCard = themeBase.Clone();
lightCard.CardColorOverride = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
Check("选白色卡片后文字自动变深、且对比度 ≥ 4.5:1", () =>
{
    var fg = lightCard.Foreground;
    return fg.R < 0x80
        && DeskNest.App.Ui.Contrast.Ratio(fg, System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)) >= 4.5;
});
var darkCardTokens = themeBase.Clone();
darkCardTokens.CardColorOverride = System.Windows.Media.Color.FromRgb(0x10, 0x10, 0x10);
Check("选近黑卡片后文字自动变浅", () => darkCardTokens.Foreground.R > 0x80);

Check("没自定义颜色时底色仍按配色方案走（默认外观没被改掉）",
    () => themeBase.PaintColor == themeBase.Background);
Check("深色方案的默认盒底仍是 #1B1B1F",
    () => themeBase.PaintColor == System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1F));

DeskNest.App.Ui.Theme.ApplyDesktop(themeBase);
Check("单盒覆盖材质/强度后，像素与全局默认不同",
    () => DeskNest.App.Ui.Theme.ResolveDesktop("Mica", 0.9, 0.7, 16, false).ResolveCardPaint()
        != themeBase.ResolveCardPaint());
Check("单盒选「跟随全局」时像素与全局完全一致",
    () => DeskNest.App.Ui.Theme.ResolveDesktop("Mica", 0.9, 0.7, 16, true).ResolveCardPaint()
        == themeBase.ResolveCardPaint());

Console.WriteLine("== v2.0：每盒卡片颜色（新增列 card_color） ==");
var colored = new BoxRecord { Name = "带颜色的盒子", CardColor = "#C02020" };
repo.Upsert(colored);
Check("卡片颜色能存能读（默认空 = 跟随全局）", () =>
{
    var back = repo.Box(colored.Id)!;
    return back.CardColor == "#C02020";
});
var plainBox = new BoxRecord { Name = "默认颜色盒子" };
repo.Upsert(plainBox);
Check("不设颜色时默认为空字符串（不会读到 NULL 崩掉）", () => repo.Box(plainBox.Id)!.CardColor == "");
colored.Name = "改过名字的盒子";
repo.Upsert(colored);
Check("改别的字段不会把卡片颜色弄丢", () => repo.Box(colored.Id)!.CardColor == "#C02020");

try { Directory.Delete(root, true); } catch { }

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failures.Count} 项");
foreach (var f in failures) Console.WriteLine("  失败：" + f);
return failures.Count == 0 ? 0 : 1;
