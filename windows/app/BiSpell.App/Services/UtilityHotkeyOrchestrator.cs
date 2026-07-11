using BiSpell.Interop;
using BiSpell.Models;
using BiSpell.Utilities;

namespace BiSpell.Services;

/// <summary>
/// Where utility text was acquired for a hotkey run (feedback / status labels).
/// </summary>
public enum UtilityTextSource
{
    /// <summary>No text acquired yet.</summary>
    None = 0,

    /// <summary>Focused-control ValuePattern (UIA).</summary>
    Uia = 1,

    /// <summary>Win32 CF_UNICODETEXT clipboard (Phase 2 path).</summary>
    Clipboard = 2,

    /// <summary>Main editor text (self-process focus policy).</summary>
    Editor = 3,
}

/// <summary>
/// Outcome of the UIA-first branch before spell/write.
/// </summary>
public enum UiaPathResult
{
    /// <summary>UIA (or self-editor) provided text to spell-check; do not use clipboard for acquire.</summary>
    Success = 0,

    /// <summary>UIA unusable / empty / off / smoke — fall through to clipboard acquire.</summary>
    FallbackClipboard = 1,

    /// <summary>Hard stop with user feedback already emitted (e.g. secure field only — rare).</summary>
    Aborted = 2,
}

/// <summary>
/// UI / engine surface the orchestrator needs without owning WinUI controls.
/// Implemented by <c>MainWindow</c>; keeps App/MainWindow thin (P3-GLUE mandate B).
/// </summary>
public interface IUtilityHotkeyHost
{
    /// <summary>Flush checkboxes into the settings model (best-effort).</summary>
    void SaveSettingsFromUi();

    /// <summary>Ensure engine exists (TryInitEngine). Returns false when still unavailable.</summary>
    bool EnsureEngineReady();

    /// <summary>Live engine instance after <see cref="EnsureEngineReady"/>; null if failed.</summary>
    BispellEngine? Engine { get; }

    /// <summary>Spell-check master enable from settings.</summary>
    bool IsSpellEnabled { get; }

    /// <summary>Try UIA before clipboard (shell <c>uiaAssistEnabled</c>).</summary>
    bool IsUiaAssistEnabled { get; }

    /// <summary>Allow SetValue / clipboard write of fixed text (shell <c>clipboardReplaceEnabled</c>).</summary>
    bool IsClipboardReplaceEnabled { get; }

    /// <summary>Current main editor text (self-focus policy); null/empty → not used.</summary>
    string? GetEditorText();

    /// <summary>Push current settings into the engine before Check.</summary>
    void ApplySettingsToEngine();

    /// <summary>
    /// Put text + misspellings into the shell editor; optionally show the main window
    /// (must run only after UIA write attempts so focus is not stolen mid-path).
    /// </summary>
    void RefreshEditor(string text, IReadOnlyList<MisspellingItem> misses, bool showWindow);

    /// <summary>
    /// Status line + tray balloon. <paramref name="pathLabel"/> is e.g. <c>UIA</c>,
    /// <c>clipboard</c>, <c>editor</c> for user-visible path distinction.
    /// </summary>
    void NotifyFeedback(string pathLabel, string body, int misspellingCount);

    /// <summary>Best-effort re-check after a successful write so the list matches fixed text.</summary>
    void RunEditorRecheck();
}

/// <summary>
/// P3-GLUE mandate B: UIA-first then clipboard utility orchestrator.
/// Owns re-entrancy, acquire → check → <see cref="ClipboardSpellFix"/> → commit,
/// and path-specific write policy. Call only from the UI / STA dispatcher thread.
/// </summary>
/// <remarks>
/// <para>
/// Product order (locked):
/// <list type="number">
/// <item>Busy guard.</item>
/// <item>Settings flush + engine ready + spell enabled.</item>
/// <item>If UIA assist on (and not smoke): try focused ValuePattern read; password refused;
/// self-process → editor text if non-empty else clipboard fallthrough (no foreign SetValue).</item>
/// <item>Else / on UIA fail: Phase 2 clipboard read.</item>
/// <item>Engine Check + pure <see cref="ClipboardSpellFix.ApplyTopSuggestions"/>.</item>
/// <item>Commit: UIA SetValue when writable + replace on; else Tier B clipboard write;
/// clipboard path uses Phase 2 write policy; replace off → review only (no write).</item>
/// <item>Show window only after write attempts (focus order).</item>
/// </list>
/// </para>
/// <para>
/// No second engine instance. Does not rewrite <see cref="Win32ClipboardText"/>,
/// <see cref="ClipboardSpellFix"/>, or <see cref="GlobalHotkeyService"/>.
/// Smoke: <see cref="UiaTextAccess"/> skips COM; clipboard subroutine remains available.
/// </para>
/// </remarks>
public sealed class UtilityHotkeyOrchestrator
{
    private readonly IUtilityHotkeyHost _host;
    private readonly Win32ClipboardText _clipboard;
    private readonly UiaTextAccess _uia;
    private bool _busy;

