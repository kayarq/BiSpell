using System.Runtime.InteropServices;

namespace BiSpell.Services;

/// <summary>
/// Notification-area icon via Win32 Shell_NotifyIcon (no WinForms).
/// WinForms + WinUI 3 causes MC6000 (WPF XAML pipeline) on modern SDKs.
/// Menu: Show BiSpell / Quit. No system-wide other-app injection.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTray = WmApp + 1;
    private const int WmCommand = 0x0111;
    private const int WmDestroy = 0x0002;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NifInfo = 0x00000010;
    /// <summary>NIIF_INFO — standard info balloon icon.</summary>
    private const int NiifInfo = 0x00000001;
    private const int IdShow = 1001;
    private const int IdQuit = 1002;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _hIcon;
    private readonly WndProc _wndProc; // keep alive
    private bool _disposed;
    private bool _added;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? QuitRequested;

    public TrayIconService()
    {
        _wndProc = WindowProc;
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "BiSpellTrayHiddenWindow",
        };
        var atom = RegisterClassEx(ref wc);
        if (atom == 0 && Marshal.GetLastWin32Error() != 1410) // already exists
        {
            // Class may already exist from prior run in same process; still try CreateWindow.
        }

        _hwnd = CreateWindowEx(
            0, "BiSpellTrayHiddenWindow", "BiSpellTray",
            0, 0, 0, 0, 0,
            new IntPtr(-3), // HWND_MESSAGE
            IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx for tray failed.");

        _hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
        var data = BuildNotifyData("BiSpell");
        if (!Shell_NotifyIcon(NimAdd, ref data))
            throw new InvalidOperationException("Shell_NotifyIcon(NIM_ADD) failed.");
        _added = true;
    }

    /// <summary>
    /// Show a notification-area balloon (NIF_INFO / NIM_MODIFY). Best-effort:
    /// Focus Assist / OS policy may suppress balloons — tip is still updated.
    /// Never throws.
    /// </summary>
    public void ShowBalloon(string title, string text, int timeoutMs = 2500)
    {
        if (_disposed || !_added || _hwnd == IntPtr.Zero)
            return;

        try
        {
            string safeTitle = Truncate(title ?? "BiSpell", 63);
            string safeText = Truncate(text ?? string.Empty, 255);
            // Keep tip in sync so hover still shows last result if balloon is blocked.
            string tip = Truncate(
                string.IsNullOrEmpty(safeText) ? "BiSpell" : $"BiSpell — {safeText}",
                127);

            var data = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NifMessage | NifIcon | NifTip | NifInfo,
                uCallbackMessage = WmTray,
                hIcon = _hIcon,
                szTip = tip,
                szInfo = safeText,
                uTimeoutOrVersion = (uint)Math.Clamp(timeoutMs, 500, 30000),
                szInfoTitle = safeTitle,
                dwInfoFlags = NiifInfo,
            };

            if (!Shell_NotifyIcon(NimModify, ref data))
            {
                // Fallback: tip-only update (no balloon).
                data.uFlags = NifMessage | NifIcon | NifTip;
                data.szInfo = string.Empty;
                data.szInfoTitle = string.Empty;
                data.dwInfoFlags = 0;
                Shell_NotifyIcon(NimModify, ref data);
            }
        }
        catch
        {
            // Balloon is feedback only — never fail callers.
        }
    }

    /// <summary>Update the tray tooltip only (no balloon). Never throws.</summary>
    public void SetTip(string tip)
    {
        if (_disposed || !_added || _hwnd == IntPtr.Zero)
            return;

        try
        {
            var data = BuildNotifyData(Truncate(tip ?? "BiSpell", 127));
            Shell_NotifyIcon(NimModify, ref data);
        }
        catch
        {
            // ignore
        }
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxChars ? value : value.Substring(0, maxChars);
    }

    private NotifyIconData BuildNotifyData(string tip)
    {
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTray,
            hIcon = _hIcon,
            szTip = tip ?? "BiSpell",
            szInfo = string.Empty,
            uTimeoutOrVersion = 0,
            szInfoTitle = string.Empty,
            dwInfoFlags = 0,
        };
        return data;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmTray)
        {
            int mouse = lParam.ToInt32() & 0xFFFF;
            if (mouse == WmLButtonDblClk)
            {
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }
            if (mouse == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WmCommand)
        {
            int id = wParam.ToInt32() & 0xFFFF;
            if (id == IdShow) ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            if (id == IdQuit) QuitRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }
        else if (msg == WmDestroy)
        {
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        AppendMenu(menu, 0, new UIntPtr(IdShow), "Show BiSpell");
        AppendMenu(menu, 0x0800, UIntPtr.Zero, string.Empty); // MF_SEPARATOR
        AppendMenu(menu, 0, new UIntPtr(IdQuit), "Quit");

        GetCursorPos(out var pt);
        // Required so menu dismisses correctly when clicking away.
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(menu, 0x0100 /* TPM_RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var data = BuildNotifyData("BiSpell");
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    /// <summary>
    /// NOTIFYICONDATAW with balloon fields (Vista+ layout without guidItem/hBalloonIcon).
    /// cbSize is set to sizeof(this) so Shell_NotifyIcon accepts NIF_INFO.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        /// <summary>uTimeout (balloon) or uVersion depending on message.</summary>
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
