# BiSpell.App — C# WinUI 3 shell

Thin **WinUI 3** host that P/Invokes `bispell_core.dll` (C ABI in `windows/core/include/bispell/c_api.h`).

**In-app Notes + spell-check** — not a port of macOS Notes templates/taxonomy, and **not** system-wide hotkey/UIA injection.

## Quick start (Windows)

1. Build native DLL: `scripts\build-native.ps1`
2. Open `BiSpell.sln` in VS 2022 → F5 (x64, unpackaged)

Full steps: [`../README.md`](../README.md).

## Project layout

```
BiSpell.App/
  Interop/
    NativeMethods.cs     ← DllImport of every c_api.h symbol used
    BispellEngine.cs     ← managed RAII wrapper + result marshaling
    NativeString.cs      ← UTF-8 helpers
  Services/
    AppPaths.cs                 ← %APPDATA%\BiSpell\ paths (+ Notes\)
    SettingsStore.cs            ← settings.json load/save
    NotesStore.cs               ← plain-text notes under Notes\
    EditorSpellDebouncer.cs     ← as-you-type debounce (DispatcherQueueTimer)
    TrayIconService.cs          ← Win32 Shell_NotifyIcon (show / quit)
  UI/
    SuggestionPopupController.cs← as-you-type suggestion Popup (Enter/1–5/Esc)
  Utilities/
    ClipboardSpellFix.cs        ← pure batch top-suggestion apply (no clipboard IO)
  Models/MisspellingItem.cs
  MainWindow.xaml(.cs)   ← notes sidebar + editor check / list / suggest / apply / settings
  App.xaml(.cs)          ← tray lifecycle (no global hotkey)
native/                  ← staged bispell_core.dll
scripts/                 ← build-native, stage-dictionaries
```

## UX

- Notes sidebar: New / Delete / select; title = first non-empty line
- Note editor with **F7** check, misspellings list, suggestions, apply
- **As-you-type** (default on): length-aware debounce — delete settles quietly; popup on word boundary
- Settings: enable, TR/EN, max suggestions, min word length, debounce, as-you-type → `%APPDATA%\BiSpell\settings.json`
- Notes files: `%APPDATA%\BiSpell\Notes\*.txt` (Ctrl+S save; auto-save on switch)
- Tray: Show BiSpell / Quit; close window hides to tray

## Diagnostic: v0.1.3 `ToggleButton.IsChecked` crash (W1 fix)

Settings strip is structure-only in XAML (no `IsChecked` / handlers). Ctor: `InitializeComponent` → `LoadSettingsIntoUi` → `WireSettingsHandlers`.
