using System.Runtime.CompilerServices;

namespace DeskNest.Tests;

/// <summary>
/// 测试环境准备：把数据的根目录指到临时目录，避免测试污染用户真实的
/// %LOCALAPPDATA%\DeskNest（必须在任何测试访问 AppPaths 之前执行）。
/// </summary>
public static class TestSetup
{
    public static string DataRoot { get; private set; } = "";

    [ModuleInitializer]
    public static void Initialize()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "desknest-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(DataRoot);
        Environment.SetEnvironmentVariable("DESKNEST_DATA_DIR", DataRoot);
    }
}
