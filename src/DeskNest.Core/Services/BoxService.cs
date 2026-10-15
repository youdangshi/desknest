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
}
