namespace BiSpell.Services;

/// <summary>
/// Soft-fail snapshot of the currently focused UIA element (probe / read / write meta).
/// All fields are best-effort; missing data is null / default, never throws.
/// </summary>
public sealed class UiaFocusSnapshot
{
    /// <summary>UIA Name property (may be empty for some edits).</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Localized control type when available, else a short id-based label
    /// (e.g. <c>Edit</c>, <c>ControlType:50004</c>).
    /// </summary>
    public string? ControlType { get; init; }

    /// <summary>UIA ClassName when available (e.g. <c>Edit</c>, <c>RichEdit20W</c>).</summary>
    public string? ClassName { get; init; }

    /// <summary>UIA AutomationId when available.</summary>
    public string? AutomationId { get; init; }

    /// <summary>Process id of the focused element (0 if unknown).</summary>
    public int ProcessId { get; init; }

    /// <summary>Process file name when resolvable (e.g. <c>notepad</c>); null on failure.</summary>
    public string? ProcessName { get; init; }

    /// <summary>True when <see cref="ProcessId"/> equals this process (GLUE self-focus policy).</summary>
    public bool IsOwnProcess { get; init; }

    /// <summary>True when ValuePattern was obtained and CurrentValue was read (password refused → false).</summary>
    public bool CanReadValue { get; init; }

    /// <summary>True when ValuePattern exists and is not read-only (password refused → false).</summary>
    public bool CanWriteValue { get; init; }

    /// <summary>True when UIA reports IsPassword (read/write refused).</summary>
    public bool IsPassword { get; init; }

    /// <summary>Value when a read succeeded; otherwise null (never populated for password fields).</summary>
    public string? Value { get; init; }

    /// <summary>Tier estimate: A read+write, B read-only, C no access / refused / fail.</summary>
    public UiaSupportTier Tier { get; init; } = UiaSupportTier.C;

    /// <summary>Patterns observed, soft errors, and probe notes (single-line friendly).</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Compact one-line summary for status / CrashLog.</summary>
    public string ToStatusLine()
    {
        var name = Truncate(Name, 40) ?? "—";
        var ctl = ControlType ?? "—";
        var proc = ProcessName ?? (ProcessId > 0 ? $"pid={ProcessId}" : "—");
        return $"tier={Tier} name={name} control={ctl} read={(CanReadValue ? 1 : 0)} write={(CanWriteValue ? 1 : 0)} " +
               $"pwd={(IsPassword ? 1 : 0)} own={(IsOwnProcess ? 1 : 0)} proc={proc} notes={Truncate(Notes, 80) ?? ""}";
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        if (s.Length <= max)
            return s;
        return s[..max] + "…";
    }
}
