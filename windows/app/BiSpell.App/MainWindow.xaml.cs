using BiSpell.Interop;
using BiSpell.Models;
using BiSpell.Services;
using BiSpell.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BiSpell;

/// <summary>
/// In-app Notes + spell shell: notes sidebar, editor check → misspellings → suggestions → apply.
/// Settings persist to %APPDATA%\BiSpell\settings.json; lexicon to user-lexicon.json;
/// notes under %APPDATA%\BiSpell\Notes\ as .txt files.
/// Keyboard: F7 = check, Ctrl+S = save note, Enter on suggestions = apply.
///
/// Settings card (W1): XAML structure only; bool/NumberBox state + handlers wired after
/// InitializeComponent + LoadSettingsIntoUi. Exposes enable / TR / EN / maxSuggestions /
/// minWordLength / debounceMilliseconds / asYouTypeEnabled.
///
/// As-you-type (P4 polish): length-aware scheduling —
/// delete → hide popup + QuietRecheck after max(debounce, 450);
/// insert letter/digit → no schedule (avoids UI freeze mid-word);
/// insert whitespace/punct or same-length replace → FullAsYouType (list + popup);
/// programmatic text sets use <c>_suppressAsYouType</c>; smoke no-ops.
///
/// Editor-only product: no global hotkey, UIA, or out-of-app clipboard utility.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Near-caret window radius (UTF-16) when document exceeds threshold.</summary>
    private const int NearCaretWindowRadius = 256;

    /// <summary>Minimum settle wait after delete/backspace before quiet list recheck.</summary>
    private const int DeleteSettleMilliseconds = 450;

    private BispellEngine? _engine;
    private MisspellingItem? _selectedMisspelling;
    private string? _lastError;
    private readonly SettingsStore _settingsStore = new();
    private AppUserSettings _settings = AppUserSettings.CreateDefault();
    /// <summary>True while applying programmatic settings (load / language guard).</summary>
    private bool _suppressSettingsEvents = true;
    private string? _lexiconPath;
    private bool _settingsHandlersWired;

    /// <summary>P4: debounced as-you-type scheduler (UI dispatcher only).</summary>
    private EditorSpellDebouncer? _editorSpellDebouncer;

    /// <summary>P4: suggestion popup near editor / caret estimate.</summary>
    private SuggestionPopupController? _suggestionPopup;

    /// <summary>
    /// True while programmatically setting <see cref="EditorBox"/>.Text so TextChanged
    /// does not storm debounced checks.
    /// </summary>
    private bool _suppressAsYouType;

    /// <summary>Re-entrancy guard for check paths (F7 + as-you-type fire).</summary>
    private bool _checkBusy;

    /// <summary>
    /// When true, misspelling list selection updates model/suggestions without focusing
    /// the editor or replacing the caret (as-you-type live refresh).
    /// </summary>
    private bool _suppressMissListSideEffects;

    /// <summary>Previous editor text length for insert vs delete detection.</summary>
    private int _lastEditorTextLength = -1;

    /// <summary>
    /// When the next debounced fire runs: true → list + nearest + popup;
    /// false → list update only (no popup open).
    /// </summary>
    private bool _asYouTypeWantPopup;

    // ---- Notes MVP ----
    private readonly NotesStore _notesStore = new();
    private NoteItem? _activeNote;
    private bool _suppressNotesSelection;
    private bool _noteDirty;

    public MainWindow()
    {
        // W2: ctor is wrapped by App.OnLaunched try/catch → WriteFatal + Environment.Exit(1).
        CrashLog.Write("MainWindow ctor: begin");

        InitializeComponent();
        CrashLog.Write("MainWindow ctor: InitializeComponent done");
        Title = "BiSpell — Notes";

        // As-you-type: debouncer + suggestion popup + TextChanged (after visual tree exists).
        try
        {
            _editorSpellDebouncer = new EditorSpellDebouncer(DispatcherQueue);
            _editorSpellDebouncer.Configure(_settings.DebounceMilliseconds, OnAsYouTypeFire);

            if (EditorBox is not null)
            {
                _suggestionPopup = new SuggestionPopupController(EditorBox);
                _suggestionPopup.SuggestionChosen += SuggestionPopup_SuggestionChosen;
                EditorBox.TextChanged += EditorBox_TextChanged;
                EditorBox.KeyDown += EditorBox_KeyDown;
            }
            CrashLog.Write("MainWindow ctor: as-you-type debouncer + popup wired");
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainWindow ctor: as-you-type wire failed (non-fatal):");
            CrashLog.Write(ex);
        }

        LoadSettingsIntoUi();
        WireSettingsHandlers();
        _suppressSettingsEvents = false;
        CrashLog.Write("MainWindow ctor: settings loaded + handlers wired");

        // Notes: load list + select first (after editor exists).
        try
        {
            RefreshNotesList(selectPath: null);
            CrashLog.Write("MainWindow ctor: notes list loaded");
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainWindow ctor: notes load failed (non-fatal):");
            CrashLog.Write(ex);
        }

        // Defer native engine load so a missing VC++ runtime / DLL cannot kill the window before paint.
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
            // Full teardown on any close path (App.Quit → Close, or Closing hook missed).
            try { SaveSettingsFromUi(); } catch { /* ignore */ }
            try { PersistActiveNote(); } catch { /* ignore */ }

            try
            {
                _editorSpellDebouncer?.Cancel();
                _editorSpellDebouncer?.Dispose();
                _editorSpellDebouncer = null;
            }
            catch { /* ignore */ }

            try
            {
                if (_suggestionPopup is not null)
                {
                    _suggestionPopup.SuggestionChosen -= SuggestionPopup_SuggestionChosen;
                    _suggestionPopup.Hide();
                    _suggestionPopup.Dispose();
                    _suggestionPopup = null;
                }
            }
            catch { /* ignore */ }

            try
            {
                if (EditorBox is not null)
                {
                    EditorBox.TextChanged -= EditorBox_TextChanged;
                    EditorBox.KeyDown -= EditorBox_KeyDown;
                }
            }
            catch { /* ignore */ }

            try
            {
                _engine?.Dispose();
                _engine = null;
            }
            catch { /* ignore */ }

            // Guarantee process exit: WinUI can keep the process alive after last window.
            // App.Quit is re-entrancy-safe (_isQuitting); if already quitting, Exit still runs
            // from the outer Quit after Close returns. If Closing never routed to Quit, do it now.
            try
            {
                if (!App.Current.IsQuitting)
                    App.Current.Quit();
            }
            catch
            {
                try { Environment.Exit(CrashLog.ExitOk); } catch { /* ignore */ }
            }
        };

        CrashLog.Write("MainWindow ctor: complete");
    }

    /// <summary>Flush current UI settings to %APPDATA%\BiSpell\settings.json (called on quit / switch).</summary>
    public void PersistSettings() => SaveSettingsFromUi();

    /// <summary>Save the active note body if dirty (called on quit / switch).</summary>
    public void PersistActiveNote()
    {
        try
        {
            if (_activeNote is null || !_noteDirty) return;
            SaveActiveNoteCore(refreshList: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("PersistActiveNote: " + ex.Message);
        }
    }

    /// <summary>Shell setting: debounced as-you-type check in the editor.</summary>
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
            if (AsYouTypeCheck is not null)
                AsYouTypeCheck.IsChecked = (bool?)_settings.AsYouTypeEnabled;

            // Settings path hint removed from chrome (P021-UI); paths remain in status/docs.
        }
        finally
        {
            // Leave suppress true until WireSettingsHandlers completes in ctor.
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

        // Keep at least one language on (UI + model).
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
    /// Shell-only editor toggles (as-you-type). Persist; cancel debouncer + hide popup when off.
    /// </summary>
    private void EditorSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;

        SaveSettingsFromUi();

        if (!_settings.AsYouTypeEnabled)
        {
            try { _editorSpellDebouncer?.Cancel(); } catch { /* ignore */ }
            try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
            SetStatus(
                "Editor settings saved (as-you-type=off). Typing will not auto-check; F7 still works.",
                CountFromList());
            return;
        }

        try
        {
            ScheduleAsYouTypeCheck(wantPopup: false);
        }
        catch { /* ignore */ }

        SetStatus(
            $"Editor settings saved (as-you-type=on, debounce={_settings.DebounceMilliseconds} ms).",
            CountFromList());
    }

    private void MaxSuggestionsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents) return;
        if (MaxSuggestionsBox is null) return;
        if (double.IsNaN(args.NewValue)) return;

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

        double clamped = Math.Clamp(args.NewValue, 0, 5000);
        if (!double.IsNaN(DebounceBox.Value) && Math.Abs(DebounceBox.Value - clamped) > 0.001)
        {
            _suppressSettingsEvents = true;
            try { DebounceBox.Value = clamped; }
            finally { _suppressSettingsEvents = false; }
        }

        SaveSettingsFromUi();
        ApplySettingsToEngine();
        if (_editorSpellDebouncer is not null)
            _editorSpellDebouncer.DebounceMilliseconds = _settings.DebounceMilliseconds;
        SetStatus(
            $"Debounce = {_settings.DebounceMilliseconds} ms (saved). Applies after typing pause / after delete settle.",
            CountFromList());
    }

    // =========================================================================
    // Notes MVP
    // =========================================================================

    private void RefreshNotesList(string? selectPath)
    {
        // Never force a welcome note — empty Notes folder stays empty.
        var notes = _notesStore.ListNotes();
        _suppressNotesSelection = true;
        try
        {
            if (NotesList is not null)
                NotesList.ItemsSource = notes;

            NoteItem? pick = null;
            if (!string.IsNullOrEmpty(selectPath))
            {
                pick = notes.FirstOrDefault(n =>
                    string.Equals(n.FilePath, selectPath, StringComparison.OrdinalIgnoreCase));
            }
            else if (notes.Count > 0)
            {
                // Startup / refresh: select newest only when notes exist.
                pick = notes.FirstOrDefault();
            }
            // notes.Count == 0 → pick stays null (empty tray, empty editor).

            if (NotesList is not null)
                NotesList.SelectedItem = pick;

            if (pick is not null)
                LoadNoteIntoEditor(pick, markClean: true);
            else
            {
                _activeNote = null;
                SetEditorTextProgrammatic(string.Empty);
                _noteDirty = false;
                UpdateNoteChrome();
                // Clear spell UI when no note is open.
                try
                {
                    _suggestionPopup?.Hide();
                    if (MisspellingsList is not null)
                        MisspellingsList.ItemsSource = null;
                    if (SuggestionsList is not null)
                        SuggestionsList.ItemsSource = null;
                }
                catch { /* ignore */ }
                SetStatus("No notes yet. Click New to start writing.", 0);
            }
        }
        finally
        {
            _suppressNotesSelection = false;
        }

        UpdateNoteActionButtons();
    }

    private void LoadNoteIntoEditor(NoteItem note, bool markClean)
    {
        _activeNote = note;
        // Opening a note must not flash as-you-type popup on load (including brand words in titles).
        try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
        try { _editorSpellDebouncer?.Cancel(); } catch { /* ignore */ }
        string body;
        try { body = _notesStore.ReadBody(note.FilePath); }
        catch (Exception ex)
        {
            body = string.Empty;
            CrashLog.Write("LoadNoteIntoEditor: " + ex.Message);
        }

        SetEditorTextProgrammatic(body);
        _noteDirty = !markClean;
        UpdateNoteChrome();
        UpdateNoteActionButtons();
        // Do not auto-recheck on open — avoids popup flash / false hits on brand words.
        // User can F7 or keep typing (as-you-type) when ready.
    }

    private void SetEditorTextProgrammatic(string text)
    {
        _suppressAsYouType = true;
        try
        {
            if (EditorBox is not null)
                EditorBox.Text = text ?? string.Empty;
            _lastEditorTextLength = (text ?? string.Empty).Length;
        }
        finally
        {
            _suppressAsYouType = false;
        }

        try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
        try { _editorSpellDebouncer?.Cancel(); } catch { /* ignore */ }
    }

    private void UpdateNoteChrome()
    {
        if (EditorHeader is null) return;
        string title = _activeNote?.Title ?? "Note";
        string dirty = _noteDirty ? " •" : string.Empty;
        EditorHeader.Text = $"Note — {title}{dirty}";
        try
        {
            Title = _activeNote is null
                ? "BiSpell — Notes"
                : $"BiSpell — {title}{dirty}";
        }
        catch { /* ignore */ }
    }

    private void UpdateNoteActionButtons()
    {
        if (DeleteNoteButton is not null)
            DeleteNoteButton.IsEnabled = _activeNote is not null;
        if (SaveNoteButton is not null)
            SaveNoteButton.IsEnabled = _activeNote is not null;
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNotesSelection) return;
        var next = NotesList?.SelectedItem as NoteItem;
        if (next is null) return;
        if (_activeNote is not null
            && string.Equals(next.FilePath, _activeNote.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        // Auto-save current before switch.
        try
        {
            if (_activeNote is not null && _noteDirty)
                SaveActiveNoteCore(refreshList: false);
        }
        catch (Exception ex)
        {
            CrashLog.Write("auto-save on switch: " + ex.Message);
        }

        LoadNoteIntoEditor(next, markClean: true);
        SetStatus($"Opened “{next.Title}”.", CountFromList());
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_activeNote is not null && _noteDirty)
                SaveActiveNoteCore(refreshList: false);

            var created = _notesStore.CreateNote(string.Empty);
            RefreshNotesList(selectPath: created.FilePath);
            SetStatus("New note created.", 0);
            try { EditorBox?.Focus(FocusState.Programmatic); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            ShowError("Could not create note", ex.Message);
        }
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeNote is null) return;
        try
        {
            string path = _activeNote.FilePath;
            string title = _activeNote.Title;
            _notesStore.DeleteNote(path);
            _activeNote = null;
            _noteDirty = false;
            RefreshNotesList(selectPath: null);
            SetStatus($"Deleted “{title}”.", 0);
        }
        catch (Exception ex)
        {
            ShowError("Could not delete note", ex.Message);
        }
    }

    private void SaveNoteButton_Click(object sender, RoutedEventArgs e) => SaveActiveNoteManual();

    private void SaveActiveNoteManual()
    {
        if (_activeNote is null)
        {
            SetStatus("No note selected to save.", CountFromList());
            return;
        }

        try
        {
            SaveActiveNoteCore(refreshList: true);
            SetStatus($"Saved “{_activeNote.Title}”.", CountFromList());
        }
        catch (Exception ex)
        {
            ShowError("Save failed", ex.Message);
        }
    }

    private void SaveActiveNoteCore(bool refreshList)
    {
        if (_activeNote is null) return;
        string body = EditorBox?.Text ?? string.Empty;
        var updated = _notesStore.SaveBody(_activeNote.FilePath, body);
        _activeNote = updated;
        _noteDirty = false;
        UpdateNoteChrome();

        if (refreshList)
        {
            string path = updated.FilePath;
            _suppressNotesSelection = true;
            try
            {
                var notes = _notesStore.ListNotes();
                if (NotesList is not null)
                {
                    NotesList.ItemsSource = notes;
                    var pick = notes.FirstOrDefault(n =>
                        string.Equals(n.FilePath, path, StringComparison.OrdinalIgnoreCase));
                    if (pick is not null)
                    {
                        NotesList.SelectedItem = pick;
                        _activeNote = pick;
                    }
                }
            }
            finally
            {
                _suppressNotesSelection = false;
            }
        }
    }

    // =========================================================================
    // Engine
    // =========================================================================

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
                return;
            }

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
                return;
            }

            SaveSettingsFromUi();
            var settings = _settings.ToNative();

            _lexiconPath = null;
            try
            {
                _lexiconPath = AppPaths.LexiconPath;
            }
            catch
            {
                _lexiconPath = null;
            }

            _engine?.Dispose();
            _engine = BispellEngine.Create(dictDir, _lexiconPath, settings);
            ErrorBar.IsOpen = false;
            // Product / app tokens are not misspellings (avoids popup on "BiSpell" in titles or sample text).
            EnsureBrandLexiconWords(_engine);
            RefreshLexiconLists();

            var settingsHint = AppPaths.SettingsPath;
            SetStatus(
                $"Engine ready — dicts: {dictDir} | settings: {settingsHint} | notes: {AppPaths.NotesDirectory}",
                0);
        }
        catch (DllNotFoundException ex)
        {
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
        }
        catch (BadImageFormatException ex)
        {
            ShowError(
                "Architecture mismatch",
                "bispell_core.dll architecture does not match this process (x64 vs x86/ARM64).\n" +
                "Rebuild the DLL for the same platform as the app.\n\n" + ex.Message);
            SetStatus("Engine not loaded — bad image format.", 0);
            RefreshLexiconLists();
        }
        catch (BispellException ex)
        {
            ShowError("Failed to load spell engine", ex.Message);
            SetStatus("Engine not loaded — see error bar.", 0);
            RefreshLexiconLists();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ShowError("Unexpected startup error", ex.Message);
            SetStatus("Engine not loaded — unexpected error.", 0);
            RefreshLexiconLists();
        }
    }

    /// <summary>
    /// Ensure product brand tokens are in the personal dictionary so they are never
    /// flagged (e.g. title "BiSpell" or note text mentioning the app).
    /// Idempotent: AddToDictionary is a no-op if already present.
    /// </summary>
    private static void EnsureBrandLexiconWords(BispellEngine engine)
    {
        // Known false positives from product chrome / help copy.
        string[] brand = { "BiSpell", "bispell", "BiSpellApp" };
        foreach (var w in brand)
        {
            try { engine.AddToDictionary(w); }
            catch (Exception ex)
            {
                CrashLog.Write($"EnsureBrandLexiconWords({w}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolve dictionary folder: app-relative Dictionaries first, then repo SoT paths.
    /// </summary>
    private static string? ResolveDictionaryDirectory()
    {
        var candidates = new List<string>();

        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Dictionaries"));

        var env = Environment.GetEnvironmentVariable("BISPELL_DICT_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Insert(0, env);

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

    // =========================================================================
    // Spell check
    // =========================================================================

    private void CheckButton_Click(object sender, RoutedEventArgs e) => RunCheck();

    private void RunCheck()
    {
        try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
        RunCheckCore(interactiveStatus: true, asYouType: false, wantPopup: false);
    }

    /// <summary>
    /// Shared spell-check path for F7 and as-you-type. Updates misspelling list; as-you-type
    /// with <paramref name="wantPopup"/> also auto-selects nearest miss and shows popup.
    /// Quiet as-you-type updates the list (and nearest selection) without opening the popup.
    /// </summary>
    private void RunCheckCore(bool interactiveStatus, bool asYouType, bool wantPopup)
    {
        if (_checkBusy) return;

        if (_engine is null)
        {
            if (asYouType) return;
            TryInitEngine();
            if (_engine is null)
            {
                SetStatus("Cannot check — engine not loaded.", 0);
                return;
            }
        }

        _checkBusy = true;
        try
        {
            ApplySettingsToEngine();

            var text = EditorBox?.Text ?? string.Empty;
            int caret = 0;
            try { caret = EditorBox?.SelectionStart ?? 0; }
            catch { caret = 0; }
            if (caret < 0) caret = 0;
            if (caret > text.Length) caret = text.Length;

            bool nearCaretOnly = asYouType
                && EditorSpellDebouncer.ShouldUseNearCaret(text.Length);
            int windowRadius = nearCaretOnly ? NearCaretWindowRadius : 120;

            IReadOnlyList<MisspellingItem> misspellings = _engine.Check(
                text,
                caretUtf16: asYouType || nearCaretOnly ? caret : -1,
                nearCaretOnly: nearCaretOnly,
                windowRadius: windowRadius);

            // Always suppress list side-effects that Select/Focus the editor while live-checking.
            _suppressMissListSideEffects = asYouType;
            try
            {
                if (MisspellingsList is not null)
                    MisspellingsList.ItemsSource = misspellings;

                if (asYouType)
                {
                    var nearest = FindNearestMisspelling(misspellings, caret);
                    _selectedMisspelling = nearest;

                    IReadOnlyList<string> suggestions = Array.Empty<string>();
                    if (nearest is not null)
                    {
                        suggestions = nearest.Suggestions;
                        if (suggestions.Count == 0 && _engine is not null && wantPopup)
                        {
                            // Only fetch extra suggestions when we will show the popup —
                            // mid-path list-only updates stay cheap.
                            try
                            {
                                suggestions = _engine.Suggestions(nearest.Word, nearest.Language);
                            }
                            catch (Exception ex)
                            {
                                _lastError = ex.Message;
                            }
                        }

                        // Do not set MisspellingsList.SelectedItem during as-you-type —
                        // selection can steal focus / feel like typing "stops".
                        // Keep _selectedMisspelling for popup apply only.
                    }

                    if (wantPopup && SuggestionsList is not null)
                    {
                        SuggestionsList.ItemsSource = suggestions.Count > 0 ? suggestions : null;
                        if (suggestions.Count > 0)
                            SuggestionsList.SelectedIndex = 0;
                    }

                    UpdateActionButtons();

                    int n = misspellings.Count;
                    if (!_settings.IsEnabled)
                    {
                        try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
                        SetStatus("Live: spell-check disabled — empty result.", 0);
                    }
                    else
                    {
                        if (wantPopup)
                        {
                            SetStatus(
                                n == 0
                                    ? "Live: no misspellings."
                                    : $"Live: {n} misspelling{(n == 1 ? "" : "s")}"
                                      + (nearCaretOnly ? " (near caret)" : "")
                                      + (nearest is not null ? $" — “{nearest.Word}”." : "."),
                                n);
                        }
                        // Quiet recheck: leave status alone so we don't thrash the bar while settling.

                        // Popup only on FullAsYouType (word boundary / replace), never on quiet.
                        if (wantPopup
                            && _settings.AsYouTypeEnabled
                            && nearest is not null
                            && suggestions.Count > 0
                            && !CrashLog.IsSmokeMode)
                        {
                            try { _suggestionPopup?.Show(nearest, suggestions); }
                            catch (Exception ex)
                            {
                                CrashLog.Write("as-you-type popup Show failed: " + ex.Message);
                            }
                        }
                        else if (!wantPopup)
                        {
                            // Quiet path: do not Hide if already open from a prior word boundary —
                            // only hide when we explicitly want no popup (e.g. after delete).
                            // After delete we already Hide() in TextChanged.
                        }
                        else
                        {
                            try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
                        }
                    }
                }
                else
                {
                    if (SuggestionsList is not null)
                        SuggestionsList.ItemsSource = null;
                    _selectedMisspelling = null;
                    UpdateActionButtons();

                    int n = misspellings.Count;
                    if (!_settings.IsEnabled)
                    {
                        SetStatus("Spell-check is disabled in settings — empty result.", 0);
                    }
                    else if (interactiveStatus)
                    {
                        SetStatus(n == 0
                            ? "No misspellings found."
                            : $"Found {n} misspelling{(n == 1 ? "" : "s")}. Select one to see suggestions.", n);
                    }
                }
            }
            finally
            {
                _suppressMissListSideEffects = false;
            }

            if (ErrorBar is not null)
                ErrorBar.IsOpen = false;
        }
        catch (BispellException ex)
        {
            if (interactiveStatus)
            {
                ShowError("Spell check failed", ex.Message);
                SetStatus("Check failed.", MisspellingsList?.Items?.Count ?? 0);
            }
            else
            {
                CrashLog.Write("as-you-type check failed: " + ex.Message);
                try { SetStatus("Live check failed (soft).", CountFromList()); }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            if (interactiveStatus)
            {
                ShowError("Spell check failed", ex.Message);
                SetStatus("Check failed.", 0);
            }
            else
            {
                CrashLog.Write("as-you-type check failed:");
                CrashLog.Write(ex);
            }
        }
        finally
        {
            _checkBusy = false;
        }
    }

    /// <summary>Debouncer fire target (UI thread). Uses <see cref="_asYouTypeWantPopup"/>.</summary>
    private void OnAsYouTypeFire()
    {
        if (CrashLog.IsSmokeMode) return;
        if (!_settings.AsYouTypeEnabled) return;
        if (_suppressAsYouType) return;
        if (_engine is null) return;
        if (!_settings.IsEnabled) return;

        bool wantPopup = _asYouTypeWantPopup;
        RunCheckCore(interactiveStatus: false, asYouType: true, wantPopup: wantPopup);
    }

    /// <summary>
    /// Schedule a debounced live check. Quiet = list only; Full = list + popup when ready.
    /// </summary>
    private void ScheduleAsYouTypeCheck(bool wantPopup, int? debounceOverrideMs = null)
    {
        if (CrashLog.IsSmokeMode) return;
        if (!_settings.AsYouTypeEnabled) return;
        if (_suppressAsYouType) return;
        if (_editorSpellDebouncer is null) return;
        if (_engine is null) return;
        if (!_settings.IsEnabled) return;

        _asYouTypeWantPopup = wantPopup;
        int ms = debounceOverrideMs ?? _settings.DebounceMilliseconds;
        _editorSpellDebouncer.Schedule(OnAsYouTypeFire, ms);
    }

    private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = EditorBox?.Text ?? string.Empty;
        int len = text.Length;

        // Track dirty note even when as-you-type / smoke skips scheduling.
        if (!_suppressAsYouType && _activeNote is not null)
        {
            _noteDirty = true;
            try
            {
                string newTitle = NotesStore.TitleFromBody(text);
                if (_activeNote.Title != newTitle)
                    _activeNote.Title = newTitle;
                UpdateNoteChrome();
            }
            catch { /* ignore */ }
        }

        // Smoke: never schedule as-you-type (debouncer also no-ops).
        if (CrashLog.IsSmokeMode) { _lastEditorTextLength = len; return; }
        if (!_settings.AsYouTypeEnabled) { _lastEditorTextLength = len; return; }
        if (_suppressAsYouType) { _lastEditorTextLength = len; return; }
        if (_engine is null || !_settings.IsEnabled) { _lastEditorTextLength = len; return; }

        int prev = _lastEditorTextLength;
        if (prev < 0)
            prev = len;

        try
        {
            if (len < prev)
            {
                // Delete / backspace: never spell-check mid-delete (UI-thread Check freezes typing).
                // Hide popup; after a long settle, quiet list-only recheck.
                try { _suggestionPopup?.Hide(); } catch { /* ignore */ }
                try { _editorSpellDebouncer?.Cancel(); } catch { /* ignore */ }
                int settle = Math.Max(_settings.DebounceMilliseconds, DeleteSettleMilliseconds);
                ScheduleAsYouTypeCheck(wantPopup: false, debounceOverrideMs: settle);
            }
            else if (len > prev)
            {
                char last = text[len - 1];
                // Mid-word typing: do NOT schedule spell check at all.
                // Synchronous Check() on the UI thread was freezing forward typing.
                // Only recheck when the user finishes a word (whitespace/punctuation).
                if (char.IsWhiteSpace(last) || char.IsPunctuation(last))
                {
                    ScheduleAsYouTypeCheck(wantPopup: true);
                }
                // letter/digit or other: no schedule — wait for word boundary or F7
            }
            else
            {
                // Same length (selection replace) → full after debounce.
                ScheduleAsYouTypeCheck(wantPopup: true);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("EditorBox_TextChanged schedule: " + ex.Message);
        }
        finally
        {
            _lastEditorTextLength = len;
        }
    }

    private void SuggestionPopup_SuggestionChosen(object? sender, string suggestion)
    {
        if (string.IsNullOrEmpty(suggestion)) return;

        var miss = _selectedMisspelling;
        if (miss is null)
        {
            SetStatus("No misspelling selected for popup apply.", CountFromList());
            return;
        }

        ApplySuggestionToMisspelling(miss, suggestion);
    }

    private static MisspellingItem? FindNearestMisspelling(
        IReadOnlyList<MisspellingItem> items,
        int caretUtf16)
    {
        if (items is null || items.Count == 0) return null;

        MisspellingItem? best = null;
        long bestDist = long.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            var m = items[i];
            int start = (int)m.Utf16Location;
            int end = start + (int)m.Utf16Length;
            long dist;
            if (caretUtf16 < start)
                dist = (long)start - caretUtf16;
            else if (caretUtf16 > end)
                dist = (long)caretUtf16 - end;
            else
                dist = 0;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = m;
            }
        }

        return best;
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

        if (!_suppressMissListSideEffects)
        {
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
        else
        {
            UpdateActionButtons();
        }
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
        if (TryHandlePopupKey(e))
            return;

        if (e.Key == VirtualKey.S && IsControlDown())
        {
            SaveActiveNoteManual();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F7)
        {
            RunCheck();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter && !IsEditorFocused())
        {
            if (SuggestionsList.Items.Count > 0)
            {
                ApplySelectedSuggestion();
                e.Handled = true;
            }
        }
    }

    private void EditorBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.S && IsControlDown())
        {
            SaveActiveNoteManual();
            e.Handled = true;
            return;
        }

        TryHandlePopupKey(e);
    }

    private static bool IsControlDown()
    {
        try
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }
        catch
        {
            return false;
        }
    }

    private bool TryHandlePopupKey(KeyRoutedEventArgs e)
    {
        if (_suggestionPopup is null || !_suggestionPopup.IsOpen)
            return false;

        try
        {
            if (_suggestionPopup.TryHandleKey(e.Key))
            {
                e.Handled = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("popup key: " + ex.Message);
        }

        return false;
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

        ApplySuggestionToMisspelling(_selectedMisspelling, suggestion);
    }

    private void ApplySuggestionToMisspelling(MisspellingItem miss, string suggestion)
    {
        if (miss is null || string.IsNullOrEmpty(suggestion))
        {
            SetStatus("No suggestion to apply.", CountFromList());
            return;
        }

        var text = EditorBox.Text ?? string.Empty;
        int start = (int)miss.Utf16Location;
        int len = (int)miss.Utf16Length;

        if (start < 0 || len < 0 || start + len > text.Length)
        {
            ShowError(
                "Invalid UTF-16 range",
                $"Cannot apply: range [{start}, {len}] is outside text length {text.Length}. Re-run Check.");
            return;
        }

        var current = text.Substring(start, len);
        if (!string.Equals(current, miss.Word, StringComparison.Ordinal))
        {
            SetStatus(
                $"Text changed under “{miss.Word}” (now “{current}”). Re-run Check.",
                CountFromList());
            return;
        }

        try { _suggestionPopup?.Hide(); } catch { /* ignore */ }

        _suppressAsYouType = true;
        try
        {
            EditorBox.Text = string.Concat(text.AsSpan(0, start), suggestion, text.AsSpan(start + len));
            _lastEditorTextLength = EditorBox.Text?.Length ?? 0;
            int newCaret = start + suggestion.Length;
            try
            {
                EditorBox.Select(newCaret, 0);
            }
            catch { /* ignore */ }
        }
        finally
        {
            _suppressAsYouType = false;
        }

        if (_activeNote is not null)
        {
            _noteDirty = true;
            try
            {
                _activeNote.Title = NotesStore.TitleFromBody(EditorBox.Text);
                UpdateNoteChrome();
            }
            catch { /* ignore */ }
        }

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
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Unignore failed", ex.Message);
        }
    }

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
