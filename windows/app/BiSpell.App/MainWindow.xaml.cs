using BiSpell.Interop;
using BiSpell.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace BiSpell;

/// <summary>
/// MVP spell shell: check → list misspellings → suggestions → apply via UTF-16 ranges.
/// Keyboard: F7 = check, Enter on suggestions = apply top/selected.
/// Double-click suggestion (or misspelling with a top suggestion) applies.
/// </summary>
public sealed partial class MainWindow : Window
{
    private BispellEngine? _engine;
    private MisspellingItem? _selectedMisspelling;
    private string? _lastError;

    public MainWindow()
    {
        InitializeComponent();
        Title = "BiSpell — Spell Check";
        TryInitEngine();
        Closed += (_, _) =>
        {
            _engine?.Dispose();
            _engine = null;
        };
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
                return;
            }

            var settings = BispellSettings.CreateDefault();
            settings.TurkishEnabled = TurkishCheck.IsChecked == true ? 1 : 0;
            settings.EnglishEnabled = EnglishCheck.IsChecked == true ? 1 : 0;

            string? lexiconPath = null;
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    var dir = Path.Combine(appData, "BiSpell");
                    Directory.CreateDirectory(dir);
                    lexiconPath = Path.Combine(dir, "user-lexicon.json");
                }
            }
            catch
            {
                // Memory-only lexicon if AppData unavailable.
                lexiconPath = null;
            }

            _engine?.Dispose();
            _engine = BispellEngine.Create(dictDir, lexiconPath, settings);
            ErrorBar.IsOpen = false;
            SetStatus($"Engine ready — dictionaries: {dictDir}", 0);
        }
        catch (DllNotFoundException ex)
        {
            ShowError(
                "Native library not found (bispell_core.dll)",
                "P/Invoke could not load bispell_core.dll.\n" +
                "Build the shared core with CMake (target bispell_core_shared) and copy " +
                "bispell_core.dll next to BiSpell.App.exe, or into windows/app/native/x64/.\n\n" +
                "See windows/README.md — “Build native DLL”.\n\n" +
                ex.Message);
            SetStatus("Engine not loaded — bispell_core.dll missing.", 0);
        }
        catch (BadImageFormatException ex)
        {
            ShowError(
                "Architecture mismatch",
                "bispell_core.dll architecture does not match this process (x64 vs x86/ARM64).\n" +
                "Rebuild the DLL for the same platform as the app.\n\n" + ex.Message);
            SetStatus("Engine not loaded — bad image format.", 0);
        }
        catch (BispellException ex)
        {
            ShowError("Failed to load spell engine", ex.Message);
            SetStatus("Engine not loaded — see error bar.", 0);
        }
        catch (Exception ex)
        {
            ShowError("Unexpected startup error", ex.Message);
            SetStatus("Engine not loaded — unexpected error.", 0);
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
            var text = EditorBox.Text ?? string.Empty;
            var misspellings = _engine.Check(text);
            MisspellingsList.ItemsSource = misspellings;
            SuggestionsList.ItemsSource = null;
            _selectedMisspelling = null;
            UpdateActionButtons();

            int n = misspellings.Count;
            SetStatus(n == 0
                ? "No misspellings found."
                : $"Found {n} misspelling{(n == 1 ? "" : "s")}. Select one to see suggestions.", n);
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
            SetStatus($"Added “{word}” to personal dictionary. Re-checking…", CountFromList());
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
            SetStatus($"Ignoring “{word}” for this session/lexicon. Re-checking…", CountFromList());
            RunCheck();
        }
        catch (Exception ex)
        {
            ShowError("Ignore failed", ex.Message);
        }
    }

    private void LanguageToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        try
        {
            var s = BispellSettings.CreateDefault();
            s.TurkishEnabled = TurkishCheck.IsChecked == true ? 1 : 0;
            s.EnglishEnabled = EnglishCheck.IsChecked == true ? 1 : 0;
            // Keep at least one language on to avoid empty checks looking like errors.
            if (s.TurkishEnabled == 0 && s.EnglishEnabled == 0)
            {
                s.EnglishEnabled = 1;
                EnglishCheck.IsChecked = true;
            }
            _engine.UpdateSettings(s);
            SetStatus("Language settings updated. Press F7 to re-check.", CountFromList());
        }
        catch (Exception ex)
        {
            ShowError("Settings update failed", ex.Message);
        }
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
