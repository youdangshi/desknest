using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>
/// 低级键盘钩子：只对外暴露"有人按了键"这一个事实（不传字符、不传键值、不落盘）。
/// 默认不安装；只有用户在桌宠设置里显式打开"打字陪伴"时才启动。带看门狗自动重装。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private Thread? _thread;
    private IntPtr _hook = IntPtr.Zero;
    private volatile bool _running;
    private uint _lastEventTick;

    public event Action? KeyPressed;
    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "DeskNest.KeyHook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Log.Info("键盘跟随已开启（只统计是否有按键，不记录内容）");
    }

    public void Stop()
    {
        _running = false;
        Log.Info("键盘跟随已关闭");
    }

    private void Loop()
    {
        while (_running)
        {
            var module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, Callback, module, 0);
            if (_hook == IntPtr.Zero)
            {
                Log.Warn("键盘钩子安装失败，2 秒后重试");
                Thread.Sleep(2000);
                continue;
            }
            while (_running && NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            if (_running) Thread.Sleep(500);
        }
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var w = (int)wParam;
                if (w is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    var tick = (uint)Environment.TickCount;
                    if (tick - _lastEventTick > 40)
                    {
                        _lastEventTick = tick;
                        KeyPressed?.Invoke();
                    }
                }
            }
        }
        catch { }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        try { if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook); } catch { }
    }
}
