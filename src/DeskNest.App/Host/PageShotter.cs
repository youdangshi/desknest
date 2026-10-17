using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskNest.App.Windows;
using DeskNest.Core;
using DeskNest.Native.Win32;

namespace DeskNest.App.Host;

/// <summary>
/// 文档截图模式（`DeskNest.exe --shot-pages &lt;目录&gt;`）：把主窗口按固定尺寸摆好，
/// 逐个打开每个设置页并抓图，再补上桌面层、盒子右键菜单、快捷搜索面板与整理向导。
/// 用途是发版时自动生成一致的界面截图（写文档、填商店页都用它），
/// 不依赖任何外部自动化工具，也不需要有人守着点鼠标。
/// </summary>
internal static class PageShotter
{
    /// <summary>非空表示本次是截图模式，值为输出目录。</summary>
    public static string? OutputDir { get; set; }

    private const int WindowWidth = 1120;
    private const int WindowHeight = 780;
    private const int WindowLeft = 60;
    private const int WindowTop = 36;

    public static async Task RunAsync(App app)
    {
        var dir = OutputDir!;
        Directory.CreateDirectory(dir);
        Log.Info("截图模式开始，输出目录：" + dir);

        var window = app.MainView;
        if (window is null)
        {
            Log.Error("截图模式：主窗口未创建");
            return;
        }

        // 固定尺寸与位置，让每张截图的构图一致
        window.WindowState = WindowState.Normal;
        window.Left = WindowLeft;
        window.Top = WindowTop;
        window.Width = WindowWidth;
        window.Height = WindowHeight;
        window.Topmost = true;
        window.Show();
        await Settle(900);

        // 没有盒子时造一个演示盒子（只写隔离数据目录，绝不碰用户真实数据），
        // 否则桌面层截图是一张空壁纸，根本看不出盒子外观有没有改对。
        if (AppServices.Repo.Boxes().Count == 0)
        {
            var demo = new Core.BoxRecord { Name = "工作", Width = 320, Height = 372, MarginX = 120, MarginY = 120 };
            AppServices.Repo.Upsert(demo);
            app.Desktop.RestoreAll();
            Log.Info("截图模式：已创建演示盒子（隔离数据目录）");
        }

        var index = 0;
        // 设置页截图期间收起桌面层：收纳盒/组件挂在桌面层上，会和主窗口叠在一起
        app.Desktop.SetLayerVisible(false);
        await Settle(300);

        foreach (var key in window.PageKeys.ToList())
        {
            index++;
            window.NavigateTo(key);
            await Settle(650);
            var title = window.TitleOf(key) ?? key;
            SaveWindow(window, Path.Combine(dir, $"{index:00}-{key}.png"));
            Log.Info($"截图 {index:00} 页面 {key}（{title}）");
        }

        // 桌面层：收纳盒、桌面组件与桌宠都在这张里。
        // 桌面层窗口平时永远在别的窗口之下，所以先临时摘下来置顶，并铺一层真实壁纸做底。
        window.Topmost = false;
        window.Hide();
        global::DeskNest.App.Host.WallpaperBackdrop? backdrop = null;
        // 盒子可能被用户放在了别的显示器/屏幕外，先统一排到主屏，截图才拍得到；
        // 拍完会按快照还原用户原来的摆放，不动他的桌面。
        var snapshot = AppServices.Repo.Boxes().ToDictionary(b => b.Id, b => (b.MarginX, b.MarginY));
        var widgetSnapshot = AppServices.Repo.Widgets().ToDictionary(w => w.Id, w => (w.X, w.Y));
        try
        {
            backdrop = new WallpaperBackdrop();
            backdrop.Show();
            app.Desktop.SetLayerVisible(true);

            var primary = DeskNest.Native.Win32.Monitors.Primary();
            if (primary is not null)
            {
                Log.Info($"截图前把盒子排到主屏 {primary.DeviceName}（工作区 {primary.WorkWidth}×{primary.WorkHeight}）");
                app.Desktop.ArrangeOn(primary);
            }

            app.Desktop.PrepareLayerForShot();
            await Settle(1200);
            SaveScreen(Path.Combine(dir, $"{++index:00}-desktop-layer.png"));
            Log.Info("截图 桌面层");

            // 菜单在拍摄期间一直用第一个盒子，这里确认它确实在屏幕内
            var shotBox = app.Desktop.Boxes.Values.FirstOrDefault();
            if (shotBox is not null)
                Log.Info($"用于菜单截图的盒子：{shotBox.Box.Name} @ {shotBox.Left:0},{shotBox.Top:0}");
        }
        catch (Exception ex) { Log.Warn("桌面层截图失败：" + ex.Message); }

        // 收纳盒右键菜单（键盘 Shift+F10 打开，见 BoxWindow）；展开二级子菜单再拍一张
        var box = app.Desktop.Boxes.Values.FirstOrDefault();
        if (box is not null)
        {
            box.OpenContextMenu();
            await Settle(600);
            SaveScreen(Path.Combine(dir, $"{++index:00}-box-menu.png"));
            Log.Info("截图 收纳盒右键菜单");

            box.CloseContextMenu();
            await Settle(300);
            box.OpenContextMenu("更多操作");
            await Settle(700);
            SaveScreen(Path.Combine(dir, $"{++index:00}-box-menu-submenu.png"));
            box.CloseContextMenu();
            Log.Info("截图 收纳盒右键菜单（二级展开）");
        }

        // 收起临时置顶与壁纸底布，把窗口装回桌面层
        app.Desktop.RestoreLayerAfterShot();
        backdrop?.Close();
        RestoreBoxGeometry(app, snapshot);
        // 组件的位置是窗口自己落盘的，还原时把记录与窗口一起放回去
        foreach (var widget in AppServices.Repo.Widgets())
        {
            if (!widgetSnapshot.TryGetValue(widget.Id, out var spot)) continue;
            widget.X = spot.X;
            widget.Y = spot.Y;
            AppServices.Repo.Upsert(widget);
            if (app.Desktop.Widgets.TryGetValue(widget.Id, out var win))
            {
                win.Left = spot.X;
                win.Top = spot.Y;
            }
        }
        await Settle(900);

        // 快捷搜索面板（Alt+Space）
        app.ShowSearch();
        await Settle(1100);
        SaveScreen(Path.Combine(dir, $"{++index:00}-search-panel.png"));
        (Application.Current as App)?.HideSearch();
        Log.Info("截图 快捷搜索面板");

        // 一键整理向导
        window.Show();
        window.Topmost = true;
        window.Activate();
        window.OpenOrganizeWizard(modal: false);
        await Settle(1200);
        foreach (Window w in Application.Current.Windows)
        {
            // 只认整理向导本人：这时候桌面上可能还飘着通知条等其它窗口
            if (ReferenceEquals(w, window) || !w.IsVisible || w.Title != "整理预览") continue;
            SaveWindow(w, Path.Combine(dir, $"{++index:00}-organize-wizard.png"));
            w.Close();
            Log.Info("截图 整理向导：" + w.Title);
            break;
        }

        window.Topmost = false;
        Log.Info("截图模式结束");
        app.Shutdown();
    }

