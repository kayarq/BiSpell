using BiSpell.Interop;
using BiSpell.Models;
using BiSpell.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BiSpell;

/// <summary>
/// MVP spell shell: check → list misspellings → suggestions → apply via UTF-16 ranges.
/// Settings persist to %APPDATA%\BiSpell\settings.json; lexicon to user-lexicon.json.
/// Keyboard: F7 = check, Enter on suggestions = apply top/selected.
/// Double-click suggestion (or misspelling with a top suggestion) applies.
///
/// Settings card (P1–P4-SETTINGS + W1): XAML holds structure only (no IsChecked /
/// Checked / Unchecked / Value / ValueChanged). Boolean and NumberBox state + handlers
/// are applied in the ctor after InitializeComponent and LoadSettingsIntoUi so handlers
/// never run mid-tree construction (root cause of ToggleButton.IsChecked assign failure
/// in v0.1.3). Exposes enable / TR / EN / maxSuggestions / minWordLength /
/// debounceMilliseconds plus shell-only globalHotkeyEnabled / clipboardReplaceEnabled /
/// uiaAssistEnabled / asYouTypeEnabled (shell flags not in native ABI; debounce ms is).
/// Path hint under the card title. As-you-type TextChanged pipeline is P4-GLUE (not here).
///
/// Utility hotkey (P2/P3-GLUE mandate B): App invokes <see cref="HandleUtilityHotkey"/> on
/// the UI dispatcher. Orchestration lives in <see cref="UtilityHotkeyOrchestrator"/>
/// (UIA-first when <c>uiaAssistEnabled</c>, else Phase 2 clipboard subroutine). This window
/// is a thin <see cref="IUtilityHotkeyHost"/>: engine, settings, editor, status/tray only.
/// No second engine instance. Concurrent F7 + hotkey serialize on the UI thread.
///
/// Lexicon panel (P1-LEXUI Mandate A): inline Expander with dual ListViews (Dictionary |
/// Ignored) + Remove selected / Unignore. Live data from engine ListAddedWords /
/// ListIgnoredWords; mutations via RemoveFromDictionary / UnignoreWord. Refresh after
/// add/ignore/remove/unignore and after engine init. No ignore-in-app editor; no new Window.
///
/// Probe (P3-PROBE): “Probe focused control” calls <see cref="UiaTextAccess.TryProbeFocused"/>
/// on the UI thread; status line + CrashLog only (no modal). Disabled in smoke and when
/// the engine is offline. Soft-fail; never hangs launch.
/// </summary>
public sealed partial class MainWindow : Window, IUtilityHotkeyHost
{
    private BispellEngine? _engine;
    private MisspellingItem? _selectedMisspelling;
    private string? _lastError;
    private readonly SettingsStore _settingsStore = new();
    private AppUserSettings _settings = AppUserSettings.CreateDefault();
    /// <summary>True while applying programmatic settings (load / language guard). Handlers are
    /// also only wired after first load, so init never relies on event order.</summary>
    private bool _suppressSettingsEvents = true;
    private string? _lexiconPath;
    private bool _settingsHandlersWired;

    /// <summary>UIA-first + clipboard utility orchestrator (owns re-entrancy + path policy).</summary>
    private readonly UtilityHotkeyOrchestrator _utilityOrchestrator;

    /// <summary>Shared soft-fail UIA facade for the probe button (COM cache inside).</summary>
    private readonly UiaTextAccess _uiaProbe = new();

