using System.Threading;

namespace DeskNest.App;

/// <summary>程序入口：单实例 + 启动 WPF。</summary>
public static class Program
{
    private const string MutexName = "DeskNest.SingleInstance.{7C4B9A21-3B5E-4E0A-9C21-D9F0F2A6B7C3}";
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
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