    public UtilityHotkeyOrchestrator(
        IUtilityHotkeyHost host,
        Win32ClipboardText? clipboard = null,
        UiaTextAccess? uia = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _clipboard = clipboard ?? new Win32ClipboardText();
        _uia = uia ?? new UiaTextAccess();
    }

    /// <summary>True while a utility run is in progress (re-entrancy guard).</summary>
    public bool IsBusy => _busy;

    /// <summary>
    /// Public entry for the global utility hotkey (UI dispatcher).
    /// Never throws to the caller.
    /// </summary>
    public void HandleUtilityHotkey()
    {
        if (_busy)
        {
            CrashLog.Write("utility hotkey: ignored re-entrant hotkey");
            return;
        }

        _busy = true;
        try
        {
            RunCore();
        }
        catch (Exception ex)
        {
            CrashLog.Write("HandleUtilityHotkey: " + ex);
            try { _host.NotifyFeedback("utility", "Utility failed", 0); } catch { /* ignore */ }
        }
        finally
        {
            _busy = false;
        }
    }

    private void RunCore()
    {
        try { _host.SaveSettingsFromUi(); } catch { /* keep last host settings */ }

        if (!_host.EnsureEngineReady() || _host.Engine is null)
        {
            _host.NotifyFeedback("utility", "Engine not loaded", 0);
            return;
        }

        if (!_host.IsSpellEnabled)
        {
            _host.NotifyFeedback("utility", "Spell-check disabled", 0);
            return;
        }

        UtilityTextSource source = UtilityTextSource.None;
        string? text = null;
        UiaFocusSnapshot? uiaMeta = null;
        bool uiaWritable = false;
        UiaSupportTier tier = UiaSupportTier.C;

        // --- Acquire: UIA-first (when enabled), else clipboard ---
        if (_host.IsUiaAssistEnabled && !CrashLog.IsSmokeMode)
        {
            UiaPathResult uiaResult = TryAcquireUiaOrSelf(out text, out uiaMeta, out uiaWritable, out source, out tier);
            if (uiaResult == UiaPathResult.Aborted)
                return;
            // Success leaves text/source set; FallbackClipboard leaves text null.
        }

        if (text is null)
        {
            // Phase 2 clipboard subroutine (UIA off, smoke, fail, empty, self empty).
            RunClipboardUtilityPath(preloadedText: null);
            return;
        }

        // Have text from UIA or self-editor — spell + commit with path policy.
        ProcessAcquiredText(text, source, uiaMeta, uiaWritable, tier);
    }

    /// <summary>
    /// UIA acquire branch: password skip, self-process → editor, else ValuePattern read.
    /// </summary>
    private UiaPathResult TryAcquireUiaOrSelf(
        out string? text,
        out UiaFocusSnapshot? meta,
        out bool writable,
        out UtilityTextSource source,
        out UiaSupportTier tier)
    {
        text = null;
        meta = null;
        writable = false;
        source = UtilityTextSource.None;
        tier = UiaSupportTier.C;

        try
        {
            // Probe-first when possible so we can detect password / self without partial read policy issues.
            // TryReadFocusedValue already refuses password and returns meta.
            bool readOk = _uia.TryReadFocusedValue(out string? value, out meta);

            if (meta is not null && meta.IsPassword)
            {
                // Privacy: do not use UIA value; soft status then Tier C clipboard fallback.
                CrashLog.Write("utility: secure field skipped (UIA password)");
                // Fall through to clipboard — do not abort entirely (clipboard may hold unrelated text).
                return UiaPathResult.FallbackClipboard;
            }

            // Self-process: never UIA-SetValue on our own chrome; prefer editor text if non-empty.
            if (meta is not null && meta.IsOwnProcess)
            {
                string? editor = _host.GetEditorText();
                if (!string.IsNullOrEmpty(editor))
                {
                    text = editor;
                    source = UtilityTextSource.Editor;
                    writable = true; // we can write editor via host refresh, not UIA SetValue
                    tier = UiaSupportTier.A;
                    CrashLog.Write("utility: self-focus → editor text path");
                    return UiaPathResult.Success;
                }

                CrashLog.Write("utility: self-focus, empty editor → clipboard fallback");
                return UiaPathResult.FallbackClipboard;
            }

            if (!readOk || value is null)
                return UiaPathResult.FallbackClipboard;

            // Plan: non-empty required for UIA path; empty → clipboard.
            if (value.Length == 0)
            {
                CrashLog.Write("utility: UIA value empty → clipboard fallback");
                return UiaPathResult.FallbackClipboard;
            }

            text = value;
            source = UtilityTextSource.Uia;
            writable = meta?.CanWriteValue == true;
            tier = writable ? UiaSupportTier.A : UiaSupportTier.B;
            if (meta is not null)
                tier = meta.Tier is UiaSupportTier.A or UiaSupportTier.B ? meta.Tier : tier;

            CrashLog.Write($"utility: UIA acquire ok tier={tier} write={(writable ? 1 : 0)} len={text.Length}");
            return UiaPathResult.Success;
        }
        catch (Exception ex)
        {
            CrashLog.Write("utility TryAcquireUiaOrSelf: " + ex.Message);
            text = null;
            return UiaPathResult.FallbackClipboard;
        }
    }

