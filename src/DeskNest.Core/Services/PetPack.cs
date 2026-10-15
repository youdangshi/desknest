using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskNest.Core.Services;

public sealed class PetPackManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = "petpack/1";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("license")] public string License { get; set; } = "unknown";
    [JsonPropertyName("canvas")] public PetCanvas Canvas { get; set; } = new();
    [JsonPropertyName("sprites")] public Dictionary<string, PetSprite> Sprites { get; set; } = new();
    [JsonPropertyName("states")] public Dictionary<string, PetStateRule> States { get; set; } = new();
}

public sealed class PetCanvas
{
    [JsonPropertyName("width")] public int Width { get; set; } = 256;
    [JsonPropertyName("height")] public int Height { get; set; } = 256;
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;
}

public sealed class PetSprite
{
    [JsonPropertyName("type")] public string Type { get; set; } = "file";     // file | sheet | apng | gif
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("frame")] public int[]? Frame { get; set; }
    [JsonPropertyName("frames")] public int Frames { get; set; } = 1;
    [JsonPropertyName("fps")] public int Fps { get; set; } = 12;
    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
}

public sealed class PetStateRule
{
    [JsonPropertyName("state")] public string State { get; set; } = "idle";
    [JsonPropertyName("holdMs")] public int HoldMs { get; set; } = 600;
    [JsonPropertyName("cooldownMs")] public int CooldownMs { get; set; } = 100;
    [JsonPropertyName("threshold")] public double? Threshold { get; set; }
}

public sealed record InstalledPetPack(string Id, string Name, string Author, string License, string RootPath, PetPackManifest Manifest);

/// <summary>素材包（.petpack = zip）：导入/导出/列举/校验。清单必须声明授权信息。</summary>
public static class PetPackStore
{
    public static List<InstalledPetPack> Installed()
    {
        var list = new List<InstalledPetPack>();
        if (!Directory.Exists(AppPaths.PetsRoot)) return list;
        foreach (var dir in Directory.EnumerateDirectories(AppPaths.PetsRoot))
        {
            var manifestFile = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestFile)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<PetPackManifest>(File.ReadAllText(manifestFile));
                if (m is null) continue;
                list.Add(new InstalledPetPack(m.Id, m.Name, m.Author, m.License, dir, m));
            }
            catch (Exception ex) { Log.Warn($"素材包读取失败 {dir}：{ex.Message}"); }
        }
        return list;
    }

    /// <summary>导入 .petpack（zip）：校验清单 → 解压到 pets\&lt;id&gt;。返回提示信息。</summary>
    public static (bool Ok, string Message) Import(string petPackPath)
    {
        try
        {
            if (!File.Exists(petPackPath)) return (false, "文件不存在");
            using var zip = ZipFile.OpenRead(petPackPath);
            var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null) return (false, "缺少 manifest.json，不是合法的 .petpack");

            PetPackManifest? m;
            using (var s = manifestEntry.Open())
            using (var sr = new StreamReader(s))
                m = JsonSerializer.Deserialize<PetPackManifest>(sr.ReadToEnd());
            if (m is null || string.IsNullOrWhiteSpace(m.Id)) return (false, "manifest.json 内容不完整（缺少 id）");

            var target = Path.Combine(AppPaths.PetsRoot, m.Id);
            Directory.CreateDirectory(target);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var full = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!full.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;   // 防目录穿越
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                entry.ExtractToFile(full, overwrite: true);
            }
            var licenseNote = string.IsNullOrWhiteSpace(m.License) || m.License == "unknown"
                ? "（注意：该素材包未声明授权，请确认你有权使用）" : $"（授权：{m.License}）";
            Log.Info($"素材包已导入：{m.Name} {licenseNote}");
            return (true, $"已导入「{m.Name}」{licenseNote}");
        }
        catch (Exception ex)
        {
            Log.Error("素材包导入失败：" + ex.Message);
            return (false, ex.Message);
        }
    }

    public static void Remove(string id)
    {
        try
        {
            var dir = Path.Combine(AppPaths.PetsRoot, id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex) { Log.Warn("删除素材包失败：" + ex.Message); }
    }

    /// <summary>生成一个内置示例素材包（自绘几何形状，不涉及任何第三方素材版权），保证开箱可用。</summary>
    public static InstalledPetPack EnsureBuiltin()
    {
        var id = "builtin-nest";
        var dir = Path.Combine(AppPaths.PetsRoot, id);
        var manifestFile = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestFile))
        {
            Directory.CreateDirectory(dir);
            var manifest = new PetPackManifest
            {
                Id = id,
                Name = "小巢（内置）",
                Author = "DeskNest",
                License = "MIT（随程序内置，自绘）",
                Canvas = new PetCanvas { Width = 160, Height = 160 },
                Sprites = new Dictionary<string, PetSprite>
                {
                    ["idle"] = new PetSprite { Type = "drawn", File = "idle" },
                    ["typing"] = new PetSprite { Type = "drawn", File = "typing" },
                    ["click"] = new PetSprite { Type = "drawn", File = "click" },
                    ["sleep"] = new PetSprite { Type = "drawn", File = "sleep" },
                    ["tired"] = new PetSprite { Type = "drawn", File = "tired" }
                },
                States = new Dictionary<string, PetStateRule>
                {
                    ["default"] = new PetStateRule { State = "idle" },
                    ["onKeyPress"] = new PetStateRule { State = "typing", HoldMs = 420, CooldownMs = 60 },
                    ["onClick"] = new PetStateRule { State = "click", HoldMs = 600 },
                    ["onHighCpu"] = new PetStateRule { State = "tired", Threshold = 85, HoldMs = 6000 }
                }
            };
            File.WriteAllText(manifestFile, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Installed().FirstOrDefault(p => p.Id == id)!;
    }
}
