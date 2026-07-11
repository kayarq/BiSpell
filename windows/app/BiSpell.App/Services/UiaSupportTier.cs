namespace BiSpell.Services;

/// <summary>
/// Mac-parity support tier for focused-control UIA access (logs use ToString → "A"/"B"/"C").
/// <list type="bullet">
/// <item><description><see cref="A"/> — ValuePattern read + write (not read-only).</description></item>
/// <item><description><see cref="B"/> — ValuePattern read only (or write failed / read-only).</description></item>
/// <item><description><see cref="C"/> — no access, password refused, no focus, or COM failure.</description></item>
/// </list>
/// </summary>
public enum UiaSupportTier
{
    /// <summary>ValuePattern read + write available.</summary>
    A = 0,

    /// <summary>ValuePattern read-only / partial.</summary>
    B = 1,

    /// <summary>No usable ValuePattern access (clipboard fallback).</summary>
    C = 2,
}