    /// <summary>
    /// Phase 2 clipboard subroutine (regression-stable). Optional <paramref name="preloadedText"/>
    /// skips clipboard read when non-null. When null, reads clipboard via Win32 CF_UNICODETEXT.
    /// </summary>
    private void RunClipboardUtilityPath(string? preloadedText)
    {
        string? text = preloadedText;
        if (text is null)
        {
            text = _clipboard.TryGetText();
            if (string.IsNullOrEmpty(text))
            {
                _host.NotifyFeedback("clipboard", "Clipboard empty or no text", 0);
                return;
            }
        }

        ProcessAcquiredText(
            text,
            UtilityTextSource.Clipboard,
            uiaMeta: null,
            uiaWritable: false,
            tier: UiaSupportTier.C);
    }

    /// <summary>
    /// Shared check → fix → commit for any acquired source.
    /// </summary>
    private void ProcessAcquiredText(
        string text,
        UtilityTextSource source,
        UiaFocusSnapshot? uiaMeta,
        bool uiaWritable,
        UiaSupportTier tier)
    {
        BispellEngine engine = _host.Engine!;
        string pathLabel = PathLabel(source, tier);

        try { _host.ApplySettingsToEngine(); }
        catch (Exception ex)
        {
            CrashLog.Write("utility ApplySettingsToEngine: " + ex.Message);
        }

        IReadOnlyList<MisspellingItem> misses;
        try
        {
            misses = engine.Check(text);
        }
        catch (Exception ex)
        {
            CrashLog.Write("utility check failed: " + ex);
            _host.NotifyFeedback(pathLabel, "Spell check failed", 0);
            return;
        }

        int missCount = misses.Count;
        if (missCount == 0)
        {
            // No write when clean. Do not show window.
            _host.RefreshEditor(text, misses, showWindow: false);
            _host.NotifyFeedback(pathLabel, "No misspellings", 0);
            return;
        }

        ClipboardFixResult fix = ClipboardSpellFix.ApplyTopSuggestions(text, misses);
        bool replaceOn = _host.IsClipboardReplaceEnabled;

        bool wroteUia = false;
        bool wroteClipboard = false;
        bool wroteEditor = false;
        bool uiaWriteAttempted = false;
        UiaSupportTier effectiveTier = tier;

        if (replaceOn && fix.ReplacementsApplied > 0)
        {
            if (source == UtilityTextSource.Uia)
            {
                if (uiaWritable)
                {
                    // Tier A: in-place SetValue — do NOT Activate BiSpell first.
                    uiaWriteAttempted = true;
                    wroteUia = _uia.TryWriteFocusedValue(fix.FixedText, out UiaFocusSnapshot? writeMeta);
                    if (writeMeta is not null)
                        uiaMeta = writeMeta;

                    if (wroteUia)
                    {
                        effectiveTier = UiaSupportTier.A;
                    }
                    else
                    {
                        // Demote to Tier B → clipboard write of fixed text.
                        effectiveTier = UiaSupportTier.B;
                        CrashLog.Write("utility: UIA SetValue failed → Tier B clipboard demotion");
                        wroteClipboard = _clipboard.TrySetText(fix.FixedText);
                        if (!wroteClipboard)
                            CrashLog.Write("utility: TrySetText failed after UIA write fail");
                    }
                }
                else
                {
                    // Tier B read-only: fixed text → clipboard for user paste.
                    effectiveTier = UiaSupportTier.B;
                    wroteClipboard = _clipboard.TrySetText(fix.FixedText);
                    if (!wroteClipboard)
                        CrashLog.Write("utility: TrySetText failed (Tier B read-only UIA)");
                }
            }
            else if (source == UtilityTextSource.Editor)
            {
                // Self-focus: apply into main editor only (no UIA SetValue, no clipboard clobber).
                wroteEditor = true;
            }
            else
            {
                // Phase 2 clipboard path.
                wroteClipboard = _clipboard.TrySetText(fix.FixedText);
                if (!wroteClipboard)
                    CrashLog.Write("utility: TrySetText failed after fixes");
            }
        }

        pathLabel = PathLabel(source, effectiveTier);

        string body = BuildBody(
            source,
            effectiveTier,
            missCount,
            fix,
            replaceOn,
            wroteUia,
            wroteClipboard,
            wroteEditor,
            uiaWriteAttempted);

        // Show window when user needs to review: replace off, partial skip, write fail, or no apply.
        bool wroteSomething = wroteUia || wroteClipboard || wroteEditor;
        bool needShow =
            !replaceOn
            || fix.SkippedNoSuggestion > 0
            || fix.ReplacementsApplied == 0
            || (fix.ReplacementsApplied > 0 && !wroteSomething);

        // Prefer original for review when replace is off; fixed text when we wrote/replaced.
        string editorText =
            replaceOn && fix.ReplacementsApplied > 0
                ? fix.FixedText
                : text;

        // Editor path always refresh with fixed/original; show per needShow.
        // Order: write already done → then refresh / optional ShowMainWindow.
        _host.RefreshEditor(editorText, misses, showWindow: needShow);
        _host.NotifyFeedback(pathLabel, body, missCount);

        if (wroteSomething && fix.ReplacementsApplied > 0)
        {
            try { _host.RunEditorRecheck(); }
            catch { /* best-effort */ }
        }
    }

