using System.Drawing;
using System.Windows.Forms;

namespace BiSpell.Services;

/// <summary>
/// Notification-area (system tray) icon for unpackaged WinUI 3.
/// Uses WinForms <see cref="NotifyIcon"/> — WinUI has no first-party tray control.
/// Menu: Show window, Quit. No system-wide other-app injection.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? QuitRequested;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show BiSpell", null, (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Text = "BiSpell",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = CreateIcon(),
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowBalloon(string title, string text, int timeoutMs = 2500)
    {
        if (_disposed) return;
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(timeoutMs);
        }
        catch
        {
            // Balloon tips are optional UX.
        }
    }

    private static Icon CreateIcon()
    {
        // Prefer an app icon next to the executable if present; else system application icon.
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var name in new[] { "BiSpell.ico", "app.ico", "Assets\\BiSpell.ico" })
            {
                var path = Path.Combine(baseDir, name);
                if (File.Exists(path))
                    return new Icon(path);
            }
        }
        catch { /* fall through */ }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }
}
