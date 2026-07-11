using BiSpell.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace BiSpell;

/// <summary>
/// WinUI 3 application entry — thin shell over bispell_core (P/Invoke).
/// Owns tray icon lifecycle. Closing the main window hides to tray unless the
/// user chose Quit (or there is no tray). Editor-only spell-check product —
/// no global hotkey / UIA / out-of-app clipboard utility.
///
/// Startup failure path (W2 Mandate B): log type+message+stack, write
/// %TEMP%\BiSpell-startup.status fail:…, MessageBox only when not smoke, Environment.Exit(1).
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private TrayIconService? _tray;
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
            // Persist settings + active note even when only hiding.
            _window?.PersistSettings();
            _window?.PersistActiveNote();
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
    /// Clean shutdown: save settings + note, dispose tray, close window, exit process 0.
    /// </summary>
    public void Quit()
    {
        if (_isQuitting) return;
        _isQuitting = true;

        try { _window?.PersistSettings(); } catch { /* ignore */ }
        try { _window?.PersistActiveNote(); } catch { /* ignore */ }

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
