using Microsoft.Win32;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>开机自启：写 HKCU\...\Run（只写一个值，关闭时删除，不留垃圾）。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DeskNest";
    // WinUI 版是 MSIX 包，裸 exe 路径启动会崩；用独立的键走 shell:AppsFolder。
    private const string MsixValueName = "DeskNestMsix";

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

    /// <summary>
    /// MSIX 版的开机自启：Run 项写 explorer.exe shell:AppsFolder\&lt;PFN&gt;!App，
    /// 而不是裸 exe 路径——WinUI 必须带包身份启动，直接跑 exe 会崩（docs/28 §10 踩过）。
    /// </summary>
    public static bool SetMsixEnabled(bool enabled, string? packageFamilyName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null) return false;
            if (enabled)
            {
                if (string.IsNullOrWhiteSpace(packageFamilyName)) return false;
                var command = $"explorer.exe shell:AppsFolder\\{packageFamilyName}!App";
                key.SetValue(MsixValueName, command);
            }
            else
            {
                key.DeleteValue(MsixValueName, throwOnMissingValue: false);
            }
            Log.Info($"开机自启(MSIX)已{(enabled ? "开启" : "关闭")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("设置开机自启(MSIX)失败：" + ex.Message);
            return false;
        }
    }

    public static bool IsMsixEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(MsixValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }
}
