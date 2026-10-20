// P0 探针：验证「WinUI 3 窗口能不能贴到桌面壁纸层、能点、还不进 Alt+Tab」
// 一个窗口里三个按钮分别试 owner / workerw / bottom，点完看同目录 spike.log。
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

internal static class Spike
{
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "spike.log");

    [STAThread]
    private static void Main(string[] args)
    {
        File.WriteAllText(LogFile, $"=== spike start {DateTime.Now:HH:mm:ss} ===\r\n");
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
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
        private TextBlock? _status;

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new Window { Title = "DeskNest P0 spike" };

            _status = new TextBlock
            {
                Text = "选一种宿主方案，再点「点我」验证能不能点",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var panel = new StackPanel { Padding = new Thickness(12), Spacing = 6 };
            panel.Children.Add(new TextBlock { Text = "宿主方案", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

            foreach (var mode in new[] { "owner", "workerw", "bottom" })
            {
                var m = mode;
                var b = new Button { Content = m, HorizontalAlignment = HorizontalAlignment.Stretch };
                b.Click += (_, _) => Apply(m);
                panel.Children.Add(b);
            }

            var click = new Button { Content = "点我（能点=输入通过）", HorizontalAlignment = HorizontalAlignment.Stretch };
            var clicks = 0;
            click.Click += (_, _) =>
            {
                clicks++;
                if (_status is not null) _status.Text = $"已点击 {clicks} 次 —— 输入可用";
                Log($"CLICK {clicks}");
            };
            panel.Children.Add(click);
            panel.Children.Add(_status);

            _window.Content = new Border { Background = new SolidColorBrush(Colors.White), Child = panel };
            _window.Activate();

            var hwnd = WindowNative.GetWindowHandle(_window);
            Log($"HWND=0x{hwnd.ToInt64():X}");
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                try { Probe.Report(hwnd, "启动时"); } catch (Exception ex) { Log("REPORT FAILED " + ex); }
            });
        }

        private void Apply(string mode)
        {
            var hwnd = WindowNative.GetWindowHandle(_window!);
            try
            {
                Log($"--- apply {mode} ---");
                Probe.Apply(hwnd, mode);
                Probe.Report(hwnd, mode);
                if (_status is not null) _status.Text = $"{mode} 已应用 —— 看窗口还在不在、还能不能点";
            }
            catch (Exception ex)
            {
                Log("APPLY FAILED " + ex);
            }
        }
    }
}

/// <summary>三种「贴桌面层」方案 + 应用后的状态回报（owner/parent 核实才作数）。</summary>
internal static class Probe
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GW_OWNER = 4;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    internal static void Apply(IntPtr hwnd, string mode)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) == 0)
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);   // 不进 Alt+Tab

        switch (mode)
        {
            case "workerw":
                {
                    var target = FindWallpaperHost();
                    var ok = SetParent(hwnd, target);
                    Spike.Log($"SetParent(0x{target.ToInt64():X}) -> {ok} lastErr={Marshal.GetLastWin32Error()}");
                    SetWindowPos(hwnd, IntPtr.Zero, 120, 120, 340, 300, SWP_NOACTIVATE);
                    break;
                }
            case "bottom":
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
                    var ok = SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    Spike.Log($"SetWindowPos(HWND_BOTTOM) -> {ok} lastErr={Marshal.GetLastWin32Error()}");
                    break;
                }
            default:
                {
                    var host = FindWallpaperHost();
                    SetWindowLongPtr(hwnd, -8 /*GWL_HWNDPARENT*/, host);
                    Spike.Log($"SetOwner(0x{host.ToInt64():X}) lastErr={Marshal.GetLastWin32Error()}");
                    break;
                }
        }
    }

    internal static void Report(IntPtr hwnd, string tag)
    {
        var owner = GetWindow(hwnd, GW_OWNER);
        var parent = GetParent(hwnd);
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        Spike.Log($"[{tag}] owner=0x{owner.ToInt64():X} parent=0x{parent.ToInt64():X} " +
                  $"exstyle=0x{ex:X} toolwindow={((ex & WS_EX_TOOLWINDOW) != 0)} noactivate={((ex & WS_EX_NOACTIVATE) != 0)}");
    }

    private static IntPtr FindWallpaperHost()
    {
        var progman = FindWindow("Progman", null);
        if (FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;

        var candidate = IntPtr.Zero;
        while (true)
        {
            candidate = FindWindowEx(IntPtr.Zero, candidate, "WorkerW", null);
            if (candidate == IntPtr.Zero) break;
            if (FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return candidate;
        }
        return progman;
    }
}
