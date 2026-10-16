using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskNest.App.Host;
using DeskNest.App.Ui;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Core.Data;
using DeskNest.Native.Win32;
using Xunit;

namespace DeskNest.Tests;

/// <summary>
/// **用真鼠标事件测核心交互**：拖动盒子、拉右下角改大小、锁定后不该动。
///
/// 为什么写成测试而不是手点：① 每次发版都能重跑；② 用的是同一个输入通道
/// （SetCursorPos + mouse_event 走系统输入队列 → WPF 收到真实鼠标事件），
/// 所以"鼠标事件到底有没有送到盒子"这件事也被覆盖了；③ 不需要人在场。
///
/// 代价：测试期间会真的挪动你的鼠标、并在桌面上短暂出现一个测试用的收纳盒（结束会关掉、删掉记录）。
/// </summary>
[Collection(DesktopUiCollection.Name)]
public sealed class MouseInteractionTests : IDisposable
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly string _boxName = "鼠标测试盒-" + Guid.NewGuid().ToString("n")[..6];
    private BoxRecord? _record;

    public void Dispose()
    {
        try
        {
            if (_record is not null) AppServices.Repo.DeleteBox(_record.Id);
        }
        catch { }
    }

    /// <summary>在独立 STA 线程上开一个真实的盒子窗口（关掉桌面层挂载，保证它在最前面，鼠标事件一定落在它身上）。</summary>
    private static (Thread Thread, Dispatcher Dispatcher, BoxWindow Window, ManualResetEventSlim Ready) StartBox(
        BoxRecord record, double left, double top, double width, double height)
    {
        var ready = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;
        BoxWindow? window = null;

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            AppServices.Initialize(startBackgroundWork: false);

            window = new BoxWindow(record)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = left,
                Top = top,
                Width = width,
                Height = height
            };
            window.Show();

            // 桌面层的窗口永远在别的窗口之下，鼠标事件会被盖住；测试期间先"摘下来"置顶，
            // 保证注入的鼠标一定落在盒子上（拖动逻辑本身与挂不挂桌面层无关）。
            var hwnd = new WindowInteropHelper(window).Handle;
            DesktopLayer.Detach(hwnd);
            window.Topmost = true;
            window.Activate();

            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(30)), "盒子窗口没能在 30 秒内建好");
        return (thread, dispatcher!, window!, ready);
    }

    private static Rect BoxRect(BoxWindow window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Assert.True(GetWindowRect(hwnd, out var r), "拿不到窗口矩形");
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>按住某个点拖到另一个点（分步移动，模拟真人拖拽）。</summary>
    private static void DragMouse(int fromX, int fromY, int toX, int toY, int steps = 12)
    {
        SetCursorPos(fromX, fromY);
        Thread.Sleep(60);
        mouse_event(MouseLeftDown, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(60);
        for (var i = 1; i <= steps; i++)
        {
            SetCursorPos(fromX + (toX - fromX) * i / steps, fromY + (toY - fromY) * i / steps);
            Thread.Sleep(25);
        }
        Thread.Sleep(60);
        mouse_event(MouseLeftUp, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(150);
    }

    [Fact]
    public void 核心交互_用真鼠标拖动标题栏_盒子真的会跟着走()
    {
        _record = new BoxRecord { Name = _boxName, StorageRoot = Path.Combine(Path.GetTempPath(), "desknest-mouse") };
        AppServices.Repo.Upsert(_record);

        var (thread, dispatcher, window, _) = StartBox(_record, left: 240, top: 200, width: 320, height: 420);
        try
        {
            var before = BoxRect(window);

            // 抓标题栏中间（标题栏高 30 DIP，取 y = top + 15）
            var grabX = (int)(before.Left + before.Width / 2);
            var grabY = (int)(before.Top + 15);
            DragMouse(grabX, grabY, grabX + 90, grabY + 60);

            dispatcher.Invoke(() => { });
            var after = BoxRect(window);

            Assert.True(Math.Abs((after.Left - before.Left) - 90) <= 6,
                $"横着应该走 90 像素，实际 {(after.Left - before.Left):0}（before={before.Left:0} after={after.Left:0}）");
            Assert.True(Math.Abs((after.Top - before.Top) - 60) <= 6,
                $"竖着应该走 60 像素，实际 {(after.Top - before.Top):0}");
        }
        finally
        {
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void 核心交互_用真鼠标拉右下角_盒子真的会变大()
    {
        _record = new BoxRecord { Name = _boxName, StorageRoot = Path.Combine(Path.GetTempPath(), "desknest-mouse") };
        AppServices.Repo.Upsert(_record);

        var (thread, dispatcher, window, _) = StartBox(_record, left: 300, top: 240, width: 320, height: 360);
        try
        {
            var before = BoxRect(window);
            var grabX = (int)(before.Left + before.Width - 8);
            var grabY = (int)(before.Top + before.Height - 8);
            DragMouse(grabX, grabY, grabX + 100, grabY + 80);

            dispatcher.Invoke(() => { });
            var after = BoxRect(window);

            Assert.True(after.Width - before.Width >= 85,
                $"宽度应该增加约 100，实际增加 {after.Width - before.Width:0}");
            Assert.True(after.Height - before.Height >= 65,
                $"高度应该增加约 80，实际增加 {after.Height - before.Height:0}");
        }
        finally
        {
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void 核心交互_锁定位置之后_真鼠标拖不动()
    {
        _record = new BoxRecord
        {
            Name = _boxName,
            StorageRoot = Path.Combine(Path.GetTempPath(), "desknest-mouse"),
            LockPosition = true
        };
        AppServices.Repo.Upsert(_record);

        var (thread, dispatcher, window, _) = StartBox(_record, left: 260, top: 220, width: 320, height: 360);
        try
        {
            var before = BoxRect(window);
            var grabX = (int)(before.Left + before.Width / 2);
            var grabY = (int)(before.Top + 15);
            DragMouse(grabX, grabY, grabX + 120, grabY + 90);

            dispatcher.Invoke(() => { });
            var after = BoxRect(window);

            Assert.True(Math.Abs(after.Left - before.Left) <= 2 && Math.Abs(after.Top - before.Top) <= 2,
                $"锁定位置后不该移动，实际从 {before.Left:0},{before.Top:0} 到了 {after.Left:0},{after.Top:0}");
        }
        finally
        {
            dispatcher.InvokeShutdown();
            thread.Join(TimeSpan.FromSeconds(10));
        }
    }
}
