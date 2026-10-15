using System.Windows;
using DeskNest.App.Ui;
using DeskNest.Core;
using DeskNest.Core.Services;

namespace DeskNest.App.Host;

/// <summary>命令总线：搜索面板、托盘、模块都通过它触发动作。</summary>
public static class Commands
{
    public static Action<string>? OrganizeRequested;
    public static Action? ToggleLayerRequested;
    public static Action? ToggleDesktopIconsRequested;
    public static Action? ShowMainWindowRequested;

    public static void Execute(string command) => Execute(command, null);

    public static void Execute(string command, Window? owner)
    {
        try
        {
            switch (command)
            {
                case "cmd://organize":
                    OrganizeRequested?.Invoke("manual-from-command");
                    break;
                case "cmd://newbox":
                case "cmd://settings":
                    ShowMainWindowRequested?.Invoke();
                    break;
                case "cmd://toggleicons":
                    ToggleDesktopIconsRequested?.Invoke();
                    break;
                case "cmd://togglelayer":
                    ToggleLayerRequested?.Invoke();
                    break;
                case "cmd://usage":
                case "cmd://perf":
                case "cmd://api":
                case "cmd://power":
                    ShowMainWindowRequested?.Invoke();
                    break;
                default:
                    Log.Warn("未知命令：" + command);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"命令执行失败 {command}：{ex.Message}");
        }
    }
}
