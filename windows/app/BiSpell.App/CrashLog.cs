using System.Runtime.InteropServices;
using System.Text;

namespace BiSpell;

/// <summary>
/// Startup diagnostics for interactive users and headless smoke (GHA).
/// <list type="bullet">
/// <item><description>Log: %TEMP%\BiSpell-startup.log (stable path — do not rename; W3 + Run-BiSpell.cmd depend on it).</description></item>
/// <item><description>Status: %TEMP%\BiSpell-startup.status one-line <c>ok</c> / <c>fail:…</c> for smoke without full-log parse.</description></item>
/// <item><description>Exit codes (process): 0 = clean loop end / Quit; 1 = fatal startup failure.</description></item>
/// <item><description>When BISPELL_SMOKE=1, MessageBox is suppressed so CI never hangs on a modal.</description></item>
/// </list>
/// BeginSession truncates the log at process start so smoke does not match stale prior-run errors.
/// </summary>
internal static class CrashLog
{
    public const int ExitOk = 0;
    public const int ExitStartupFailure = 1;

    public static string LogPath { get; } = Path.Combine(
        Path.GetTempPath(), "BiSpell-startup.log");

    public static string StatusPath { get; } = Path.Combine(
        Path.GetTempPath(), "BiSpell-startup.status");

    /// <summary>True when BISPELL_SMOKE=1 (GHA / headless smoke). Skips MessageBox.</summary>
    public static bool IsSmokeMode
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("BISPELL_SMOKE");
            return string.Equals(v, "1", StringComparison.Ordinal)
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Truncate log and write a session header. Call once at process entry (Program.Main).
    /// Prevents smoke from matching fatal strings left by a previous run (B4).
    /// </summary>
    public static void BeginSession()
    {
        try
        {
            var header = new StringBuilder();
            header.AppendLine("=== BiSpell startup session ===");
            header.AppendLine($"time={DateTime.Now:O}");
            header.AppendLine($"pid={Environment.ProcessId}");
            header.AppendLine($"base={AppContext.BaseDirectory}");
            header.AppendLine($"cwd={Environment.CurrentDirectory}");
            header.AppendLine($"smoke={(IsSmokeMode ? "1" : "0")}");
            header.AppendLine($"log={LogPath}");
            header.AppendLine($"status={StatusPath}");
            header.AppendLine("---");
            File.WriteAllText(LogPath, header.ToString(), Encoding.UTF8);
        }
        catch { /* ignore */ }

        try
        {
            // Clear stale status so smoke does not read a previous ok/fail.
            if (File.Exists(StatusPath))
                File.Delete(StatusPath);
        }
        catch { /* ignore */ }
    }

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
        // Full ToString keeps type, message, stack, and inner exceptions searchable
        // (ToggleButton.IsChecked, Failed to assign to property, DllNotFoundException, …).
        Write(ex.ToString());
    }

    /// <summary>
    /// Structured fatal log: exception type + message + stack + full ToString(),
    /// then write status <c>fail:…</c>. Caller should Environment.Exit(ExitStartupFailure).
    /// </summary>
    public static void WriteFatal(Exception ex, string context)
    {
        try
        {
            Write($"FATAL startup [{context}] type={ex.GetType().FullName}");
            Write($"FATAL message: {ex.Message}");
            Write($"FATAL stack: {ex.StackTrace ?? "(no stack)"}");
            if (ex.InnerException is not null)
            {
                Write($"FATAL inner type={ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
            }
            Write(ex); // full ToString for searchable known-bad strings
        }
        catch { /* ignore */ }

        WriteStatusFail(ex, context);
    }

    public static void WriteStatusOk()
    {
        WriteStatus("ok");
        Write("startup.status=ok");
    }

    public static void WriteStatusFail(Exception ex, string context)
    {
        // One line, no newlines — smoke can read with Get-Content -Raw easily.
        var msg = ex.Message?.Replace('\r', ' ').Replace('\n', ' ') ?? "unknown";
        if (msg.Length > 200)
            msg = msg[..200] + "…";
        var type = ex.GetType().Name;
        WriteStatus($"fail:{type}:{context}:{msg}");
    }

    public static void WriteStatus(string oneLine)
    {
        try
        {
            File.WriteAllText(StatusPath, oneLine + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Interactive error dialog. No-op when BISPELL_SMOKE=1 so headless CI never blocks.
    /// </summary>
    public static void MessageBox(string title, string body)
    {
        if (IsSmokeMode)
        {
            Write($"MessageBox suppressed (BISPELL_SMOKE): {title}: {body}");
            return;
        }

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
