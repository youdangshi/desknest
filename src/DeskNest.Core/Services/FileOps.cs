using DeskNest.Core.Data;

namespace DeskNest.Core.Services;

/// <summary>
/// 文件安全操作：所有移动/还原都走这里。
/// 规则：同卷 rename（瞬间）→ 跨卷 copy+校验+delete；重名自动 (1)(2)；失败逐项报告；
///      每次真正动文件前先写"意图记录"（journal），崩溃后可审计与续做。
/// </summary>
public sealed class FileOps
{
    private readonly Repository _repo;
    public FileOps(Repository repo) => _repo = repo;

    public sealed record OpResult(bool Ok, string? StoredPath, string? Error)
    {
        public static OpResult Success(string stored) => new(true, stored, null);
        public static OpResult Fail(string err) => new(false, null, err);
    }

    /// <summary>目标目录内取一个不冲突的名字：name.ext → name (1).ext</summary>
    public static string UniquePath(string dir, string fileName)
    {
        Directory.CreateDirectory(dir);
        var candidate = Path.Combine(dir, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():n}){ext}");
    }

    public static bool IsUnder(string child, string parent)
    {
        var c = Path.GetFullPath(child).TrimEnd('\\');
        var p = Path.GetFullPath(parent).TrimEnd('\\');
        return c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)
               || string.Equals(c, p, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool SameVolume(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)),
                                 Path.GetPathRoot(Path.GetFullPath(b)),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>把源路径移动进存储目录（普通盒/抽屉盒语义：真实移动）。</summary>
    public OpResult MoveIntoStorage(string sourcePath, string storageRoot)
    {
        try
        {
            if (!Exists(sourcePath)) return OpResult.Fail($"源不存在：{sourcePath}");
            if (IsUnder(sourcePath, storageRoot)) return OpResult.Fail("源文件已在目标目录内，已跳过");

            _repo.Journal("move-in", new { source = sourcePath, root = storageRoot });
            var name = Path.GetFileName(sourcePath.TrimEnd('\\'));
            var target = UniquePath(storageRoot, name);
            MoveOrCopy(sourcePath, target);
            return OpResult.Success(target);
        }
        catch (Exception ex)
        {
            Log.Warn($"MoveIntoStorage 失败: {sourcePath} → {ex.Message}");
            return OpResult.Fail(ex.Message);
        }
    }

    /// <summary>删除盒内条目 = 把文件还原回原位置；原目录不存在则回退桌面。</summary>
    public OpResult RestoreToOriginal(string storedPath, string originalPath)
    {
        try
        {
            if (!Exists(storedPath)) return OpResult.Fail($"存储文件不存在：{storedPath}");
            var dir = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                Log.Info($"原目录不存在，回退桌面：{dir}");
            }
            var target = UniquePath(dir, Path.GetFileName(originalPath));
            _repo.Journal("restore", new { stored = storedPath, target });
            MoveOrCopy(storedPath, target);
            return OpResult.Success(target);
        }
        catch (Exception ex)
        {
            Log.Error($"RestoreToOriginal 失败: {storedPath} → {ex.Message}");
            return OpResult.Fail(ex.Message);
        }
    }

    /// <summary>整理用：把文件/文件夹移动到目标目录（盒子登记由调用方处理）。</summary>
    public OpResult MoveTo(string sourcePath, string targetDirectory, string? newName = null)
    {
        try
        {
            if (!Exists(sourcePath)) return OpResult.Fail($"源不存在：{sourcePath}");
            if (IsUnder(sourcePath, targetDirectory)) return OpResult.Fail("源已在目标目录内");
            var name = newName ?? Path.GetFileName(sourcePath.TrimEnd('\\'));
            var target = UniquePath(targetDirectory, name);
            _repo.Journal("move", new { from = sourcePath, to = target });
            MoveOrCopy(sourcePath, target);
            return OpResult.Success(target);
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }
    }

    private static void MoveOrCopy(string source, string target)
    {
        var isDir = Directory.Exists(source);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        if (SameVolume(source, target))
        {
            if (isDir) Directory.Move(source, target);
            else File.Move(source, target);
            return;
        }

        if (isDir)
        {
            CopyDirectory(source, target);
            if (!Directory.Exists(target)) throw new IOException("跨卷复制目录失败");
            Directory.Delete(source, recursive: true);
        }
        else
        {
            File.Copy(source, target, overwrite: false);
            if (!File.Exists(target)) throw new IOException("跨卷复制文件失败");
            File.Delete(source);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            var t = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t, overwrite: true);
        }
    }

    public static void HardDelete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else if (File.Exists(path)) File.Delete(path);
    }
}
