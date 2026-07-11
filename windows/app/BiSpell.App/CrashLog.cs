using System.Runtime.InteropServices;
using System.Text;

namespace BiSpell;

/// <summary>Write startup failures to %TEMP%\BiSpell-startup.log and a MessageBox.</summary>
internal static class CrashLog
{
    public static string LogPath { get; } = Path.Combine(
        Path.GetTempPath(), "BiSpell-startup.log");

    public static void Write(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    public static void Write(Exception ex)
    {
        Write(ex.ToString());
    }

    public static void MessageBox(string title, string body)
    {
        try
        {
            // Cap size so MessageBox stays usable.
            if (body.Length > 1500)
                body = body[..1500] + "\n…\n(see " + LogPath + ")";
            else
                body = body + "\n\nLog: " + LogPath;
            MessageBoxW(IntPtr.Zero, body, title, 0x00000010); // MB_ICONERROR
        }
        catch { /* ignore */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
