using System.Text.Json;

namespace DeskNest.Core.Services;

/// <summary>设置存储：settings.json + .bak 双写，损坏时自动回退备份。</summary>
public class SettingsService
{
    private readonly Dictionary<string, JsonElement> _map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions J = new() { WriteIndented = true };
    private readonly object _gate = new();

    public SettingsService()
    {
        Load();
    }

    public event Action? Changed;

    private void Load()
    {
        foreach (var file in new[] { AppPaths.SettingsFile, AppPaths.SettingsFile + ".bak" })
        {
            try
            {
                if (!File.Exists(file)) continue;
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (dict is null) continue;
                foreach (var (k, v) in dict) _map[k] = v;
                Log.Info($"设置已加载：{Path.GetFileName(file)}（{_map.Count} 项）");
                return;
            }
            catch (Exception ex) { Log.Warn($"读取设置失败 {file}：{ex.Message}"); }
        }
    }

    private void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                var json = JsonSerializer.Serialize(_map, J);
                if (File.Exists(AppPaths.SettingsFile)) File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bak", true);
                File.WriteAllText(AppPaths.SettingsFile, json);
                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Warn("保存设置失败：" + ex.Message); }
        }
    }

    public T Get<T>(string key, T fallback)
    {
        try
        {
            if (_map.TryGetValue(key, out var el)) return el.Deserialize<T>(J) ?? fallback;
        }
        catch { }
        return fallback;
    }

    public void Set<T>(string key, T value)
    {
        _map[key] = JsonSerializer.SerializeToElement(value, J);
        Save();
    }

    public IReadOnlyDictionary<string, JsonElement> All => _map;
}
