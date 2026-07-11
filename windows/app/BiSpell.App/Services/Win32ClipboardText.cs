using System.Runtime.InteropServices;
using System.Text;

namespace BiSpell.Services;

/// <summary>
/// Win32 clipboard facade for UTF-16 text only (<c>CF_UNICODETEXT</c>).
/// Synchronous OpenClipboard / GetClipboardData / SetClipboardData with short retries
/// when the clipboard is busy. Never throws to callers (soft-fail → null / false).
/// </summary>
/// <remarks>
/// <para>
/// Threading: OpenClipboard is process-global; this type serializes access via a private
/// gate. Call from any thread; hotkey GLUE typically invokes from the UI dispatcher.
/// </para>
/// <para>
/// Ownership: after a successful <c>SetClipboardData</c>, the system owns the HGLOBAL —
/// do not free it. On failure before transfer, the HGLOBAL is freed here.
/// </para>
/// <para>
/// No WinRT <c>Windows.ApplicationModel.DataTransfer.Clipboard</c> dependency (mandate B).
/// No App / MainWindow coupling — pure service for P2-GLUE to consume.
/// </para>
/// </remarks>
public sealed class Win32ClipboardText
{
    private const uint CfUnicodeText = 13; // CF_UNICODETEXT
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    /// <summary>GMEM_MOVEABLE | GMEM_ZEROINIT — standard clipboard alloc flags.</summary>
    private const uint Ghnd = GmemMoveable | GmemZeroInit;

    /// <summary>OpenClipboard attempts when another process holds the clipboard.</summary>
    public const int DefaultMaxOpenAttempts = 8;

    /// <summary>Delay between OpenClipboard retries (milliseconds).</summary>
    public const int DefaultRetryDelayMs = 15;

    private static readonly object ClipboardGate = new();

    private readonly int _maxOpenAttempts;
    private readonly int _retryDelayMs;

    public Win32ClipboardText(
        int maxOpenAttempts = DefaultMaxOpenAttempts,
        int retryDelayMs = DefaultRetryDelayMs)
    {
        _maxOpenAttempts = Math.Clamp(maxOpenAttempts, 1, 50);
        _retryDelayMs = Math.Clamp(retryDelayMs, 0, 200);
    }

    /// <summary>
    /// Read clipboard Unicode text. Returns <c>null</c> when empty, non-text,
    /// unavailable, or OpenClipboard fails after retries.
    /// </summary>
    public string? TryGetText()
    {
        try
        {
            lock (ClipboardGate)
            {
                if (!TryOpenClipboardWithRetry())
                {
                    LogSoft("TryGetText: OpenClipboard failed after retries");
                    return null;
                }

                try
                {
                    if (!IsClipboardFormatAvailable(CfUnicodeText))
                        return null;

                    IntPtr hData = GetClipboardData(CfUnicodeText);
                    if (hData == IntPtr.Zero)
                        return null;

                    IntPtr p = GlobalLock(hData);
                    if (p == IntPtr.Zero)
                        return null;

                    try
                    {
                        string? text = Marshal.PtrToStringUni(p);
                        if (string.IsNullOrEmpty(text))
                            return null;
                        return text;
                    }
                    finally
                    {
                        GlobalUnlock(hData);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
        }
        catch (Exception ex)
        {
            LogSoft($"TryGetText: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Write Unicode text to the clipboard (replaces current content).
    /// Returns <c>false</c> on failure; never throws.
    /// </summary>
    public bool TrySetText(string text)
    {
        if (text is null)
            return false;

        try
        {
            lock (ClipboardGate)
            {
                if (!TryOpenClipboardWithRetry())
                {
                    LogSoft("TrySetText: OpenClipboard failed after retries");
                    return false;
                }

                IntPtr hGlobal = IntPtr.Zero;
                try
                {
                    if (!EmptyClipboard())
                    {
                        LogSoft("TrySetText: EmptyClipboard failed");
                        return false;
                    }

                    // (char count + NUL) * sizeof(char) as UTF-16LE.
                    int charCount = text.Length + 1;
                    nuint byteCount = (nuint)charCount * sizeof(char);
                    hGlobal = GlobalAlloc(Ghnd, byteCount);
                    if (hGlobal == IntPtr.Zero)
                    {
                        LogSoft("TrySetText: GlobalAlloc failed");
                        return false;
                    }

                    IntPtr p = GlobalLock(hGlobal);
                    if (p == IntPtr.Zero)
                    {
                        LogSoft("TrySetText: GlobalLock failed");
                        GlobalFree(hGlobal);
                        hGlobal = IntPtr.Zero;
                        return false;
                    }

                    try
                    {
                        // Copy UTF-16 code units + terminating NUL.
                        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                        Marshal.Copy(bytes, 0, p, bytes.Length);
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }

                    IntPtr result = SetClipboardData(CfUnicodeText, hGlobal);
                    if (result == IntPtr.Zero)
                    {
                        LogSoft("TrySetText: SetClipboardData failed");
                        GlobalFree(hGlobal);
                        hGlobal = IntPtr.Zero;
                        return false;
                    }

                    // System now owns the memory.
                    hGlobal = IntPtr.Zero;
                    return true;
                }
                finally
                {
                    // If we still own hGlobal (failure path already frees; success zeros it).
                    if (hGlobal != IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                        hGlobal = IntPtr.Zero;
                    }
                    CloseClipboard();
                }
            }
        }
        catch (Exception ex)
        {
            LogSoft($"TrySetText: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Async wrapper over <see cref="TryGetText"/> (Win32 is synchronous).</summary>
    public Task<string?> TryGetTextAsync() => Task.FromResult(TryGetText());

    /// <summary>Async wrapper over <see cref="TrySetText"/> (Win32 is synchronous).</summary>
    public Task<bool> TrySetTextAsync(string text) => Task.FromResult(TrySetText(text));

    private bool TryOpenClipboardWithRetry()
    {
        for (int attempt = 0; attempt < _maxOpenAttempts; attempt++)
        {
            // hwndOwner = NULL: current task becomes the owner (no window required).
            if (OpenClipboard(IntPtr.Zero))
                return true;

            if (attempt + 1 < _maxOpenAttempts && _retryDelayMs > 0)
                Thread.Sleep(_retryDelayMs);
        }
        return false;
    }

    private static void LogSoft(string message)
    {
        try
        {
            // Optional: CrashLog is internal in root namespace — only if available via reflection-free call.
            // Prefer compile-time link: CrashLog is internal static in BiSpell.
            CrashLog.Write("clipboard: " + message);
        }
        catch
        {
            // Never throw from soft-fail paths.
        }
    }

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    #endregion
}
