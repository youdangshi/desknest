namespace DeskNest.Core;

/// <summary>所有本地路径的唯一来源。便携模式：设置 DESKNEST_DATA_DIR 即可把数据放到任意目录。</summary>
public static class AppPaths
{
    public const string AppName = "DeskNest";

    public static string DataRoot { get; } = ResolveDataRoot();
    public static string DbFile => Path.Combine(DataRoot, "desknest.db");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string LogFile => Path.Combine(DataRoot, "logs", "desknest.log");
    public static string BackupRoot => Path.Combine(DataRoot, "backups");
    public static string BoxesRoot => Path.Combine(DataRoot, "boxes");
    public static string ModulesRoot { get; } = Path.Combine(AppContext.BaseDirectory, "modules");
    public static string PetsRoot => Path.Combine(DataRoot, "pets");
    public static string AssetsRoot => Path.Combine(AppContext.BaseDirectory, "assets");

    public static string DefaultManagedRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DeskNest");

    private static string ResolveDataRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DESKNEST_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv!;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, AppName);
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
        Directory.CreateDirectory(BackupRoot);
        Directory.CreateDirectory(BoxesRoot);
        Directory.CreateDirectory(PetsRoot);
        Directory.CreateDirectory(ModulesRoot);
    }
}

/// <summary>极简日志（滚动写入，永不抛异常）。</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("FAIL", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(AppPaths.LogFile)!;
                Directory.CreateDirectory(dir);
                var file = AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > 4 * 1024 * 1024)
                {
                    File.Move(file, file + ".1", overwrite: true);
                }
                File.AppendAllText(file, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败不能影响主流程 */ }
    }
}