    private static string PathLabel(UtilityTextSource source, UiaSupportTier tier) => source switch
    {
        UtilityTextSource.Uia => $"UIA tier {tier}",
        UtilityTextSource.Editor => "editor",
        UtilityTextSource.Clipboard => "clipboard",
        _ => "utility",
    };

    private static string BuildBody(
        UtilityTextSource source,
        UiaSupportTier tier,
        int missCount,
        ClipboardFixResult fix,
        bool replaceOn,
        bool wroteUia,
        bool wroteClipboard,
        bool wroteEditor,
        bool uiaWriteAttempted)
    {
        if (!replaceOn)
            return $"{missCount} misspelling(s) — replace off";

        if (fix.ReplacementsApplied <= 0)
            return $"{missCount} misspelling(s), no suggestions to apply";

        if (source == UtilityTextSource.Uia)
        {
            if (wroteUia)
                return $"Fixed {fix.ReplacementsApplied} word(s) in place, skipped {fix.SkippedNoSuggestion}";

            if (uiaWriteAttempted && wroteClipboard)
                return $"Fixed {fix.ReplacementsApplied} word(s) → clipboard (UIA write failed), skipped {fix.SkippedNoSuggestion}";

            if (uiaWriteAttempted && !wroteClipboard)
                return $"Fixed {fix.ReplacementsApplied} word(s); UIA write + clipboard failed";

            // Read-only Tier B
            if (wroteClipboard)
                return $"Fixed {fix.ReplacementsApplied} word(s) → clipboard (read-only UIA), skipped {fix.SkippedNoSuggestion}";

            return $"Fixed {fix.ReplacementsApplied} word(s); clipboard write failed (tier {tier})";
        }

        if (source == UtilityTextSource.Editor)
        {
            return wroteEditor
                ? $"Fixed {fix.ReplacementsApplied} word(s) in editor, skipped {fix.SkippedNoSuggestion}"
                : $"Fixed {fix.ReplacementsApplied} word(s); editor apply skipped";
        }

        // Clipboard path (Phase 2 wording)
        if (wroteClipboard)
            return $"Fixed {fix.ReplacementsApplied} word(s), skipped {fix.SkippedNoSuggestion}";

        return $"Fixed {fix.ReplacementsApplied} word(s); clipboard write failed";
    }
}
