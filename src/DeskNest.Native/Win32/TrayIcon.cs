using System.Runtime.InteropServices;
using DeskNest.Core;

namespace DeskNest.Native.Win32;

/// <summary>托盘菜单项。Id 会原样回传给 onCommand。</summary>
public sealed record TrayMenuItem(int Id, string Text, bool IsSeparator = false);

/// <summary>
/// 系统托盘图标（Win32 Shell_NotifyIconW）。
///
/// 为什么不用 WinForms NotifyIcon：WinUI 3 项目不引 WinForms，且这类 App 的托盘
/// 本来就是 Shell_NotifyIcon + 一个隐藏窗口，直接写反而少一层依赖。
///
/// 消息窗口跑在独立 STA 线程：右键菜单弹在该线程，不阻塞 UI；
/// 左键 / 菜单点击通过回调交给宿主，宿主自行 DispatcherQueue.TryEnqueue 切回 UI 线程。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "DeskNestTrayMessageWindow";
    private const uint WmUser = 0x0400;
    private const uint WmTrayCallback = WmUser + 1;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmNull = 0x0000;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;

    private const int IconId = 1;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;
    private const uint NimAdd = 0x0000;
    private const uint NimModify = 0x0001;
    private const uint NimDelete = 0x0002;

    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0004;

    private const int IDI_APPLICATION = 32512;

    // 静态 WndProc 必须强引用，否则委托被 GC 后来的消息会直接崩进程
    // （项目里 DispatcherQueueTimer 已踩过同样的坑）。
    private static readonly WndProcDelegate WndProcStatic = WndProc;
    private static readonly Dictionary<IntPtr, TrayIcon> Instances = new();
    private static readonly object InstancesLock = new();

    private readonly Thread _thread;
    private readonly IReadOnlyList<TrayMenuItem> _items;
    private readonly Action<int> _onCommand;
    private readonly Action _onActivate;
    private readonly string _iconPath;
    private readonly ManualResetEventSlim _ready = new(false);

    private IntPtr _hwnd;
    private IntPtr _iconHandle;
    private string _tooltip;
    private volatile bool _disposed;

    /// <param name="iconPath">ico 文件路径；文件缺失时降级为系统默认应用图标。</param>
    /// <param name="tooltip">鼠标悬停提示。</param>
    /// <param name="items">右键菜单项。</param>
    /// <param name="onCommand">菜单项被选中的 Id。</param>
    /// <param name="onActivate">左键单击/双击托盘图标。</param>
    public TrayIcon(string iconPath, string tooltip, IReadOnlyList<TrayMenuItem> items,
                    Action<int> onCommand, Action onActivate)
    {
        _iconPath = iconPath ?? "";
        _tooltip = tooltip ?? "DeskNest";
        _items = items ?? Array.Empty<TrayMenuItem>();
        _onCommand = onCommand ?? (_ => { });
        _onActivate = onActivate ?? (() => { });

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DeskNest.Tray"
        };
        try { _thread.SetApartmentState(ApartmentState.STA); } catch { }
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            Log.Warn("托盘消息窗口启动超时（5s）");
    }

    /// <summary>更新悬停提示（例如"桌巢 DeskNest · 3 个收纳盒"）。</summary>
    public void SetTooltip(string text)
    {
        _tooltip = text ?? "";
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            var data = NewData(_hwnd);
            data.uFlags = NifTip;
            data.szTip = _tooltip;
            if (!Shell_NotifyIconW(NimModify, ref data))
                Log.Warn("托盘提示更新失败");
        }
        catch (Exception ex) { Log.Warn("托盘提示更新异常：" + ex.Message); }
    }

    private void Run()
    {
        var module = GetModuleHandle(null);
        var wc = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcStatic),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = module,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = "",
            lpszClassName = WindowClassName
        };
        var atom = RegisterClassW(ref wc);
        if (atom == 0)
        {
            var err = Marshal.GetLastWin32Error();
            // 类名重复（进程里已有实例）不算错，继续创建窗口
            if (err != 1410) Log.Warn($"托盘窗口类注册失败：{err}");
        }

        _hwnd = CreateWindowExW(0, WindowClassName, "DeskNestTray", 0,
            0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, module, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Log.Warn($"托盘消息窗口创建失败：{Marshal.GetLastWin32Error()}");
            _ready.Set();
            return;
        }

        lock (InstancesLock) Instances[_hwnd] = this;

        // ico 文件里第一张通常是 32x32，托盘会自己缩放；加载失败就用系统默认图标。
        _iconHandle = LoadIconFromFile(_iconPath);
        var data = NewData(_hwnd);
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayCallback;
        data.hIcon = _iconHandle;
        data.szTip = _tooltip;
        if (!Shell_NotifyIconW(NimAdd, ref data))
            Log.Warn($"托盘图标添加失败：{Marshal.GetLastWin32Error()}");
        else
            Log.Info("托盘图标已创建");

        _ready.Set();

        var msg = new MSG();
        int ret;
        while ((ret = GetMessage(out msg, IntPtr.Zero, 0, 0)) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        Log.Info($"托盘消息线程退出（GetMessage={ret}）");
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        TrayIcon? self;
        lock (InstancesLock) Instances.TryGetValue(hwnd, out self);

        switch (msg)
        {
            case WmTrayCallback:
            {
                if (self is null) return IntPtr.Zero;
                var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg is WmRButtonUp or WmContextMenu)
                    self.ShowMenu();
                else if (mouseMsg is WmLButtonUp or WmLButtonDblClk)
                    self.FireActivate();
                return IntPtr.Zero;
            }
            case WmClose:
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WmDestroy:
            {
                if (self is not null)
                {
                    var data = NewData(hwnd);
                    data.uFlags = 0;
                    Shell_NotifyIconW(NimDelete, ref data);
                    if (self._iconHandle != IntPtr.Zero) DestroyIcon(self._iconHandle);
                    self._iconHandle = IntPtr.Zero;
                    lock (InstancesLock) Instances.Remove(hwnd);
                }
                UnregisterClassW(WindowClassName, GetModuleHandle(null));
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            default:
                return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private void ShowMenu()
    {
        if (_items.Count == 0) return;
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            foreach (var item in _items)
            {
                AppendMenuW(menu, item.IsSeparator ? MfSeparator : MfString,
                    new IntPtr(item.Id), item.IsSeparator ? null : item.Text);
            }
            GetCursorPos(out var pt);
            // 经典顺序：先置前，再弹菜单，弹完投递一个 WM_NULL，
            // 否则点击菜单外面时菜单不会消失。message-only 窗口置前可能失败，忽略即可。
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightAlign | TpmBottomAlign,
                pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessage(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (cmd > 0) _onCommand((int)cmd);
        }
        catch (Exception ex) { Log.Warn("托盘菜单异常：" + ex.Message); }
        finally { DestroyMenu(menu); }
    }

    private void FireActivate()
    {
        if (_disposed) return;
        try { _onActivate(); } catch (Exception ex) { Log.Warn("托盘激活回调异常：" + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            if (!_thread.Join(TimeSpan.FromSeconds(2)))
                Log.Warn("托盘消息线程未在 2s 内退出");
        }
        _ready.Dispose();
    }

    private static NOTIFYICONDATAW NewData(IntPtr hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = hwnd,
        uID = IconId
    };

    private static IntPtr LoadIconFromFile(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var h = LoadImageW(IntPtr.Zero, path, /* IMAGE_ICON */ 1, 0, 0,
                /* LR_LOADFROMFILE | LR_DEFAULTSIZE */ 0x0010 | 0x0040);
            if (h != IntPtr.Zero) return h;
            Log.Warn($"托盘图标加载失败：{path}（{Marshal.GetLastWin32Error()}）");
        }
        return LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public int lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y,
        IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
