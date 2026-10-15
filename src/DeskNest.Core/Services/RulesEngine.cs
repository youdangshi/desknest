using System.Text.Json;

namespace DeskNest.Core.Services;

public sealed class RuleConditions
{
    public List<string> Extensions { get; set; } = new();   // 含点小写：.png
    public List<string> NameContains { get; set; } = new();
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public int? OlderThanDays { get; set; }
    public int? NewerThanDays { get; set; }
}

public sealed class RuleActions
{
    public string Kind { get; set; } = "MoveToBox";   // MoveToBox | MoveToFolder
    public string? BoxId { get; set; }
    public string? SubFolder { get; set; }
    public bool CreateDateSubFolder { get; set; }
}

public enum FileCategory { Document, Archive, Image, Shortcut, Url, Video, Audio, App, Other }

/// <summary>规则判定与文件分类。默认扩展名分类表参考 360 桌面精简版与常见桌面习惯。</summary>
public static class RulesEngine
{
    public static readonly Dictionary<FileCategory, string[]> CategoryExtensions = new()
    {
        [FileCategory.Document] = new[]
        {
            ".doc", ".docx", ".dot", ".dotm", ".pdf", ".xps", ".htm", ".html", ".mht", ".mhtml", ".xml",
            ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".xlsb", ".xltx", ".xlt", ".csv", ".ods", ".ppt",
            ".pptx", ".pptm", ".ppsx", ".pps", ".wps", ".md", ".epub"
        },
        [FileCategory.Archive] = new[]
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso", ".jar", ".z01", ".zipx", ".lzma", ".001"
        },
        [FileCategory.Image] = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".psd", ".psb", ".svg",
            ".ico", ".heic", ".raw", ".cr2", ".nef", ".ai", ".tga", ".dds"
        },
        [FileCategory.Shortcut] = new[] { ".lnk" },
        [FileCategory.Url] = new[] { ".url", ".website" },
        [FileCategory.Video] = new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".rmvb", ".3gp", ".vob"
        },
        [FileCategory.Audio] = new[]
        {
            ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma", ".ape", ".mid", ".midi", ".aiff"
        },
        [FileCategory.App] = new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".apk", ".appx", ".msix", ".dll" }
    };

    public static string CategoryName(FileCategory c) => c switch
    {
        FileCategory.Document => "文档",
        FileCategory.Archive => "压缩",
        FileCategory.Image => "图片",
        FileCategory.Shortcut => "快捷方式",
        FileCategory.Url => "网址",
        FileCategory.Video => "视频",
        FileCategory.Audio => "音频",
        FileCategory.App => "程序",
        _ => "其他"
    };

    public static FileCategory Classify(string path)
    {
        if (Directory.Exists(path)) return FileCategory.Other;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        foreach (var (cat, exts) in CategoryExtensions)
            if (Array.IndexOf(exts, ext) >= 0) return cat;
        return FileCategory.Other;
    }

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public static bool Matches(RuleConditions c, string path, DateTimeOffset now)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path);

        if (c.Extensions.Count > 0 && !c.Extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (c.NameContains.Count > 0 && !c.NameContains.Any(k => !string.IsNullOrWhiteSpace(k)
                && name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            if (!Directory.Exists(path))
            {
                var info = new FileInfo(path);
                if (c.MinSizeBytes is { } mn && info.Length < mn) return false;
                if (c.MaxSizeBytes is { } mx && info.Length > mx) return false;
                if (c.OlderThanDays is { } older && info.LastWriteTime > now.AddDays(-older).LocalDateTime) return false;
                if (c.NewerThanDays is { } newer && info.LastWriteTime < now.AddDays(-newer).LocalDateTime) return false;
            }
        }
        catch { return false; }
        return true;
    }

    public static RuleConditions ParseConditions(string json)
    {
        try { return JsonSerializer.Deserialize<RuleConditions>(json, J) ?? new RuleConditions(); }
        catch { return new RuleConditions(); }
    }

    public static RuleActions ParseActions(string json)
    {
        try { return JsonSerializer.Deserialize<RuleActions>(json, J) ?? new RuleActions(); }
        catch { return new RuleActions(); }
    }

    public static string ToJson(object o) => JsonSerializer.Serialize(o, J);
}
