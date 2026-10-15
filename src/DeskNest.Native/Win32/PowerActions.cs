using System.Diagnostics;
using DeskNest.Core;
using DeskNest.Core.Services;

namespace DeskNest.Native.Win32;

/// <summary>关机/重启/睡眠：优先 ExitWindowsEx（带权限提升），失败回退 shutdown.exe。</summary>
public static class PowerActions
{
    private static bool EnableShutdownPrivilege()
    {
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
                return false;
            if (!NativeMethods.LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) return false;
            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
                {
                    Luid = luid,
                    Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                }
            };
            return NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { return false; }
    }

    public static void Execute(PowerActionKind kind)
    {
        try
        {
            switch (kind)
            {
                case PowerActionKind.Shutdown:
                    if (!(EnableShutdownPrivilege() && NativeMethods.ExitWindowsEx(
                            NativeMethods.EWX_SHUTDOWN | NativeMethods.EWX_FORCEIFHUNG, 0)))
                        Run("shutdown", "/s /t 0 /f");
                    break;
                case PowerActionKind.Restart:
                    if (!(EnableShutdownPrivilege() && NativeMethods.ExitWindowsEx(
                            NativeMethods.EWX_REBOOT | NativeMethods.EWX_FORCEIFHUNG, 0)))
                        Run("shutdown", "/r /t 0 /f");
                    break;
                case PowerActionKind.Sleep:
                    Run("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                    break;
            }
            Log.Info($"已执行电源动作：{kind}");
        }
        catch (Exception ex)
        {
            Log.Error($"电源动作失败：{ex.Message}");
        }
    }

    public static void AbortPendingShutdown() => Run("shutdown", "/a");

    public static void ScheduleSystemShutdown(int seconds)
        => Run("shutdown", $"/s /t {Math.Max(0, seconds)}");

    private static void Run(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error($"{exe} {args} 执行失败：{ex.Message}");
        }
    }
}