    public MainWindow()
    {
        // W2: ctor is wrapped by App.OnLaunched try/catch → WriteFatal + Environment.Exit(1).
        // Markers here make XAML vs post-init failures easy to spot in BiSpell-startup.log.
        CrashLog.Write("MainWindow ctor: begin");

        // 1) Build visual tree with inert settings controls (no XAML event/state coupling).
        InitializeComponent();
        CrashLog.Write("MainWindow ctor: InitializeComponent done");
        Title = "BiSpell — Spell Check";

        // P3-GLUE: host = this (engine/settings/UI); orchestrator owns UIA + clipboard IO.
        _utilityOrchestrator = new UtilityHotkeyOrchestrator(this);

        // 2) Drive all boolean / numeric state from code while handlers are still unwired.
        LoadSettingsIntoUi();

        // 3) Wire change handlers only after tree is complete and initial values are set.
        //    Guarantees A1/A4/A5: no Settings_Changed during InitializeComponent, no early save.
        WireSettingsHandlers();
        _suppressSettingsEvents = false;
        CrashLog.Write("MainWindow ctor: settings loaded + handlers wired");

        // P3-PROBE: start disabled until engine is ready (and stay off in smoke).
        UpdateProbeButtonEnabled();

        // Defer native engine load so a missing VC++ runtime / DLL cannot kill the window before paint.
        // Engine failures are non-fatal for window startup (status already ok after Activate).
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try { TryInitEngine(); }
                catch (Exception ex)
                {
                    CrashLog.Write("Engine init failed (non-fatal for window):");
                    CrashLog.Write(ex);
                    ShowError("Engine init failed", ex.Message);
                }
            });
        }
        catch
        {
            TryInitEngine();
        }

        Closed += (_, _) =>
        {
            // Persist latest settings on teardown.
            try { SaveSettingsFromUi(); } catch { /* ignore */ }
            _engine?.Dispose();
            _engine = null;
        };

        CrashLog.Write("MainWindow ctor: complete");
    }

    /// <summary>Flush current UI settings to %APPDATA%\BiSpell\settings.json (called on hide/quit).</summary>
    public void PersistSettings() => SaveSettingsFromUi();

    /// <summary>Shell setting: global clipboard-utility hotkey enabled (for App registration).</summary>
    public bool IsGlobalHotkeyEnabled => _settings.GlobalHotkeyEnabled;

    /// <summary>Shell setting: write fixed text back to clipboard after utility check.</summary>
    public bool IsClipboardReplaceEnabled => _settings.ClipboardReplaceEnabled;

    /// <summary>Shell setting: try focused-control UIA before clipboard on utility hotkey (P3).</summary>
    public bool IsUiaAssistEnabled => _settings.UiaAssistEnabled;

    /// <summary>Shell setting: debounced as-you-type check in the editor (P4; GLUE wires TextChanged).</summary>
    public bool IsAsYouTypeEnabled => _settings.AsYouTypeEnabled;

    /// <summary>Current debounce wait in ms (native + as-you-type scheduler; default 250).</summary>
    public int DebounceMilliseconds => _settings.DebounceMilliseconds;

    /// <summary>
    /// Attach Checked/Unchecked/ValueChanged only after InitializeComponent + LoadSettingsIntoUi.
    /// Idempotent. XAML must not declare these attributes.
    /// </summary>
    private void WireSettingsHandlers()
    {
        if (_settingsHandlersWired) return;

        if (EnabledCheck is not null)
        {
            EnabledCheck.Checked += Settings_Changed;
            EnabledCheck.Unchecked += Settings_Changed;
        }

        if (TurkishCheck is not null)
        {
            TurkishCheck.Checked += Settings_Changed;
            TurkishCheck.Unchecked += Settings_Changed;
        }

        if (EnglishCheck is not null)
        {
            EnglishCheck.Checked += Settings_Changed;
            EnglishCheck.Unchecked += Settings_Changed;
        }

        if (MaxSuggestionsBox is not null)
            MaxSuggestionsBox.ValueChanged += MaxSuggestionsBox_ValueChanged;

        if (MinWordLengthBox is not null)
            MinWordLengthBox.ValueChanged += MinWordLengthBox_ValueChanged;

        if (DebounceBox is not null)
            DebounceBox.ValueChanged += DebounceBox_ValueChanged;

        // P2/P3-SETTINGS: shell-only utility toggles (no engine ApplySettingsToEngine needed).
        if (GlobalHotkeyCheck is not null)
        {
            GlobalHotkeyCheck.Checked += UtilitySettings_Changed;
            GlobalHotkeyCheck.Unchecked += UtilitySettings_Changed;
        }

        if (ClipboardReplaceCheck is not null)
        {
            ClipboardReplaceCheck.Checked += UtilitySettings_Changed;
            ClipboardReplaceCheck.Unchecked += UtilitySettings_Changed;
        }

        if (UiaAssistCheck is not null)
        {
            UiaAssistCheck.Checked += UtilitySettings_Changed;
            UiaAssistCheck.Unchecked += UtilitySettings_Changed;
        }

        // P4-SETTINGS: as-you-type is shell-only; persist only (GLUE will cancel/restart debounce).
        if (AsYouTypeCheck is not null)
        {
            AsYouTypeCheck.Checked += EditorSettings_Changed;
            AsYouTypeCheck.Unchecked += EditorSettings_Changed;
        }

        _settingsHandlersWired = true;
    }

    private void LoadSettingsIntoUi()
    {
        _settings = _settingsStore.Load();
        _suppressSettingsEvents = true;
        try
        {
            // Explicit nullable bool assign; values come from code only (not XAML defaults).
            if (EnabledCheck is not null)
                EnabledCheck.IsChecked = (bool?)_settings.IsEnabled;
            if (TurkishCheck is not null)
                TurkishCheck.IsChecked = (bool?)_settings.TurkishEnabled;
            if (EnglishCheck is not null)
                EnglishCheck.IsChecked = (bool?)_settings.EnglishEnabled;
            if (MaxSuggestionsBox is not null)
                MaxSuggestionsBox.Value = Math.Clamp(_settings.MaxSuggestions, 1, 20);
            if (MinWordLengthBox is not null)
                MinWordLengthBox.Value = Math.Clamp(_settings.MinWordLength, 1, 10);
            if (DebounceBox is not null)
                DebounceBox.Value = Math.Clamp(_settings.DebounceMilliseconds, 0, 5000);

            // Shell-only utility flags (P2/P3-SETTINGS); not pushed to native BispellSettings.
            if (GlobalHotkeyCheck is not null)
                GlobalHotkeyCheck.IsChecked = (bool?)_settings.GlobalHotkeyEnabled;
            if (ClipboardReplaceCheck is not null)
                ClipboardReplaceCheck.IsChecked = (bool?)_settings.ClipboardReplaceEnabled;
            if (UiaAssistCheck is not null)
                UiaAssistCheck.IsChecked = (bool?)_settings.UiaAssistEnabled;

            // P4-SETTINGS: as-you-type shell toggle (default true; not native ABI).
            if (AsYouTypeCheck is not null)
                AsYouTypeCheck.IsChecked = (bool?)_settings.AsYouTypeEnabled;

            // Path hint: concrete settings.json location when AppData is available.
            if (SettingsPathHint is not null)
            {
                try
                {
                    SettingsPathHint.Text = $"Saved to {AppPaths.SettingsPath}";
                }
                catch
                {
                    SettingsPathHint.Text = "Saved to %APPDATA%\\BiSpell\\settings.json";
                }
            }
        }
        finally
        {
            // Leave suppress true until WireSettingsHandlers completes in ctor; caller clears it.
        }
    }

    private void SaveSettingsFromUi()
    {
        if (EnabledCheck is not null)
            _settings.IsEnabled = EnabledCheck.IsChecked == true;
        if (TurkishCheck is not null)
            _settings.TurkishEnabled = TurkishCheck.IsChecked == true;
        if (EnglishCheck is not null)
            _settings.EnglishEnabled = EnglishCheck.IsChecked == true;
        if (MaxSuggestionsBox is not null)
        {
            _settings.MaxSuggestions = double.IsNaN(MaxSuggestionsBox.Value)
                ? 5
                : (int)Math.Clamp(MaxSuggestionsBox.Value, 1, 20);
        }
        if (MinWordLengthBox is not null)
        {
            _settings.MinWordLength = double.IsNaN(MinWordLengthBox.Value)
                ? 2
                : (int)Math.Clamp(MinWordLengthBox.Value, 1, 10);
        }
        if (DebounceBox is not null)
        {
            _settings.DebounceMilliseconds = double.IsNaN(DebounceBox.Value)
                ? 250
                : (int)Math.Clamp(DebounceBox.Value, 0, 5000);
        }

        if (GlobalHotkeyCheck is not null)
            _settings.GlobalHotkeyEnabled = GlobalHotkeyCheck.IsChecked == true;
        if (ClipboardReplaceCheck is not null)
            _settings.ClipboardReplaceEnabled = ClipboardReplaceCheck.IsChecked == true;
        if (UiaAssistCheck is not null)
            _settings.UiaAssistEnabled = UiaAssistCheck.IsChecked == true;
        if (AsYouTypeCheck is not null)
            _settings.AsYouTypeEnabled = AsYouTypeCheck.IsChecked == true;

        _settings.Normalize();
        _settingsStore.Save(_settings);
    }

    private void ApplySettingsToEngine()
    {
        if (_engine is null) return;
        try
        {
            _engine.UpdateSettings(_settings.ToNative());
        }
        catch (Exception ex)
        {
            ShowError("Settings update failed", ex.Message);
        }
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        if (TurkishCheck is null || EnglishCheck is null || EnabledCheck is null) return;

        // Keep at least one language on (UI + model). Product rule: never both TR and EN off.
        if (TurkishCheck.IsChecked != true && EnglishCheck.IsChecked != true)
        {
            _suppressSettingsEvents = true;
            try
            {
                EnglishCheck.IsChecked = (bool?)true;
            }
            finally
            {
                _suppressSettingsEvents = false;
            }
        }

        SaveSettingsFromUi();
        ApplySettingsToEngine();

        string state = _settings.IsEnabled ? "enabled" : "disabled";
        SetStatus(
            $"Settings saved ({state}; TR={_settings.TurkishEnabled}, EN={_settings.EnglishEnabled}, max={_settings.MaxSuggestions}, minLen={_settings.MinWordLength}). Press F7 to re-check.",
            CountFromList());
    }

    /// <summary>
    /// Shell-only utility toggles (global hotkey / clipboard replace / UIA assist). Persist
    /// only; do not call <see cref="ApplySettingsToEngine"/> (flags are not in BispellSettings).
    /// Hotkey register/unregister via <see cref="App.SyncGlobalHotkeyFromSettings"/> (no restart).
    /// UIA assist is read at hotkey time (P3-GLUE); no COM registration here.
    /// </summary>
    private void UtilitySettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;

        SaveSettingsFromUi();

        // Register / unregister without process restart when Global hotkey toggles.
        try
        {
            App.Current.SyncGlobalHotkeyFromSettings();
        }
        catch (Exception ex)
        {
            CrashLog.Write("UtilitySettings_Changed hotkey sync: " + ex.Message);
        }

        SetStatus(
            $"Utility settings saved (hotkey={(_settings.GlobalHotkeyEnabled ? "on" : "off")}, " +
            $"clipboard replace={(_settings.ClipboardReplaceEnabled ? "on" : "off")}, " +
            $"UIA assist={(_settings.UiaAssistEnabled ? "on" : "off")}).",
            CountFromList());
    }

    /// <summary>
    /// Shell-only editor toggles (as-you-type). Persist only; not in <see cref="BispellSettings"/>.
    /// P4-GLUE will also cancel/restart the debounce scheduler when this fires; this unit
    /// only saves + status feedback (no TextChanged pipeline).
    /// </summary>
    private void EditorSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;

        SaveSettingsFromUi();
        SetStatus(
            $"Editor settings saved (as-you-type={(_settings.AsYouTypeEnabled ? "on" : "off")}).",
            CountFromList());
    }

    /// <summary>
    /// Update the caption under the utility checkboxes with the live binding (App calls this).
    /// </summary>
    public void UpdateHotkeyBindingCaption(string? bindingDisplay, bool registered, bool smoke)
    {
        if (HotkeyBindingCaption is null) return;

        try
        {
            if (smoke)
            {
                HotkeyBindingCaption.Text =
                    "Global hotkey: disabled in smoke mode (BISPELL_SMOKE).";
            }
            else if (registered && !string.IsNullOrEmpty(bindingDisplay))
            {
                HotkeyBindingCaption.Text =
                    $"Global hotkey: {bindingDisplay} — focus a field (UIA) or copy text, press combo to check/fix.";
            }
            else if (!_settings.GlobalHotkeyEnabled)
            {
                HotkeyBindingCaption.Text =
                    "Global hotkey: off (enable checkbox to register Ctrl+Alt+. or fallback).";
            }
            else
            {
                HotkeyBindingCaption.Text =
                    "Global hotkey: unavailable (Ctrl+Alt+. and Win+Shift+. both failed — another app may own them).";
            }
        }
        catch
        {
            // Caption is cosmetic.
        }
    }

    /// <summary>
    /// Global utility hotkey entry (P3-GLUE). UI dispatcher only. Delegates to
    /// <see cref="UtilityHotkeyOrchestrator"/> (UIA-first + clipboard fallback).
    /// </summary>
    public void HandleUtilityHotkey() => _utilityOrchestrator.HandleUtilityHotkey();

    /// <summary>
    /// P3-PROBE: soft-fail UIA probe of the currently focused control. Status + CrashLog only.
    /// Safe if BiSpell itself is focused (reports own=1). No modal; never throws to UI.
    /// </summary>
    private void ProbeFocusedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CrashLog.IsSmokeMode)
            {
                const string smokeMsg = "Probe: skipped (smoke mode / BISPELL_SMOKE).";
                CrashLog.Write("probe: " + smokeMsg);
                SetStatus(smokeMsg, CountFromList());
                return;
            }

            if (_engine is null)
            {
                const string offline = "Probe: engine offline — load dictionaries / bispell_core.dll first.";
                CrashLog.Write("probe: " + offline);
                SetStatus(offline, CountFromList());
                return;
            }

            UiaFocusSnapshot? snap = _uiaProbe.TryProbeFocused();
            if (snap is null)
            {
                const string none = "Probe: no snapshot (UIA unavailable or soft-fail).";
                CrashLog.Write("probe: " + none);
                SetStatus(none, CountFromList());
                return;
            }

            string line = snap.ToStatusLine();
            CrashLog.Write("probe: " + line);
            // Value length only (never dump password / full field content to status).
            string valueHint = snap.IsPassword
                ? "value=(refused)"
                : snap.CanReadValue
                    ? $"valueLen={(snap.Value?.Length ?? 0)}"
                    : "value=(n/a)";
            SetStatus($"Probe: {line} {valueHint}", CountFromList());
        }
        catch (Exception ex)
        {
            // Defensive: UiaTextAccess never throws; still guard UI path.
            CrashLog.Write("probe: unexpected " + ex.GetType().Name + ": " + ex.Message);
            try { SetStatus("Probe failed (soft): " + ex.Message, CountFromList()); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Enable probe when engine is loaded and not in smoke mode; otherwise disabled.
    /// </summary>
    private void UpdateProbeButtonEnabled()
    {
        if (ProbeFocusedButton is null) return;
        try
        {
            ProbeFocusedButton.IsEnabled = _engine is not null && !CrashLog.IsSmokeMode;
        }
        catch
        {
            // Button enable is cosmetic for smoke safety.
        }
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="HandleUtilityHotkey"/> (Phase 2 name).
    /// </summary>
    public void HandleClipboardUtilityHotkey() => HandleUtilityHotkey();

    // ---- IUtilityHotkeyHost (thin shell for orchestrator) ----

    BispellEngine? IUtilityHotkeyHost.Engine => _engine;

    bool IUtilityHotkeyHost.IsSpellEnabled => _settings.IsEnabled;

    bool IUtilityHotkeyHost.IsUiaAssistEnabled => _settings.UiaAssistEnabled;

    bool IUtilityHotkeyHost.IsClipboardReplaceEnabled => _settings.ClipboardReplaceEnabled;

    void IUtilityHotkeyHost.SaveSettingsFromUi() => SaveSettingsFromUi();

    bool IUtilityHotkeyHost.EnsureEngineReady()
    {
        if (_engine is not null)
            return true;
        TryInitEngine();
        return _engine is not null;
    }

    string? IUtilityHotkeyHost.GetEditorText()
    {
        try
        {
            return EditorBox?.Text;
        }
        catch
        {
            return null;
        }
    }

    void IUtilityHotkeyHost.ApplySettingsToEngine() => ApplySettingsToEngine();

    void IUtilityHotkeyHost.RefreshEditor(
        string text,
        IReadOnlyList<MisspellingItem> misses,
        bool showWindow)
    {
        try
        {
            if (EditorBox is not null)
                EditorBox.Text = text ?? string.Empty;

            if (MisspellingsList is not null)
                MisspellingsList.ItemsSource = misses;

            if (SuggestionsList is not null)
                SuggestionsList.ItemsSource = null;

            _selectedMisspelling = null;
            UpdateActionButtons();
        }
        catch (Exception ex)
        {
            CrashLog.Write("RefreshEditor (utility): " + ex.Message);
        }

        // Only after UIA/clipboard write attempts (orchestrator guarantees call order).
        if (showWindow)
        {
            try { App.Current.ShowMainWindow(); }
            catch { /* ignore */ }
        }
    }

    void IUtilityHotkeyHost.NotifyFeedback(string pathLabel, string body, int misspellingCount)
    {
        string prefix = string.IsNullOrEmpty(pathLabel) ? "Utility" : $"Utility ({pathLabel})";
        string status = prefix + ": " + body;

        try
        {
            SetStatus(status, misspellingCount);
        }
        catch { /* ignore */ }

        try
        {
            // Tray: keep body short; include path when useful.
            string tray = string.IsNullOrEmpty(pathLabel) ? body : $"[{pathLabel}] {body}";
            App.Current.ShowTrayBalloon("BiSpell", tray);
        }
        catch { /* ignore */ }

        CrashLog.Write("utility: " + status);
    }

    void IUtilityHotkeyHost.RunEditorRecheck()
    {
        try { RunCheck(); }
        catch { /* best-effort */ }
    }

    private void MaxSuggestionsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents) return;
        if (MaxSuggestionsBox is null) return;
        if (double.IsNaN(args.NewValue)) return;

        // Clamp to product rule 1–20 in the UI as well as the model.
        double clamped = Math.Clamp(args.NewValue, 1, 20);
        if (!double.IsNaN(MaxSuggestionsBox.Value) && Math.Abs(MaxSuggestionsBox.Value - clamped) > 0.001)
        {
            _suppressSettingsEvents = true;
            try { MaxSuggestionsBox.Value = clamped; }
            finally { _suppressSettingsEvents = false; }
        }

        SaveSettingsFromUi();
        ApplySettingsToEngine();
        SetStatus(
            $"Max suggestions = {_settings.MaxSuggestions} (saved). Press F7 to re-check.",
            CountFromList());
    }

    private void MinWordLengthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents) return;
        if (MinWordLengthBox is null) return;
        if (double.IsNaN(args.NewValue)) return;

        // Clamp to product rule 1–10 (matches AppUserSettings.Normalize / engine).
        double clamped = Math.Clamp(args.NewValue, 1, 10);
        if (!double.IsNaN(MinWordLengthBox.Value) && Math.Abs(MinWordLengthBox.Value - clamped) > 0.001)
        {
            _suppressSettingsEvents = true;
            try { MinWordLengthBox.Value = clamped; }
            finally { _suppressSettingsEvents = false; }
        }

        SaveSettingsFromUi();
        ApplySettingsToEngine();
        SetStatus(
            $"Min word length = {_settings.MinWordLength} (saved). Short tokens skipped on check. Press F7 to re-check.",
            CountFromList());
    }

    private void DebounceBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents) return;
        if (DebounceBox is null) return;
        if (double.IsNaN(args.NewValue)) return;

        // Clamp to model rule 0–5000 (matches AppUserSettings.Normalize / native ToNative).
        double clamped = Math.Clamp(args.NewValue, 0, 5000);
        if (!double.IsNaN(DebounceBox.Value) && Math.Abs(DebounceBox.Value - clamped) > 0.001)
        {
            _suppressSettingsEvents = true;
            try { DebounceBox.Value = clamped; }
            finally { _suppressSettingsEvents = false; }
        }

        SaveSettingsFromUi();
        // Debounce is in BispellSettings; keep native in sync for consistency.
        ApplySettingsToEngine();
        SetStatus(
            $"Debounce = {_settings.DebounceMilliseconds} ms (saved). Wait after typing before re-checking.",
            CountFromList());
    }

    private void TryInitEngine()
    {
        try
        {
            var dictDir = ResolveDictionaryDirectory();
            if (dictDir is null)
            {
                ShowError(
                    "Dictionaries not found",
                    "Could not locate tr.dic and en_US.dic.\n" +
                    "Expected next to the app under Dictionaries\\, or under the repo path " +
                    "Sources\\BiSpellCore\\Resources\\Dictionaries\\.\n" +
                    "See windows/README.md for packaging steps.");
                SetStatus("Engine not loaded — missing dictionaries.", 0);
                RefreshLexiconLists();
                UpdateProbeButtonEnabled();
                return;
            }

            // Verify files exist with a clear message before calling native.
            var en = Path.Combine(dictDir, "en_US.dic");
            var tr = Path.Combine(dictDir, "tr.dic");
            if (!File.Exists(en) || !File.Exists(tr))
            {
                ShowError(
                    "Dictionary files missing",
                    $"Required files not found in:\n{dictDir}\n\n" +
                    $"en_US.dic present: {File.Exists(en)}\n" +
                    $"tr.dic present: {File.Exists(tr)}");
                SetStatus("Engine not loaded — incomplete dictionary folder.", 0);
                RefreshLexiconLists();
                UpdateProbeButtonEnabled();
                return;
            }

            // Ensure latest UI values are in _settings before create.
            SaveSettingsFromUi();
            var settings = _settings.ToNative();

            _lexiconPath = null;
            try
            {
                _lexiconPath = AppPaths.LexiconPath;
            }
            catch
            {
                // Memory-only lexicon if AppData unavailable.
                _lexiconPath = null;
            }

            _engine?.Dispose();
            _engine = BispellEngine.Create(dictDir, _lexiconPath, settings);
            ErrorBar.IsOpen = false;
            RefreshLexiconLists();
            UpdateProbeButtonEnabled();

            var settingsHint = AppPaths.SettingsPath;
            SetStatus(
                $"Engine ready — dicts: {dictDir} | settings: {settingsHint} | lexicon: {_lexiconPath ?? "(memory)"}",
                0);
        }
        catch (DllNotFoundException ex)
        {
            // Keep DllNotFoundException text in startup log for smoke search (non-fatal window).
            CrashLog.Write(ex);
            ShowError(
                "Native library not found (bispell_core.dll)",
                "P/Invoke could not load bispell_core.dll.\n" +
                "Build the shared core with CMake (target bispell_core_shared) and copy " +
                "bispell_core.dll next to BiSpell.App.exe, or into windows/app/native/x64/.\n\n" +
                "See windows/README.md — “Build native DLL”.\n\n" +
                ex.Message);
            SetStatus("Engine not loaded — bispell_core.dll missing.", 0);
            RefreshLexiconLists();
            UpdateProbeButtonEnabled();
        }
        catch (BadImageFormatException ex)
        {
            ShowError(
                "Architecture mismatch",
                "bispell_core.dll architecture does not match this process (x64 vs x86/ARM64).\n" +
                "Rebuild the DLL for the same platform as the app.\n\n" + ex.Message);
            SetStatus("Engine not loaded — bad image format.", 0);
            RefreshLexiconLists();
            UpdateProbeButtonEnabled();
        }
        catch (BispellException ex)
        {
            ShowError("Failed to load spell engine", ex.Message);
            SetStatus("Engine not loaded — see error bar.", 0);
            RefreshLexiconLists();
            UpdateProbeButtonEnabled();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ShowError("Unexpected startup error", ex.Message);
            SetStatus("Engine not loaded — unexpected error.", 0);
            RefreshLexiconLists();
            UpdateProbeButtonEnabled();
        }
    }

    /// <summary>
    /// Resolve dictionary folder: app-relative Dictionaries first, then repo SoT paths.
    /// </summary>
    private static string? ResolveDictionaryDirectory()
    {
        var candidates = new List<string>();

        // 1) Next to the executable (packaged / CopyToOutputDirectory)
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Dictionaries"));

        // 2) Env override for tests / custom installs
        var env = Environment.GetEnvironmentVariable("BISPELL_DICT_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Insert(0, env);

        // 3) Walk up from base dir / cwd looking for SoT
        foreach (var start in new[] { baseDir, Directory.GetCurrentDirectory() })
        {
            try
            {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
                {
                    candidates.Add(Path.Combine(dir.FullName, "Sources", "BiSpellCore", "Resources", "Dictionaries"));
                    candidates.Add(Path.Combine(dir.FullName, "windows", "assets", "Dictionaries"));
                    candidates.Add(Path.Combine(dir.FullName, "Dictionaries"));
                }
            }
            catch { /* ignore */ }
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(c)
                && File.Exists(Path.Combine(c, "en_US.dic"))
                && File.Exists(Path.Combine(c, "tr.dic")))
            {
                return Path.GetFullPath(c);
            }
        }

        return null;
    }

    private void CheckButton_Click(object sender, RoutedEventArgs e) => RunCheck();

    private void RunCheck()
    {
        if (_engine is null)
        {
            TryInitEngine();
            if (_engine is null)
            {
                SetStatus("Cannot check — engine not loaded.", 0);
                return;
            }
        }

        try
        {
            // Push latest settings so enable/maxSuggestions gate the check.
            ApplySettingsToEngine();

            var text = EditorBox.Text ?? string.Empty;
            var misspellings = _engine.Check(text);
            MisspellingsList.ItemsSource = misspellings;
            SuggestionsList.ItemsSource = null;
            _selectedMisspelling = null;
            UpdateActionButtons();

            int n = misspellings.Count;
            if (!_settings.IsEnabled)
            {
                SetStatus("Spell-check is disabled in settings — empty result.", 0);
            }
            else
            {
                SetStatus(n == 0
                    ? "No misspellings found."
                    : $"Found {n} misspelling{(n == 1 ? "" : "s")}. Select one to see suggestions.", n);
            }
            ErrorBar.IsOpen = false;
        }
        catch (BispellException ex)
        {
            ShowError("Spell check failed", ex.Message);
            SetStatus("Check failed.", MisspellingsList.Items?.Count ?? 0);
        }
        catch (Exception ex)
        {
            ShowError("Spell check failed", ex.Message);
            SetStatus("Check failed.", 0);
        }
    }

    private void MisspellingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMisspelling = MisspellingsList.SelectedItem as MisspellingItem;
        if (_selectedMisspelling is null)
        {
            SuggestionsList.ItemsSource = null;
            UpdateActionButtons();
            return;
        }

        // Prefer suggestions already attached by engine check; refresh if empty.
        IReadOnlyList<string> suggestions = _selectedMisspelling.Suggestions;
        if (suggestions.Count == 0 && _engine is not null)
        {
            try
            {
                suggestions = _engine.Suggestions(_selectedMisspelling.Word, _selectedMisspelling.Language);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        SuggestionsList.ItemsSource = suggestions;
        if (suggestions.Count > 0)
            SuggestionsList.SelectedIndex = 0;

        // Highlight range in editor (selection uses UTF-16 indices = C# string indices).
        try
        {
            int start = (int)_selectedMisspelling.Utf16Location;
            int len = (int)_selectedMisspelling.Utf16Length;
            var text = EditorBox.Text ?? string.Empty;
            if (start >= 0 && len >= 0 && start + len <= text.Length)
            {
                EditorBox.Select(start, len);
                EditorBox.Focus(FocusState.Programmatic);
            }
        }
        catch { /* selection is best-effort */ }

        UpdateActionButtons();
        SetStatus(
            $"Selected “{_selectedMisspelling.Word}” ({_selectedMisspelling.LanguageLabel}) — " +
            $"{suggestions.Count} suggestion(s). Enter/double-click to apply.",
            (MisspellingsList.ItemsSource as IReadOnlyList<MisspellingItem>)?.Count
                ?? MisspellingsList.Items.Count);
    }

    private void SuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => ApplySelectedSuggestion();

    private void SuggestionsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => ApplySelectedSuggestion();

    private void MisspellingsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Double-click misspelling: apply top suggestion if any.
        if (_selectedMisspelling is null) return;
        if (SuggestionsList.Items.Count > 0)
        {
            SuggestionsList.SelectedIndex = 0;
            ApplySelectedSuggestion();
        }
    }

    private void SuggestionsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ApplySelectedSuggestion();
            e.Handled = true;
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F7)
        {
            RunCheck();
            e.Handled = true;
            return;
        }

        // Enter applies top/selected suggestion when focus is not in the editor.
        if (e.Key == VirtualKey.Enter && !IsEditorFocused())
        {
            if (SuggestionsList.Items.Count > 0)
            {
                ApplySelectedSuggestion();
                e.Handled = true;
            }
        }
    }

    private bool IsEditorFocused()
    {
        try
        {
            var root = RootGrid.XamlRoot ?? Content?.XamlRoot;
            if (root is null) return false;
            var focused = FocusManager.GetFocusedElement(root);
            return ReferenceEquals(focused, EditorBox);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Replace the selected misspelling in the editor using UTF-16 location/length
    /// (same contract as NSRange on macOS / C# string indexing).
    /// </summary>
    private void ApplySelectedSuggestion()
    {
        if (_selectedMisspelling is null)
        {
            SetStatus("Select a misspelling first.", CountFromList());
            return;
        }

        string? suggestion = SuggestionsList.SelectedItem as string;
        if (string.IsNullOrEmpty(suggestion))
        {
            if (SuggestionsList.Items.Count > 0)
                suggestion = SuggestionsList.Items[0] as string;
        }

        if (string.IsNullOrEmpty(suggestion))
        {
            SetStatus("No suggestion to apply.", CountFromList());
            return;
        }

        var text = EditorBox.Text ?? string.Empty;
        int start = (int)_selectedMisspelling.Utf16Location;
        int len = (int)_selectedMisspelling.Utf16Length;

        if (start < 0 || len < 0 || start + len > text.Length)
        {
            ShowError(
                "Invalid UTF-16 range",
                $"Cannot apply: range [{start}, {len}] is outside text length {text.Length}. Re-run Check.");
            return;
        }

        // Verify the range still matches the expected word (user may have edited).
        var current = text.Substring(start, len);
        if (!string.Equals(current, _selectedMisspelling.Word, StringComparison.Ordinal))
        {
            SetStatus(
                $"Text changed under “{_selectedMisspelling.Word}” (now “{current}”). Re-run Check.",
                CountFromList());
            return;
        }

        EditorBox.Text = string.Concat(text.AsSpan(0, start), suggestion, text.AsSpan(start + len));
        // Place caret after the replacement (UTF-16 units).
        int newCaret = start + suggestion.Length;
        try
        {
            EditorBox.Select(newCaret, 0);
        }
        catch { /* ignore */ }

        SetStatus($"Applied “{suggestion}” at UTF-16 {start}+{len}. Re-checking…", CountFromList());
        RunCheck();
    }

    private void AddDictButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _selectedMisspelling is null) return;
        try
        {
            var word = _selectedMisspelling.Word;
            _engine.AddToDictionary(word);
            RefreshLexiconLists();
            SetStatus(
                $"Added “{word}” to personal dictionary ({_lexiconPath ?? "memory"}). Re-checking…",
                CountFromList());
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Add to dictionary failed", ex.Message);
        }
    }

    private void IgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _selectedMisspelling is null) return;
        try
        {
            var word = _selectedMisspelling.Word;
            _engine.IgnoreWord(word);
            RefreshLexiconLists();
            SetStatus($"Ignoring “{word}” in lexicon. Re-checking…", CountFromList());
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Ignore failed", ex.Message);
        }
    }

    private void AddedWordsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateLexiconActionButtons();

    private void IgnoredWordsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateLexiconActionButtons();

    private void RemoveWordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var word = AddedWordsList.SelectedItem as string;
        if (string.IsNullOrEmpty(word)) return;

        try
        {
            _engine.RemoveFromDictionary(word);
            RefreshLexiconLists();
            SetStatus(
                $"Removed “{word}” from personal dictionary. Re-checking…",
                CountFromList());
            // Re-check so the word can reappear as a misspelling if still in the editor.
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Remove from dictionary failed", ex.Message);
        }
    }

    private void UnignoreWordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var word = IgnoredWordsList.SelectedItem as string;
        if (string.IsNullOrEmpty(word)) return;

        try
        {
            _engine.UnignoreWord(word);
            RefreshLexiconLists();
            SetStatus($"Unignored “{word}”. Re-checking…", CountFromList());
            // Re-check so the word can reappear as a misspelling if still in the editor.
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Unignore failed", ex.Message);
        }
    }

    /// <summary>
    /// Pull live added/ignored words from the engine into the management ListViews.
    /// Safe when engine is null (clears lists, disables actions). Does not throw to UI.
    /// </summary>
    private void RefreshLexiconLists()
    {
        if (AddedWordsList is null || IgnoredWordsList is null)
            return;

        if (_engine is null)
        {
            AddedWordsList.ItemsSource = null;
            IgnoredWordsList.ItemsSource = null;
            if (AddedWordsHeader is not null)
                AddedWordsHeader.Text = "Dictionary (engine offline)";
            if (IgnoredWordsHeader is not null)
                IgnoredWordsHeader.Text = "Ignored (engine offline)";
            if (RemoveWordButton is not null)
                RemoveWordButton.IsEnabled = false;
            if (UnignoreWordButton is not null)
                UnignoreWordButton.IsEnabled = false;
            return;
        }

        try
        {
            var added = _engine.ListAddedWords();
            var ignored = _engine.ListIgnoredWords();

            AddedWordsList.ItemsSource = added;
            IgnoredWordsList.ItemsSource = ignored;

            if (AddedWordsHeader is not null)
            {
                AddedWordsHeader.Text = added.Count == 0
                    ? "Dictionary (empty)"
                    : $"Dictionary ({added.Count})";
            }
            if (IgnoredWordsHeader is not null)
            {
                IgnoredWordsHeader.Text = ignored.Count == 0
                    ? "Ignored (empty)"
                    : $"Ignored ({ignored.Count})";
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: leave previous items if any; surface message.
            CrashLog.Write("RefreshLexiconLists failed:");
            CrashLog.Write(ex);
            if (AddedWordsHeader is not null)
                AddedWordsHeader.Text = "Dictionary (refresh failed)";
            if (IgnoredWordsHeader is not null)
                IgnoredWordsHeader.Text = "Ignored (refresh failed)";
        }

        UpdateLexiconActionButtons();
    }

    private void UpdateLexiconActionButtons()
    {
        bool engineOk = _engine is not null;
        if (RemoveWordButton is not null)
            RemoveWordButton.IsEnabled = engineOk && AddedWordsList?.SelectedItem is string;
        if (UnignoreWordButton is not null)
            UnignoreWordButton.IsEnabled = engineOk && IgnoredWordsList?.SelectedItem is string;
    }

    private void UpdateActionButtons()
    {
        bool hasMiss = _selectedMisspelling is not null;
        bool hasSuggestion = SuggestionsList.SelectedItem is string
            || (SuggestionsList.Items.Count > 0);
        ApplyButton.IsEnabled = hasMiss && hasSuggestion;
        AddDictButton.IsEnabled = hasMiss;
        IgnoreButton.IsEnabled = hasMiss;
    }

    private void SetStatus(string message, int misspellingCount)
    {
        StatusText.Text = message;
        CountBadge.Text = misspellingCount == 1
            ? "1 misspelling"
            : $"{misspellingCount} misspellings";
    }

    private int CountFromList()
    {
        if (MisspellingsList.ItemsSource is IReadOnlyList<MisspellingItem> list)
            return list.Count;
        return MisspellingsList.Items.Count;
    }

    private void ShowError(string title, string message)
    {
        _lastError = message;
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.IsOpen = true;
    }
}
