using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;

namespace DeskNest.WinUI;

public partial class App : Application
{
    internal static readonly string LogPath = BuildLogPath();

    /// <summary>与 WPF 版共用的数据库（WAL 模式，两进程同时读写没问题）。</summary>
    internal static DeskNest.Core.Data.Repository? Repo;

    /// <summary>文件搬进/搬出盒子存储目录（与 WPF 版同一个实现，数据互通）。</summary>
    internal static DeskNest.Core.Services.FileOps? Files;

    /// <summary>盒子生命周期（删除盒子会先把文件还原回原位置）。</summary>
    internal static DeskNest.Core.Services.BoxService? Boxes;

    /// <summary>当前开着的盒子窗口（拖动时互相吸附要用）。</summary>
    internal static readonly List<Windows.BoxWindow> BoxWindows = new();

    /// <summary>
    /// 打开（或聚焦）一个盒子窗口。同一个盒子只开一个窗口；隐藏的盒子打开前先取消隐藏。
    /// </summary>
    internal static Windows.BoxWindow OpenBox(DeskNest.Core.BoxRecord box)
    {
        var existing = BoxWindows.FirstOrDefault(w => w.BoxId == box.Id);
        if (existing is not null)
        {
            existing.Activate();
            return existing;
        }
        if (box.IsHidden)
        {
            box.IsHidden = false;
            try { Repo?.Upsert(box); } catch (Exception ex) { Log("取消隐藏失败：" + ex.Message); }
        }
        var win = new Windows.BoxWindow(box);
        BoxWindows.Add(win);
        win.Activate();
        return win;
    }

    internal static void CloseBox(string boxId)
    {
        var win = BoxWindows.FirstOrDefault(w => w.BoxId == boxId);
        win?.Close();
    }

    /// <summary>盒子数据变了（新建/删除/改名/显示隐藏）后，让主窗口收纳盒页刷新。</summary>
    internal static event Action? BoxesChanged;

    internal static void NotifyBoxesChanged()
    {
        try { BoxesChanged?.Invoke(); } catch (Exception ex) { Log("BoxesChanged 通知失败：" + ex.Message); }
    }

    private Window? _window;

    private static string BuildLogPath()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskNest");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "winui.log");
        }
        catch { return Path.Combine(AppContext.BaseDirectory, "winui.log"); }
    }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n"); } catch { }
    }

    public App()
    {
        Log("App ctor: begin");
        UnhandledException += (_, e) => Log("UnhandledException: " + e.Exception);
        try
        {
            InitializeComponent();
            Log("InitializeComponent: ok");
        }
        catch (Exception ex)
        {
            Log("InitializeComponent FAILED: " + ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched: begin");
        try
        {
            _window = new MainWindow();
            Log("MainWindow created");
            _window.Activate();
            Log("Activate called");

            // P2：从数据库恢复所有"显示中"的盒子（与 WPF 版同库，位置/内容互通）；
            // 库里一个都没有时放一个演示盒子，保证有东西可看。
            try
            {
                var db = new DeskNest.Core.Data.Db();
                Repo = new DeskNest.Core.Data.Repository(db);
                Files = new DeskNest.Core.Services.FileOps(Repo);
                Boxes = new DeskNest.Core.Services.BoxService(Repo, Files);
                var boxes = Repo.Boxes().Where(b => !b.IsHidden).ToList();
                Log($"boxes in db: {boxes.Count}");
                foreach (var box in boxes)
                {
                    var win = new Windows.BoxWindow(box);
                    BoxWindows.Add(win);
                    win.Activate();
                }
                if (boxes.Count == 0)
                {
                    var demo = new Windows.BoxWindow(new DeskNest.Core.BoxRecord { Name = "演示收纳盒" });
                    BoxWindows.Add(demo);
                    demo.Activate();
                    Log("no boxes, demo BoxWindow activated");
                }
            }
            catch (Exception ex)
            {
                Log("load boxes FAILED: " + ex);
            }
        }
        catch (Exception ex)
        {
            Log("OnLaunched FAILED: " + ex);
            throw;
        }
    }
}
