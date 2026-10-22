using System.Runtime.InteropServices;
using System.Text;

namespace DeskNest.Native.Win32;

internal static class NativeMethods
{
    // ---------- 窗口 ----------
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // 64 位下设置窗口里的指针型字段（如 GWLP_HWNDPARENT）必须走 Ptr 版本，
    // 用上面的 SetWindowLong 会把指针截成 32 位
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    internal const int SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // ---------- 拖动窗口：用 Win32 原生移动循环，比手动算坐标可靠 ----------
    internal const int WM_NCLBUTTONDOWN = 0x00A1;
    internal const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---------- 热键 ----------
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---------- 键盘钩子 ----------
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_HOTKEY = 0x0312;

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    // ---------- 系统指标 ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public readonly ulong ToUlong() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    // ---------- 图标 ----------
    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    // ---------- 显示器 ----------
    internal const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT pt);

    // ---------- 关机/权限 ----------
    internal const uint EWX_LOGOFF = 0x0;
    internal const uint EWX_SHUTDOWN = 0x1;
    internal const uint EWX_REBOOT = 0x2;
    internal const uint EWX_POWEROFF = 0x8;
    internal const uint EWX_FORCEIFHUNG = 0x10;
    internal const uint EWX_HYBRID_SHUTDOWN = 0x00400000;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID_AND_ATTRIBUTES { public long Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint SE_PRIVILEGE_ENABLED = 0x0002;

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    // ---------- DPAPI ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, uint dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr hMem);

    // ---------- GDI（图标渲染成像素用） ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    internal const uint DI_NORMAL = 0x0003;
    internal const uint BLACKNESS = 0x00000042;

    // ---------- 窗口透明度（分层窗口） ----------
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr hDC, ref BITMAPINFO pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] internal static extern bool PatBlt(IntPtr hDC, int x, int y, int w, int h, uint rop);
    [DllImport("user32.dll")] internal static extern bool DrawIconEx(IntPtr hDC, int x, int y, IntPtr hIcon,
        int cx, int cy, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);
}
