using System.Reflection;

namespace DeskNest.App.Host;

/// <summary>应用自身的版本信息（关于页与「检查更新」用）。版本号只在 csproj 的 &lt;Version&gt; 里改一处。</summary>
public static class AppInfo
{
    /// <summary>形如 1.1.0；读不到时退回 0.0.0 而不是抛异常。</summary>
    public static string Version { get; } = ReadVersion();

    public static string DisplayVersion => "v" + Version;

    public static string ProductName => "桌巢 DeskNest";

    private static string ReadVersion()
    {
        try
        {
            var assembly = typeof(AppInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0].Trim();

            var version = assembly.GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "0.0.0";
        }
    }
}
