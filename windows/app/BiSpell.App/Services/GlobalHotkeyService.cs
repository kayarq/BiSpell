using System.Runtime.InteropServices;

namespace BiSpell.Services;

/// <summary>
/// Global hotkey via Win32 <c>RegisterHotKey</c> on a dedicated message-only HWND
/// (same pattern as <see cref="TrayIconService"/>; distinct window class).
/// <para>
/// Primary binding: <b>Ctrl+Alt+.</b> (<c>MOD_CONTROL|MOD_ALT</c>, <c>VK_OEM_PERIOD</c>).
/// Fallback if primary fails: <b>Win+Shift+.</b> (<c>MOD_WIN|MOD_SHIFT</c>, same VK).
/// </para>
/// <para>
/// <b>Threading:</b> <see cref="HotkeyPressed"/> is raised on the thread that pumps
/// the message-only window (typically the UI/main thread that created the service).
/// GLUE must still marshal to the WinUI dispatcher if the handler touches UI/engine.
/// </para>
/// <para>
/// Smoke: when <c>BISPELL_SMOKE=1</c>/<c>true</c>, <see cref="TryRegister"/> returns false
/// immediately and never calls <c>RegisterHotKey</c> (defense-in-depth; GLUE should also skip).
/// </para>
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>Reserved hotkey id for clipboard utility (single MVP combo).</summary>
    public const int HotkeyIdClipboardUtility = 1;

    private const uint WmHotkey = 0x0312;
    private const uint WmDestroy = 0x0002;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    /// <summary>Windows 7+: suppress auto-repeat of the hotkey.</summary>
    private const uint ModNoRepeat = 0x4000;

    private const uint VkOemPeriod = 0xBE; // VK_OEM_PERIOD

    private const string WindowClassName = "BiSpellHotkeyHiddenWindow";

    private static readonly (uint Mods, string Display) Primary =
        (ModControl | ModAlt | ModNoRepeat, "Ctrl+Alt+.");

    private static readonly (uint Mods, string Display) Fallback =
        (ModWin | ModShift | ModNoRepeat, "Win+Shift+.");

    private readonly IntPtr _hwnd;
    private readonly WndProc _wndProc; // keep alive for native callback
    private bool _registered;
    private bool _disposed;
    private string? _activeBindingDisplay;

    /// <summary>Raised when the registered global hotkey is pressed (WM_HOTKEY).</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Human-readable active binding (e.g. <c>Ctrl+Alt+.</c> or <c>Win+Shift+.</c>),
    /// or <c>null</c> if not registered.
    /// </summary>
    public string? ActiveBindingDisplay => _registered ? _activeBindingDisplay : null;

    /// <summary>True after a successful <see cref="TryRegister"/> until <see cref="Unregister"/> / <see cref="Dispose"/>.</summary>
    public bool IsRegistered => _registered;

    public GlobalHotkeyService()
    {
        _wndProc = WindowProc;
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClassName,
        };
        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            // 1410 = ERROR_CLASS_ALREADY_EXISTS — OK if re-created in same process.
            if (err != 1410)
            {
                global::BiSpell.CrashLog.Write($"GlobalHotkeyService: RegisterClassEx failed err={err}");
            }
        }

        _hwnd = CreateWindowEx(
            0, WindowClassName, "BiSpellHotkey",
            0, 0, 0, 0, 0,
            new IntPtr(-3), // HWND_MESSAGE
            IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"CreateWindowEx for hotkey message window failed (err={err}).");
        }
    }

    /// <summary>
    /// Attempt primary then fallback hotkey registration.
    /// Returns <c>false</c> in smoke mode without calling RegisterHotKey;
    /// returns <c>false</c> if both combos fail (no throw).
    /// Idempotent: if already registered, returns <c>true</c>.
    /// </summary>
    public bool TryRegister()
    {
        if (_disposed)
            return false;

        if (global::BiSpell.CrashLog.IsSmokeMode)
        {
            global::BiSpell.CrashLog.Write("hotkey skipped (BISPELL_SMOKE)");
            return false;
        }

        if (_registered)
            return true;

        if (_hwnd == IntPtr.Zero)
            return false;

        // Ensure clean slate (never leave two registrations for same action id).
        UnregisterHotKey(_hwnd, HotkeyIdClipboardUtility);

        if (TryRegisterCombo(Primary.Mods, Primary.Display))
            return true;

        // Primary failed — explicitly unregister before fallback (same id).
        UnregisterHotKey(_hwnd, HotkeyIdClipboardUtility);

        if (TryRegisterCombo(Fallback.Mods, Fallback.Display))
            return true;

        UnregisterHotKey(_hwnd, HotkeyIdClipboardUtility);
        _registered = false;
        _activeBindingDisplay = null;
        global::BiSpell.CrashLog.Write(
            "GlobalHotkeyService: RegisterHotKey failed for primary (Ctrl+Alt+.) " +
            "and fallback (Win+Shift+.); hotkey unavailable.");
        return false;
    }

    /// <summary>Unregister the hotkey if registered. Safe to call multiple times.</summary>
    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero && _registered)
        {
            UnregisterHotKey(_hwnd, HotkeyIdClipboardUtility);
            global::BiSpell.CrashLog.Write(
                $"GlobalHotkeyService: unregistered ({_activeBindingDisplay ?? "?"})");
        }

        _registered = false;
        _activeBindingDisplay = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Unregister();
        }
        catch (Exception ex)
        {
            global::BiSpell.CrashLog.Write($"GlobalHotkeyService.Dispose unregister: {ex.Message}");
        }

        if (_hwnd != IntPtr.Zero)
        {
            try { DestroyWindow(_hwnd); }
            catch { /* ignore */ }
        }

        GC.SuppressFinalize(this);
    }

    private bool TryRegisterCombo(uint mods, string display)
    {
        if (!RegisterHotKey(_hwnd, HotkeyIdClipboardUtility, mods, VkOemPeriod))
        {
            int err = Marshal.GetLastWin32Error();
            global::BiSpell.CrashLog.Write(
                $"GlobalHotkeyService: RegisterHotKey({display}) failed err={err}");
            return false;
        }

        _registered = true;
        _activeBindingDisplay = display;
        global::BiSpell.CrashLog.Write($"GlobalHotkeyService: registered {display} id={HotkeyIdClipboardUtility}");
        return true;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyIdClipboardUtility)
            {
                try
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    // Never let handler exceptions escape native WndProc.
                    global::BiSpell.CrashLog.Write($"GlobalHotkeyService.HotkeyPressed handler: {ex}");
                }
            }
            return IntPtr.Zero;
        }

        if (msg == WmDestroy)
            return IntPtr.Zero;

        return DefWindowProc(hWnd, msg, wParam, lParam);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
