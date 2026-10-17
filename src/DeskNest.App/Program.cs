using System.Threading;
using DeskNest.App.Host;

namespace DeskNest.App;

/// <summary>程序入口：单实例 + 启动 WPF。</summary>
public static class Program
{
    private const string MutexName = "DeskNest.SingleInstance.{7C4B9A21-3B5E-4E0A-9C21-D9F0F2A6B7C3}";
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // 文档截图模式：DeskNest.exe --shot-pages <目录>
        // 逐个打开每个设置页并抓图，跑完自动退出（发版时生成界面截图用，不影响正常启动）。
        var shotFlag = Array.IndexOf(args, "--shot-pages");
        if (shotFlag >= 0 && shotFlag + 1 < args.Length)
            PageShotter.OutputDir = args[shotFlag + 1];

        // 截图模式用独立的互斥名：否则用户正开着桌巢时自动化截图永远跑不起来（会被单实例挡掉）。
        // 截图模式自带隔离数据目录（DESKNEST_DATA_DIR），不会碰用户数据。
        var mutexName = PageShotter.OutputDir is null ? MutexName : MutexName + ".shot";
        _mutex = new Mutex(true, mutexName, out var isFirst);
        if (!isFirst)
        {
            // 截图模式必须是唯一实例：否则会连到已运行的实例上，抓不到图还给用户添乱
            if (PageShotter.OutputDir is not null)
            {
                Console.Error.WriteLine("已有桌巢实例在运行，请先退出再执行截图模式。");
                return;
            }
            // 已有实例：交给它把界面唤出来（通过本地接口的 /health 探测后退出）
            try
            {
                using var c = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                c.GetAsync("http://127.0.0.1:8737/health").GetAwaiter().GetResult();
            }
            catch { }
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
