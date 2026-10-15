using System.Runtime.Loader;
using DeskNest.Core.Services;

namespace DeskNest.Core.Modules;

/// <summary>
/// 模块宿主：扫描 modules\ 下的 dll，用独立 AssemblyLoadContext 加载，读取实现 IDeskNestModule 的类型。
/// 未安装的模块不会加载、不会运行、不占内存；加载失败的模块只影响它自己。
/// </summary>
public sealed class ModuleHost
{
    private readonly SettingsService _settings;
    private readonly IModuleContext _context;
    private readonly Dictionary<string, LoadedModule> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);

    public sealed record LoadedModule(IDeskNestModule Instance, AssemblyLoadContext Context, string AssemblyPath, bool IsEnabled);

    public ModuleHost(SettingsService settings, IModuleContext context)
    {
        _settings = settings;
        _context = context;
        foreach (var id in settings.Get("modules.enabled", new List<string>())) _enabled.Add(id);
    }

    public IReadOnlyDictionary<string, LoadedModule> Loaded => _loaded;
    public IEnumerable<LoadedModule> All => _loaded.Values;
    public bool IsEnabled(string id) => _enabled.Contains(id);

    public void SetEnabled(string id, bool enabled)
    {
        if (enabled) _enabled.Add(id); else _enabled.Remove(id);
        _settings.Set("modules.enabled", _enabled.ToList());
        if (_loaded.TryGetValue(id, out var m))
        {
            try { m.Instance.OnEnabledChanged(enabled); }
            catch (Exception ex) { Log.Warn($"模块 {id} 状态回调失败：{ex.Message}"); }
        }
        Log.Info($"模块 {id} {(enabled ? "已启用" : "已停用")}");
    }

    public int Scan()
    {
        var dir = AppPaths.ModulesRoot;
        if (!Directory.Exists(dir)) return 0;
        var count = 0;
        foreach (var dll in Directory.EnumerateFiles(dir, "DeskNest.Module.*.dll"))
        {
            try
            {
                var ctx = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dll), isCollectible: true);
                var asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(dll));
                foreach (var type in asm.GetTypes().Where(t => typeof(IDeskNestModule).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    if (Activator.CreateInstance(type) is not IDeskNestModule module) continue;
                    var id = module.Info.Id;
                    module.Initialize(_context);
                    var on = module.Info.DefaultEnabled || _enabled.Contains(id);
                    module.OnEnabledChanged(on);
                    _loaded[id] = new LoadedModule(module, ctx, dll, on);
                    count++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"模块加载失败 {dll}：{ex.Message}");
            }
        }
        Log.Info($"模块扫描完成：加载 {count} 个");
        return count;
    }

    public void Shutdown()
    {
        foreach (var m in _loaded.Values)
        {
            try { m.Context.Unload(); } catch { }
        }
        _loaded.Clear();
    }
}
