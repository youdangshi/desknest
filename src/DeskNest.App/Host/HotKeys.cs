using System.Windows.Interop;
using DeskNest.Core;
using DeskNest.Native.Win32;

namespace DeskNest.App.Host;

/// <summary>
/// 全局热键宿主：用一个不可见窗口接收 WM_HOTKEY。
/// 带看门狗：每 30 秒检查一次，热键被别的程序抢走时自动抢回。
/// </summary>
public sealed class HotKeys : IDisposable
{
    public const int IdSearch = 0x5301;
    public const int IdToggleLayer = 0x5302;

    private HwndSource? _source;
    private System.Windows.Threading.DispatcherTimer? _watchdog;
    private bool _searchRegistered;
    private bool _layerRegistered;

    public event Action? SearchPressed;
    public event Action? ToggleLayerPressed;

    public void Install()
    {
        var p = new HwndSourceParameters("DeskNest.HotKeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x00000080   // WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        RegisterAll();

        _watchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _watchdog.Tick += (_, _) => RegisterAll();
        _watchdog.Start();
    }

    private void RegisterAll()
    {
        if (_source is null) return;
        var hwnd = _source.Handle;
        if (!_searchRegistered)
        {
            _searchRegistered = GlobalHotKey.Register(hwnd, IdSearch, GlobalHotKey.Mods.Alt, 0x20);   // Alt+Space
            if (_searchRegistered) Log.Info("热键已注册：Alt+Space（快捷搜索）");
        }
        if (!_layerRegistered)
        {
            _layerRegistered = GlobalHotKey.Register(hwnd, IdToggleLayer, GlobalHotKey.Mods.Alt, 0xC0); // Alt+`
            if (_layerRegistered) Log.Info("热键已注册：Alt+`（显示/隐藏桌面层）");
        }
    }

    /// <summary>热键被抢占后热键状态需要复位，等待下一轮看门狗重试。</summary>
    public void InvalidateRegistration()
    {
        _searchRegistered = false;
        _layerRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotKey.WmHotKey)
        {
            var id = wParam.ToInt32();
            if (id == IdSearch) { SearchPressed?.Invoke(); handled = true; }
            else if (id == IdToggleLayer) { ToggleLayerPressed?.Invoke(); handled = true; }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            _watchdog?.Stop();
            if (_source is not null)
            {
                GlobalHotKey.Unregister(_source.Handle, IdSearch);
                GlobalHotKey.Unregister(_source.Handle, IdToggleLayer);
                _source.RemoveHook(WndProc);
                _source.Dispose();
            }
        }
        catch { }
    }
}
