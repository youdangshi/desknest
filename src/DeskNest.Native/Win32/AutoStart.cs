using Microsoft.Win32;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>开机自启：写 HKCU\...\Run（只写一个值，关闭时删除，不留垃圾）。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskNest";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    public static bool SetEnabled(bool enabled, string? exePath = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null) return false;
            if (enabled)
            {
                var path = exePath ?? Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(path)) return false;
                key.SetValue(ValueName, "\"" + path + "\" --silent");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info($"开机自启已{(enabled ? "开启" : "关闭")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("设置开机自启失败：" + ex.Message);
            return false;
        }
    }
}
