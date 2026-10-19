// P0 探针：验证「WinUI 3 窗口能不能贴到桌面壁纸层、能点、还不进 Alt+Tab」
// 用法：desknest-spike.exe owner | workerw | bottom
//   每种模式跑一次，把日志（spike-winui\spike.log）贴回来即可。
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

internal static class Spike
{
    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "spike.log");

    private static string _mode = "owner";

    [STAThread]
    private static void Main(string[] args)
    {
        _mode = args.Length > 0 ? args[0].ToLowerInvariant() : "owner";
        File.WriteAllText(LogFile, $"=== spike start mode={_mode} ===\r\n");

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            Log("FATAL " + ex);
        }
    }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogFile, message + "\r\n", Encoding.UTF8); } catch { }
    }

    internal sealed class App : Application
    {
        private Window? _window;

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new Window { Title = "DeskNest P0 spike (" + _mode + ")" };

            var button = new Button { Content = "点我（能点＝输入通过）" };
            var status = new TextBlock
            {
                Text = "等待宿主…",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var clicks = 0;
            button.Click += (_, _) =>
            {
                clicks++;
                status.Text = $"已点击 {clicks} 次 —— 输入路径可用";
                Log($"CLICK {clicks}");
            };

            var panel = new StackPanel { Padding = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = $"宿主模式：{_mode}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            panel.Children.Add(button);
            panel.Children.Add(status);
            _window.Content = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                Child = panel
            };

            _window.Activate();
            var hwnd = WindowNative.GetWindowHandle(_window);
            Log($"HWND=0x{hwnd.ToInt64():X}");

            // 窗口起来之后再动 Win32 层
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                try { Host.Apply(hwnd, _mode, Log); }
                catch (Exception ex) { Log("APPLY FAILED " + ex); }
            });
        }
    }
}

/// <summary>三种「贴桌面层」方案，逐个试。</summary>
internal static class Host
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    internal static void Apply(IntPtr hwnd, string mode, Action<string> log)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);   // 不进 Alt+Tab
        log($"exstyle before=0x{ex:X} after=0x{GetWindowLong(hwnd, GWL_EXSTYLE):X}");

        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var workerW = IntPtr.Zero;
        if (defView == IntPtr.Zero)
        {
            // 老办法：DefView 被移到了某个 WorkerW 下面
            var shell = GetShellWindow();
            var candidate = IntPtr.Zero;
            do
            {
                candidate = FindWindowEx(IntPtr.Zero, candidate, "WorkerW", null);
                if (candidate == IntPtr.Zero) break;
                if (FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    workerW = candidate;
                    break;
                }
            } while (true);
        }
        log($"Progman=0x{progman.ToInt64():X} DefView=0x{defView.ToInt64():X} WorkerW=0x{workerW.ToInt64():X} Desktop=0x{GetDesktopWindow().ToInt64():X}");

        switch (mode)
        {
            case "workerw":
                {
                    // 方案 B：挂到 WorkerW 下（壁纸层）
                    var parent = workerW != IntPtr.Zero ? workerW : progman;
                    var ok = SetParent(hwnd, parent);
                    log($"SetParent({mode}) -> {ok} parent=0x{GetParent(hwnd).ToInt64():X} lastErr={Marshal.GetLastWin32Error()}");
                    break;
                }
            case "bottom":
                {
                    // 方案 C：不挂父窗口，只置底 + 不激活
                    var ex2 = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex2 | WS_EX_NOACTIVATE);
                    var ok = SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    log($"SetWindowPos(HWND_BOTTOM) -> {ok} lastErr={Marshal.GetLastWin32Error()}");
                    break;
                }
            default:
                {
                    // 方案 A：owner 窗口（WPF 版用的就是这条路：把壁纸窗口设为 owner）
                    var ok = SetWindowLongPtr(hwnd, -8 /*GWL_HWNDPARENT*/, workerW != IntPtr.Zero ? workerW : progman);
                    log($"SetOwner -> ok={ok} lastErr={Marshal.GetLastWin32Error()}");
                    break;
                }
        }

        SetWindowPos(hwnd, IntPtr.Zero, 120, 120, 320, 220, SWP_NOACTIVATE);
        log("done");
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
