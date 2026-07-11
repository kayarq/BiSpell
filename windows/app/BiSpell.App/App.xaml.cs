using BiSpell.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace BiSpell;

/// <summary>
/// WinUI 3 application entry — thin shell over bispell_core (P/Invoke).
/// Owns tray icon lifecycle: Show window / Quit. Closing the main window hides to tray
/// unless the user chose Quit (or there is no tray).
/// Still no system-wide other-app injection.
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
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            CrashLog.MessageBox("BiSpell XAML init failed", ex.Message);
            throw;
        }

        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UnhandledException: " + e.Exception);
            // Keep false so process can terminate visibly; still log to disk.
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
            _window = new MainWindow();
            CrashLog.Write("MainWindow created");
            _window.Activate();
            CrashLog.Write("MainWindow.Activate done");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            CrashLog.MessageBox("BiSpell window failed", ex.Message);
            throw;
        }

        try
        {
            _tray = new TrayIconService();
            _tray.ShowWindowRequested += (_, _) => ShowMainWindow();
            _tray.QuitRequested += (_, _) => Quit();
        }
        catch (Exception ex)
        {
            // Tray is nice-to-have; app still runs without it.
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

    /// <summary>Clean shutdown: save settings, dispose tray, close window, exit process.</summary>
    public void Quit()
    {
        if (_isQuitting) return;
        _isQuitting = true;

        try { _window?.PersistSettings(); } catch { /* ignore */ }

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
        Environment.Exit(0);
    }
}
