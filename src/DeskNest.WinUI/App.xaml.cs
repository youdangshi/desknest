using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace DeskNest.WinUI;

public partial class App : Application
{
    internal static readonly string LogPath = BuildLogPath();

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
        }
        catch (Exception ex)
        {
            Log("OnLaunched FAILED: " + ex);
            throw;
        }
    }
}
