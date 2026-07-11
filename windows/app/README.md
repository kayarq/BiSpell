# BiSpell.App — C# WinUI 3 shell

Thin **WinUI 3** host that P/Invokes `bispell_core.dll` (C ABI in `windows/core/include/bispell/c_api.h`).

This is a **spell-check shell only** — not a port of macOS Notes / templates / taxonomy.

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
    AppPaths.cs          ← %APPDATA%\BiSpell\ paths
    SettingsStore.cs     ← settings.json load/save
    TrayIconService.cs   ← WinForms NotifyIcon (show / quit)
  Models/MisspellingItem.cs
  MainWindow.xaml(.cs)   ← check / list / suggest / apply / add / ignore / settings
  App.xaml(.cs)          ← tray lifecycle (hide-to-tray, Quit)
native/                  ← staged bispell_core.dll
scripts/                 ← build-native, stage-dictionaries
```

## UX polish (mandate)

- Status line + misspelling count badge
- Double-click suggestion (or misspelling) to apply
- **F7** check; **Enter** apply top/selected suggestion
- Settings bar: enable, TR/EN, max suggestions → `%APPDATA%\BiSpell\settings.json`
- Tray: Show BiSpell / Quit; close window hides to tray

## Diagnostic: v0.1.3 `ToggleButton.IsChecked` crash (W1 fix)

**Exception:** `Failed to assign to property 'Microsoft.UI.Xaml.Controls.Primitives.ToggleButton.IsChecked'. [Line: 0 Position: 0]` during `MainWindow` construction (compiled XAML → Line/Position 0 is normal, not a missing file).

**Root cause (confirmed by code path):** During `InitializeComponent()`, XAML applied `IsChecked="True"` on `EnabledCheck` / `TurkishCheck` / `EnglishCheck` while `Checked`/`Unchecked` were already wired to `Settings_Changed`. That handler called language-guard logic and/or `SaveSettingsFromUi`, touching sibling controls not yet constructed (XAML order: Enabled → Turkish → English → NumberBox). The throw mid-property-assign is wrapped by WinUI as a `ToggleButton.IsChecked` assign failure. This is **not** a native `bispell_core.dll` fault (that would be `DllNotFoundException` after deferred engine init).

**Why 0.1.3 hit this next:** Bootstrap was already off + self-contained packaging; the Runtime popup was gone, so the process reached real WinUI window construction and the settings-strip race.

**Fix (Mandate B — XAML-minimal / code-driven state):**
- XAML settings strip is structure-only: no `IsChecked`, no `Checked`/`Unchecked`, no NumberBox `Value`/`ValueChanged`.
- Ctor: `InitializeComponent` → `LoadSettingsIntoUi` (assign `(bool?)` / max suggestions) → `WireSettingsHandlers` → clear suppress.
- Product rules unchanged: persist to `%APPDATA%\BiSpell\settings.json`, force at least one of TR/EN, max suggestions 1–20.
- **Control choice:** kept `CheckBox` (not `ToggleSwitch`) for layout parity with the MVP strip; non-nullable `IsOn` was not needed once events are post-init.
- Did **not** re-enable `WindowsAppSdkBootstrapInitialize=true` or `UseWindowsForms=true`.
