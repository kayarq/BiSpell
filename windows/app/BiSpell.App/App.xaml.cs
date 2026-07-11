using BiSpell.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace BiSpell;

/// <summary>
/// WinUI 3 application entry — thin shell over bispell_core (P/Invoke).
/// Owns tray icon + global clipboard-utility hotkey lifecycle. Closing the main
/// window hides to tray unless the user chose Quit (or there is no tray).
/// Still no system-wide other-app injection.
///
/// Startup failure path (W2 Mandate B): log type+message+stack, write
/// %TEMP%\BiSpell-startup.status fail:…, MessageBox only when not smoke, Environment.Exit(1).
///
/// Phase 2/3 (P2/P3-GLUE): when <see cref="AppUserSettings.GlobalHotkeyEnabled"/> and not
/// smoke, registers <see cref="GlobalHotkeyService"/>; HotkeyPressed → UI dispatcher →
/// <see cref="MainWindow.HandleUtilityHotkey"/> (UIA-first + clipboard orchestrator).
/// Settings toggle re-registers without restart. Smoke (<c>BISPELL_SMOKE=1</c>) never registers.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private TrayIconService? _tray;
    private GlobalHotkeyService? _hotkey;
    private bool _isQuitting;

    public App()
    {
        try
        {
            CrashLog.Write("App ctor: InitializeComponent");
            InitializeComponent();
            CrashLog.Write("App ctor: InitializeComponent done");
        }
        catch (Exception ex)
        {
            CrashLog.WriteFatal(ex, "App.InitializeComponent");
            CrashLog.MessageBox("BiSpell XAML init failed", ex.Message);
            throw;
        }

        UnhandledException += (_, e) =>
        {
            // Keep Handled=false so the process can terminate with a visible failure;
            // still log full exception for smoke string search.
            try
            {
                if (e.Exception is not null)
                    CrashLog.WriteFatal(e.Exception, "UnhandledException");
                else
                    CrashLog.Write("UnhandledException: (null)");
            }
            catch { /* ignore */ }

            e.Handled = false;

            try
            {
                CrashLog.MessageBox("BiSpell error", e.Exception?.Message ?? "unknown");
            }
            catch { /* ignore */ }
        };
    }

    /// <summary>Shared app instance for window/tray coordination.</summary>
    public static new App Current => (App)Application.Current;

    public MainWindow? MainWindow => _window;

    /// <summary>Active hotkey binding display (e.g. Ctrl+Alt+.), or null if unregistered.</summary>
    public string? ActiveHotkeyBinding => _hotkey?.ActiveBindingDisplay;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CrashLog.Write("OnLaunched");
        try
        {
            CrashLog.Write("MainWindow creating");
            _window = new MainWindow();
            CrashLog.Write("MainWindow created");
            _window.Activate();
            CrashLog.Write("MainWindow.Activate done");
            // Sentinel for smoke: one-line ok without parsing the full log.
            CrashLog.WriteStatusOk();
        }
        catch (Exception ex)
        {
            // B2: log type + message + stack before process exit; searchable known-bad strings.
            CrashLog.WriteFatal(ex, "MainWindow construction/activate");
            CrashLog.MessageBox("BiSpell window failed", ex.Message);
            // B5/exit-code: do not rethrow into an ambiguous WinUI path — exit 1 now.
            Environment.Exit(CrashLog.ExitStartupFailure);
            return;
        }

        try
        {
            _tray = new TrayIconService();
            _tray.ShowWindowRequested += (_, _) => ShowMainWindow();
            _tray.QuitRequested += (_, _) => Quit();
        }
        catch (Exception ex)
        {
            // Tray is nice-to-have; app still runs without it. Log but do not fail startup.
            CrashLog.Write("Tray init failed (non-fatal): " + ex);
            System.Diagnostics.Debug.WriteLine($"Tray init failed: {ex.Message}");
            _tray = null;
        }

        // P2-GLUE: global hotkey after MainWindow + tray (non-fatal; smoke never registers).
        try
        {
            InitGlobalHotkey();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Hotkey init failed (non-fatal): " + ex);
            System.Diagnostics.Debug.WriteLine($"Hotkey init failed: {ex.Message}");
            DisposeHotkeyService();
        }

        // Intercept close: hide to tray instead of exit (when tray is available).
        try
        {
            _window.AppWindow.Closing += OnMainWindowClosing;
        }
        catch (Exception ex)
        {
            CrashLog.Write("AppWindow.Closing hook failed (non-fatal): " + ex.Message);
            System.Diagnostics.Debug.WriteLine($"AppWindow.Closing hook failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Create hotkey service and register when settings allow.
    /// Smoke: log and skip RegisterHotKey entirely (defense-in-depth with service).
    /// </summary>
    private void InitGlobalHotkey()
    {
        if (CrashLog.IsSmokeMode)
        {
            CrashLog.Write("hotkey skipped (BISPELL_SMOKE)");
            _window?.UpdateHotkeyBindingCaption(null, registered: false, smoke: true);
            return;
        }

        _hotkey = new GlobalHotkeyService();
        _hotkey.HotkeyPressed += OnGlobalHotkeyPressed;
        SyncGlobalHotkeyFromSettings();
    }

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e)
    {
        // Marshal to WinUI dispatcher: engine + editor + clipboard GLUE run on UI thread.
        try
        {
            DispatcherQueue? dq = _window?.DispatcherQueue;
            if (dq is null)
            {
                CrashLog.Write("hotkey pressed but MainWindow dispatcher unavailable");
                return;
            }

            bool enqueued = dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try
                {
                    _window?.HandleUtilityHotkey();
                }
                catch (Exception ex)
                {
                    CrashLog.Write("HandleUtilityHotkey: " + ex);
                }
            });

            if (!enqueued)
                CrashLog.Write("hotkey: DispatcherQueue.TryEnqueue returned false");
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnGlobalHotkeyPressed: " + ex.Message);
        }
    }

    /// <summary>
    /// Register or unregister the global hotkey from current MainWindow settings.
    /// Called at launch and when the Global hotkey checkbox toggles (no restart).
    /// No-op in smoke mode.
    /// </summary>
    public void SyncGlobalHotkeyFromSettings()
    {
        if (CrashLog.IsSmokeMode)
        {
            CrashLog.Write("hotkey skipped (BISPELL_SMOKE)");
            _window?.UpdateHotkeyBindingCaption(null, registered: false, smoke: true);
            return;
        }

        bool wantEnabled = _window?.IsGlobalHotkeyEnabled ?? true;

        try
        {
            if (_hotkey is null)
            {
                if (!wantEnabled)
                {
                    _window?.UpdateHotkeyBindingCaption(null, registered: false, smoke: false);
                    return;
                }

                _hotkey = new GlobalHotkeyService();
                _hotkey.HotkeyPressed += OnGlobalHotkeyPressed;
            }

            if (wantEnabled)
            {
                bool ok = _hotkey.TryRegister();
                string? binding = _hotkey.ActiveBindingDisplay;
                if (ok)
                    CrashLog.Write($"hotkey active: {binding ?? "?"}");
                else
                    CrashLog.Write("hotkey unavailable (register failed)");
                _window?.UpdateHotkeyBindingCaption(binding, registered: ok, smoke: false);
            }
            else
            {
                _hotkey.Unregister();
                CrashLog.Write("hotkey disabled by settings");
                _window?.UpdateHotkeyBindingCaption(null, registered: false, smoke: false);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("SyncGlobalHotkeyFromSettings: " + ex);
            _window?.UpdateHotkeyBindingCaption(null, registered: false, smoke: false);
        }
    }

    /// <summary>Tray balloon (or tip fallback). Never throws.</summary>
    public void ShowTrayBalloon(string title, string text, int timeoutMs = 2500)
    {
        try
        {
            _tray?.ShowBalloon(title, text, timeoutMs);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ShowTrayBalloon: " + ex.Message);
        }
    }

    private void DisposeHotkeyService()
    {
        if (_hotkey is null) return;
        try
        {
            _hotkey.HotkeyPressed -= OnGlobalHotkeyPressed;
        }
        catch { /* ignore */ }
        try
        {
            _hotkey.Dispose();
        }
        catch (Exception ex)
        {
            CrashLog.Write("hotkey dispose: " + ex.Message);
        }
        _hotkey = null;
    }

    private void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isQuitting || _tray is null)
        {
            // Allow real close / process exit path.
            return;
        }

        // Hide to notification area instead of quitting.
        args.Cancel = true;
        try
        {
            // Persist settings even when only hiding.
            _window?.PersistSettings();
            sender.Hide();
        }
        catch
        {
            // If hide fails, force quit on next close.
            _isQuitting = true;
        }
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow.Show();
            _window.Activate();
        }
        catch
        {
            try
            {
                _window.AppWindow.Show();
                _window.Activate();
            }
            catch
            {
                try { _window.Activate(); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Clean shutdown: save settings, dispose hotkey + tray, close window, exit process 0.
    /// Hotkey is unregistered before exit so other apps can claim the combo.
    /// </summary>
    public void Quit()
    {
        if (_isQuitting) return;
        _isQuitting = true;

        try { _window?.PersistSettings(); } catch { /* ignore */ }

        // Unregister hotkey before tearing down UI so WM_HOTKEY cannot re-enter mid-quit.
        DisposeHotkeyService();

        try { _tray?.Dispose(); } catch { /* ignore */ }
        _tray = null;

        try
        {
            if (_window is not null)
            {
                try { _window.AppWindow.Closing -= OnMainWindowClosing; } catch { /* ignore */ }
                _window.Close();
            }
        }
        catch { /* ignore */ }

        // Ensure process exit even if WinUI keeps the message loop alive after last window.
        Environment.Exit(CrashLog.ExitOk);
    }
}
