using System.Text.Json;
using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>
/// 启动时对未关账的文件意图记录做一次审计。
///
/// 背景：FileOps 每次移动文件前写一条 journal(applied=0)，成功后 CloseJournal(applied=1)。
/// 若进程在"写了意图、还没关账"之间被强杀/断电，就留下 open 记录。这里按源/目标文件是否
/// 还存在来判断那一步到底做没做成，并给出结论。
///
/// 保守原则（对应"收纳＝真移动、任何异常不得丢文件"红线）：
///   1. **绝不自动删除或移动任何文件**——只判定与关账，把可疑情况写日志交给人看；
///   2. 源和目标都不在时保持 open，不关账——那是需要人工介入的信号，不能被静默抹掉；
///   3. 同卷 rename 是原子的，不会有中间态；跨卷 copy+delete 的"源目标都在"是正常半成品，
///      目标文件是完整的，关账即可，源留在原地不动。
/// </summary>
public sealed class JournalRecovery
{
    private readonly Repository _repo;

    public JournalRecovery(Repository repo) => _repo = repo;

    public sealed record AuditReport(int Open, int Closed, int KeptOpen)
    {
        public bool Clean => KeptOpen == 0;
    }

    private sealed record Intent(string? Source, string? Target);

    /// <summary>审计所有未关账记录，返回统计。可重复调用（已关账的不会再进入）。</summary>
    public AuditReport Audit()
    {
        var open = _repo.OpenJournals();
        var closed = 0;
        var kept = 0;
        // "源与目标都不在"的记录会一直保持 open（不掩盖丢失信号），若逐条刷 WARN 每次启动都要刷屏，
        // 这里收集起来最后汇总成一行，既不丢信号也安静。
        var missing = new List<string>();

        foreach (var (id, kind, payload) in open)
        {
            try
            {
                var intent = Parse(kind, payload);
                if (intent.Source is null)
                {
                    // 老格式或字段缺失，无法判断——保持 open，交人工核查，绝不猜。
                    kept++;
                    Log.Warn($"journal 无法审计（缺 source 字段）：id={id} kind={kind} payload={payload}");
                    continue;
                }

                var srcExists = FileOps.Exists(intent.Source);
                var dstExists = intent.Target is not null && FileOps.Exists(intent.Target);

                if (srcExists && !dstExists)
                {
                    // 源还在、目标没生成：那一步没做成（或已回滚），文件没丢，关账。
                    _repo.CloseJournal(id);
                    closed++;
                    Log.Info($"journal 审计：未执行，已关账（id={id} kind={kind}）");
                }
                else if (!srcExists && dstExists)
                {
                    // 源没了、目标在：移动已完成，只是崩在关账前。关账，文件安全。
                    _repo.CloseJournal(id);
                    closed++;
                    Log.Info($"journal 审计：已完成，补关账（id={id} kind={kind} → {intent.Target}）");
                }
                else if (srcExists && dstExists)
                {
                    // 源和目标都在：跨卷 copy 成功、delete 源之前崩了。目标完整，关账；
                    // 源可能是用户原件，绝不能自动删，也不去合并——提示人工。
                    _repo.CloseJournal(id);
                    closed++;
                    Log.Warn($"journal 审计：源与目标同时存在，已关账但未删任何文件，请人工核对：源={intent.Source} 目标={intent.Target}");
                }
                else
                {
                    // 两边都不在：可能文件被移走/删掉，保持 open 让人工看见，不静默关账。
                    kept++;
                    if (missing.Count < 3) missing.Add($"{intent.Source} → {intent.Target}");
                }
            }
            catch (Exception ex)
            {
                kept++;
                Log.Warn($"journal 审计异常（id={id}）：{ex.Message}");
            }
        }

        if (missing.Count > 0)
            Log.Warn($"journal 审计：有 {kept} 条意图记录的源与目标均不存在，保持未关账待人工核查" +
                     $"（示例：{string.Join("；", missing)}）");
        Log.Info($"journal 审计完成：未关账 {open.Count} 条，关账 {closed} 条，保留 {kept} 条");
        return new AuditReport(open.Count, closed, kept);
    }

    /// <summary>把三种 kind 的 payload 归一成 (源, 目标)；老 move-in 没有 target 则目标为 null。</summary>
    private static Intent Parse(string kind, string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return kind switch
        {
            "move-in" => new Intent(Get(root, "source"), Get(root, "target")),
            "restore" => new Intent(Get(root, "stored"), Get(root, "target")),
            "move" => new Intent(Get(root, "from"), Get(root, "to")),
            _ => new Intent(Get(root, "source") ?? Get(root, "stored") ?? Get(root, "from"),
                            Get(root, "target") ?? Get(root, "to"))
        };
    }

    private static string? Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
