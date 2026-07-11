using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace BiSpell;

/// <summary>
/// WinUI 3 application entry — thin shell over bispell_core (P/Invoke).
/// Editor-only Notes + spell product (no global hotkey / UIA / out-of-app clipboard).
/// No system tray: closing the main window always fully quits (persist, dispose
/// window resources via Closed, then Environment.Exit) so no orphan process remains.
/// Quieter Defender / SmartScreen story for an unsigned Notes app.
///
/// Startup failure path (W2 Mandate B): log type+message+stack, write
/// %TEMP%\BiSpell-startup.status fail:…, MessageBox only when not smoke, Environment.Exit(1).
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
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

    /// <summary>Shared app instance for window / quit coordination.</summary>
    public static new App Current => (App)Application.Current;

    public MainWindow? MainWindow => _window;

    /// <summary>True while <see cref="Quit"/> is tearing down (re-entrancy gate).</summary>
    public bool IsQuitting => _isQuitting;

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

        // Tray intentionally not created (v0.2.1 Mandate B): close = full process exit.
        // No Shell_NotifyIcon / message-only HWND — quieter unsigned-app Defender story.

        // Intercept close: always full Quit (never hide-to-tray).
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
        // Already in Quit → allow the real close so Closed dispose + Exit can finish.
        if (_isQuitting)
            return;

        // Cancel the default close and run ordered teardown (persist → Close → Exit).
        // Never Hide() / minimize-to-tray.
        args.Cancel = true;
        Quit();
    }

    /// <summary>
    /// Clean shutdown: save settings + note, unhook Closing, close window (Closed
    /// disposes debouncer / popup / engine), then Environment.Exit(0).
    /// Single intentional full-exit API (besides startup Exit(1)).
    /// </summary>
    public void Quit()
    {
        if (_isQuitting) return;
        _isQuitting = true;

        try { CrashLog.Write("Quit"); } catch { /* ignore */ }

        try { _window?.PersistSettings(); } catch { /* ignore */ }
        try { _window?.PersistActiveNote(); } catch { /* ignore */ }

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
