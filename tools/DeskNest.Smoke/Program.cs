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

try { Directory.Delete(root, true); } catch { }

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failures.Count} 项");
foreach (var f in failures) Console.WriteLine("  失败：" + f);
return failures.Count == 0 ? 0 : 1;