    /// <summary>等界面画完：先把 Dispatcher 里排队的工作跑干净，再给动画/图表留一点时间。</summary>
    private static async Task Settle(int milliseconds)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(milliseconds);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>把截图期间挪动过的盒子放回用户原来的位置（截图不该改变用户的桌面）。</summary>
    private static void RestoreBoxGeometry(App app, Dictionary<string, (double X, double Y)> snapshot)
    {
        try
        {
            foreach (var box in AppServices.Repo.Boxes())
            {
                if (!snapshot.TryGetValue(box.Id, out var spot)) continue;
                box.MarginX = spot.X;
                box.MarginY = spot.Y;
                AppServices.Repo.Upsert(box);
                if (app.Desktop.Boxes.TryGetValue(box.Id, out var win)) win.MoveTo(spot.X, spot.Y);
            }
            Log.Info("已还原盒子原来的摆放位置");
        }
        catch (Exception ex) { Log.Warn("还原盒子位置失败：" + ex.Message); }
    }

    private static void SaveWindow(Window window, string path)
    {
        try
        {
            window.Topmost = true;
            window.Activate();
            var hwnd = new WindowInteropHelper(window).Handle;
            WindowShot.BringToFront(hwnd);
            Thread.Sleep(340);                       // 等置顶与合成器更新一帧
            if (WindowShot.Bounds(hwnd) is not { } b)
            {
                Log.Warn("截图失败：拿不到窗口矩形 " + path);
                return;
            }
            // 先让窗口"自己画自己"：这样即使有别的窗口盖在同一块屏幕区域上（浏览器、聊天软件…），
            // 拍到的也只会是本窗口 —— 既保证文档截图正确，也避免把用户的其它窗口拍进文档。
            if (SaveWindowContent(hwnd, b.W, b.H, path)) return;
            Log.Warn("PrintWindow 抓不到窗口内容，退化为按屏幕区域抓图：" + path);
            SaveRegion(b.X, b.Y, b.W, b.H, path);
        }
        catch (Exception ex) { Log.Warn($"截图失败 {path}：{ex.Message}"); }
    }

    private static void SaveScreen(string path)
    {
        try
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
            if (b is null) { Log.Warn("截图失败：取不到主屏尺寸"); return; }
            SaveRegion(b.Value.X, b.Value.Y, b.Value.Width, b.Value.Height, path);
        }
        catch (Exception ex) { Log.Warn($"截图失败 {path}：{ex.Message}"); }
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>PW_RENDERFULLCONTENT：Win10 1809+ 支持，能把 DWM 合成的窗口内容完整画进 DC。</summary>
    private const uint PwRenderFullContent = 0x00000002;

    /// <summary>
    /// 只抓**这个窗口自己的内容**（不受别的窗口遮挡）。成功返回 true。
    /// 失败（老系统 / 特殊窗口）时由调用方退回按屏幕区域抓图。
    /// </summary>
    private static bool SaveWindowContent(IntPtr hwnd, int w, int h, string path)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                bool ok;
                try { ok = PrintWindow(hwnd, hdc, PwRenderFullContent); }
                finally { g.ReleaseHdc(hdc); }
                if (!ok) return false;
            }
            ForceOpaque(bmp);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("PrintWindow 抓图失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 合成窗口的 PrintWindow 结果常常 alpha 全 0（存成 PNG 会变成"全透明/全黑"），
    /// 这里一次性把 alpha 拉满。用 LockBits 整块处理，比逐像素 GetPixel 快几个数量级。
    /// </summary>
    private static void ForceOpaque(System.Drawing.Bitmap bmp)
    {
        System.Drawing.Imaging.BitmapData? data = null;
        try
        {
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var len = data.Stride * bmp.Height;
            var buf = new byte[len];
            Marshal.Copy(data.Scan0, buf, 0, len);
            var anyAlpha = false;
            for (var i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (var i = 3; i < buf.Length; i += 4) buf[i] = 255;
            Marshal.Copy(buf, 0, data.Scan0, len);
        }
        finally { if (data is not null) bmp.UnlockBits(data); }
    }

    /// <summary>按屏幕区域抓图（GDI 抓的是合成后的画面，所以 Mica/亚克力材质会如实出现在截图里）。</summary>
    private static void SaveRegion(int x, int y, int w, int h, string path)
    {
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
