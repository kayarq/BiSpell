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
  Models/MisspellingItem.cs
  MainWindow.xaml(.cs)   ← check / list / suggest / apply / add / ignore
  App.xaml(.cs)
native/                  ← staged bispell_core.dll
scripts/                 ← build-native, stage-dictionaries
```

## UX polish (mandate)

- Status line + misspelling count badge
- Double-click suggestion (or misspelling) to apply
- **F7** check; **Enter** apply top/selected suggestion
