# BiSpell on Windows

Fork-friendly **parallel platform path**. macOS SwiftPM (`Sources/BiSpellCore`, `Sources/BiSpellApp`) stays the production product and is not rewritten for Windows.

Windows work lives under [`windows/`](../windows/). Details for implementers and fork maintainers.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  macOS (primary, unchanged)                                 │
│  Package.swift → BiSpellCore (Swift) → BiSpellApp (SwiftUI) │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  windows/                                                   │
│  assets/Dictionaries  ←── same .dic files as Swift bundle   │
│  core/  C++17  Tokenizer │ HunspellDictionary │ SpellEngine  │
│         LanguageTagger (heuristics) │ UserLexicon │ Settings │
│  tests/ ctest on Linux + Windows                            │
│  app/   C# WinUI 3 shell ──► P/Invokes bispell_core.dll     │
│         Check │ Suggest │ Apply │ Tray │ Settings           │
│         Global hotkey (Ctrl+Alt+.) │ Clipboard utility      │
└─────────────────────────────────────────────────────────────┘
```

- **Core**: C++17 library (static for tests; shared `bispell_core` for P/Invoke). No WinRT/Win32 UI headers in public APIs.
- **Ranges**: UTF-16 code unit offsets (parity with macOS `NSRange` for text APIs).
- **UI**: C# WinUI 3 shell for the spell loop — **not** a SwiftUI Notes / taxonomy / templates port.

## Stack choice

| Decision | Choice | Why |
|----------|--------|-----|
| Core language | C++17 + CMake | Same tests on Linux orchestrator and MSVC; static lib for tests; shared DLL for C# P/Invoke |
| UI (product path) | **C# WinUI 3** + P/Invoke | Thin host over `c_api.h`; unpackaged F5 on Windows; not WPF/WinForms/full SwiftUI clone |
| Dictionary engine | Algorithm parity with Swift `HunspellDictionary` | Stem word-list + restricted-edit suggestions; **not** full libhunspell affix expansion for MVP |
| System spell API | Optional later | No `NSSpellChecker` equivalent required for MVP |

**Future alternative (not a second product path):** a C++/WinRT shell could static-link `bispell_core` in one MSBuild solution if a single native binary is preferred later. That remains optional; do **not** maintain dual C# and C++/WinRT app trees.

## MVP vs non-goals

### In scope (Windows MVP)

1. Portable spell core reimplementing Swift logic for:
   - Models (`SpellLanguage`, `Misspelling`, ranges)
   - Tokenizer (TR/EN, skip rules)
   - Hunspell dictionary load (stem-stripped `.dic`), normalize, light stems, distance-1 suggestions
   - Language tagger **heuristics only**
   - SpellEngine (check, suggestions, lexicon hooks, settings gates, near-caret window)
   - UserLexicon + file store under `%APPDATA%\BiSpell\`
   - AppSettings subset (enabled, TR/EN, maxSuggestions, minWordLength, debounce)
2. Bundled TR + EN dictionaries (shared source with macOS assets).
3. WinUI 3 shell: multiline editor, run check, list misspellings, suggestions, apply, add/ignore, **lexicon manage** (remove / unignore), **min word length**, **persistent settings**, **system tray** (show / quit).
4. **Phase 2 clipboard utility:** global hotkey (**Ctrl+Alt+.**, fallback **Win+Shift+.**), optional **clipboard replace** after top-suggestion apply, shell toggles in settings (no system-wide UIA injection).
5. Docs and CMake so core tests run on Linux; app builds on a Windows host.

### Explicit non-goals

- Notes window, templates, locks, taxonomy, markdown library, terminal themes.
- System-wide Accessibility / UI Automation overlay in other apps (optional probe only later).
- WPF / WinForms / full SwiftUI-in-C# clone of the macOS UI.
- Full libhunspell affix engine (unless proven drop-in superior without breaking parity tests).
- macOS `SystemSpellSuggester` / `NSSpellChecker` equivalent on Windows MVP.
- Changing `Package.swift` platforms or deleting/renaming Swift sources.
- Requiring Linux CI to compile WinUI.

## Dictionary source path (single source of truth)

**Canonical SoT (only master copy):**

```
Sources/BiSpellCore/Resources/Dictionaries/
  en_US.aff
  en_US.dic
  tr.aff
  tr.dic
```

| Consumer | How it gets dicts |
|----------|-------------------|
| macOS Swift bundle | `Package.swift` resource copy of SoT |
| Linux/Windows `ctest` | CMake `configure_file` → `windows/build/tests/Dictionaries/` |
| C# WinUI app | `BiSpell.App.csproj` copies SoT into output `Dictionaries\` |
| `windows/assets/Dictionaries/` | Optional mirror only (gitignored `.dic`/`.aff`; CMake or `stage-dictionaries.ps1`) |

Do **not** hand-maintain two divergent dictionary blobs. Prefer SoT always; assets mirror is a packaging convenience, not a second master.

Licenses: same as root README — dictionaries from [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses). Retained for both macOS and Windows packaging.
## Build matrix

| What | Host | Toolchain | Notes |
|------|------|-----------|--------|
| macOS app + tests | macOS 14+ | SwiftPM | Unchanged; primary product |
| `bispell_core` + tests | Linux, macOS, Windows | CMake ≥ 3.20, C++17 (g++/clang++/MSVC) | Acceptance for core units |
| WinUI 3 shell | **Windows 10/11** | VS 2022, Windows App SDK, CMake/MSBuild | Not built on Linux |

### Build on Windows host — prerequisites

1. **Visual Studio 2022** with:
   - Desktop development with C++
   - Windows App SDK / WinUI workload (as offered by VS Installer)
2. **Windows App SDK** (matching the app project)
3. **CMake** ≥ 3.20 (VS bundled CMake is fine)
4. Windows 10 version 1809+ or Windows 11

### Suggested commands

**Portable core (any host with C++17) — clean clone:**

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

Expected: 6/6 tests pass. Optional CI: [`.github/workflows/windows-core.yml`](../.github/workflows/windows-core.yml) (Ubuntu, core only).

**WinUI app (Windows only) — C# P/Invoke host:**

```text
# 1) Native DLL (VS 2022 Developer PowerShell)
cd windows\app\scripts
.\build-native.ps1 -Platform x64 -Config Release

# 2) Open windows/app/BiSpell.sln → set BiSpell.App, Debug|x64 → F5 (unpackaged)
#    or: dotnet build windows\app\BiSpell.App\BiSpell.App.csproj -c Release -p:Platform=x64
# Full steps: windows/README.md
# MVP smoke checklist: docs/WINDOWS_PHASES.md
```

**macOS (primary product — unchanged):**

```bash
swift test
swift build -c release
```

`Package.swift` and `Sources/` are not modified by the Windows path.
## Layout under `windows/`

| Path | Role |
|------|------|
| `windows/core/` | Portable C++ spell library (`include/bispell/`, `src/`) |
| `windows/app/` | WinUI 3 shell |
| `windows/assets/Dictionaries/` | Build-copied or staged dicts (source of truth remains Swift Resources) |
| `windows/tests/` | Core unit tests |
| `windows/CMakeLists.txt` | Top-level: core + tests **always**; C# app documented / `if(WIN32)` status only (not CMake-built) |
| `windows/README.md` | Windows-facing quick start |
| `docs/WINDOWS_PHASES.md` | MVP unit checklist (U1–U7) + clean-clone / Windows smoke |

## Fork / branch notes

- Keep **`main`** (or your primary branch) **macOS-clean**: Swift tree untouched; Windows is additive under `windows/` + docs.
- Optional long-lived branch name: `windows-platform` for Windows-focused work; merge docs/`windows/` without stripping `Sources/`.
- Optional: publish a GitHub fork for Windows packaging; not required for in-tree development.
- **Do not** delete or rename Swift sources in ways that break `swift build` / `Package.swift`.
- Implementers: stay inside `windows/` for code; document-only links to Swift for algorithm reference.

## User data (`%APPDATA%\BiSpell\`)

| File | Role |
|------|------|
| `settings.json` | Spell settings subset (`isEnabled`, `turkishEnabled`, `englishEnabled`, `maxSuggestions`, `minWordLength`, `debounceMilliseconds`) plus shell-only `globalHotkeyEnabled`, `clipboardReplaceEnabled` (default true). Loaded at startup; saved when toggles change. |
| `user-lexicon.json` | Personal dictionary + ignored words (engine `UserLexiconStore`). Survives relaunch. |

Full path examples (per-user profile):

- `%APPDATA%\BiSpell\settings.json`
- `%APPDATA%\BiSpell\user-lexicon.json`

C++ path helpers: `bispell::paths::default_config_dir()`, `default_settings_path()`, `default_lexicon_path()` (Windows → `%APPDATA%\BiSpell\`; injectable override for tests).

C# mirrors the same locations via `BiSpell.Services.AppPaths` / `SettingsStore`. Status line shows dict / settings / lexicon paths when the engine loads.

### Phase 1 shell features (settings + lexicon manage)

**Min word length** — settings card NumberBox (1–10, default 2). Tokens shorter than this are skipped on check; value is written to `settings.json` as `minWordLength` and applied via `UpdateSettings`.

**Lexicon manage UI** — collapsible expander **Personal dictionary & ignored words** on the main window (inline lists; **no** modal at startup, so GHA `smoke-launch.ps1` / `BISPELL_SMOKE=1` never hangs on a dialog).

| Action | UI | Effect |
|--------|-----|--------|
| Add | Misspelling selected → **Add to dictionary** | Word enters personal dict; re-check no longer flags it; appears under **Dictionary** |
| Ignore | Misspelling selected → **Ignore** | Word enters ignore list; re-check skips it; appears under **Ignored** |
| **Remove selected** | Select a **Dictionary** row → button | `RemoveFromDictionary`; re-check can flag the word again |
| **Unignore selected** | Select an **Ignored** row → button | `UnignoreWord`; re-check can flag the word again |

Lists are live from the engine (`ListAddedWords` / `ListIgnoredWords`), not a raw disk re-read only. Empty / engine-offline headers keep buttons disabled without crashing.

### Persistence smoke tests (Windows host)

**Settings across relaunch**

1. Launch BiSpell, note status line shows paths under `%APPDATA%\BiSpell\`.
2. Uncheck **Turkish** (leave English on), set **Max suggestions** to `3`, set **Min word length** to e.g. `4`, optionally uncheck **Spell-check enabled**.
3. Quit via tray **Quit** (or close then Quit).
4. Confirm `%APPDATA%\BiSpell\settings.json` contains the chosen values (including `minWordLength`).
5. Relaunch → UI toggles match; Check with `isEnabled=false` returns no misspellings; with languages restored, max suggestions caps the list; short tokens below min length are skipped.

**Lexicon across relaunch**

1. Paste a nonsense token (e.g. `BiSpellPersistXYZ`) and Check → it is flagged.
2. Select it → **Add to dictionary** (confirm it appears in the Dictionary list).
3. Quit and relaunch (same user profile).
4. Paste the same token → Check → **not** flagged; Dictionary list still shows the word.
5. Confirm `%APPDATA%\BiSpell\user-lexicon.json` lists the word under `addedWords` (or equivalent).
6. **Remove selected** → re-check → word flagged again; **Ignore** then **Unignore selected** similarly.

### Tray (notification area)

- Unpackaged WinUI has no first-party tray control; the shell uses **WinForms `NotifyIcon`** (`UseWindowsForms` in the csproj).
- Menu: **Show BiSpell** (activate main window), **Quit** (dispose tray + exit).
- Double-click tray icon = show window.
- Closing the main window **hides to tray** (does not quit); use **Quit** to exit cleanly.
- Tray **balloon** (or tip fallback) reports clipboard-utility results (Phase 2).
- Still **no** system-wide other-app injection / UI Automation overlay (out of MVP; optional later probe only). Clipboard utility is **copy → hotkey → paste**, not in-place UIA rewrite of foreign apps.

### Phase 2 — global hotkey & clipboard utility

Shell-only feature (not part of the native C ABI). Uses Win32 `RegisterHotKey` on a message-only HWND and `CF_UNICODETEXT` clipboard read/write.

| Piece | Behavior |
|-------|----------|
| **Primary hotkey** | **Ctrl+Alt+.** (`MOD_CONTROL\|MOD_ALT`, `VK_OEM_PERIOD`) |
| **Fallback** | **Win+Shift+.** if primary registration fails |
| **Global hotkey** checkbox | Default on; toggle re-registers / unregisters **without restart** |
| **Clipboard replace** checkbox | Default on; when off, check + feedback only (clipboard not overwritten) |
| Settings JSON | `globalHotkeyEnabled`, `clipboardReplaceEnabled` (missing keys → true) |

**How to use**

1. **Copy** text with typos from any app.
2. Press the registered combo (**Ctrl+Alt+.** or the caption’s fallback).
3. BiSpell runs the full-field check with current languages/settings, applies top suggestions per misspelling, and (if replace is on) writes fixed text to the clipboard.
4. **Paste** the corrected text.

Caption under the settings toggles shows the live binding or a reason it is off / unavailable / skipped in smoke.

**Headless smoke:** `windows/app/scripts/smoke-launch.ps1` sets **`BISPELL_SMOKE=1`** before launch. The app then **skips hotkey registration** (and MessageBox) so GHA `smoke-windows-x64` never depends on interactive hotkeys or blocks on modals. Manual hotkey E2E remains a Windows-host checklist item only.

## Status

**Windows MVP (U1–U5 + U7) is integration-complete in-tree; Phase 2 clipboard utility is in-tree.** Checklist: [`docs/WINDOWS_PHASES.md`](WINDOWS_PHASES.md).

| Layer | State |
|-------|--------|
| C++ core + `ctest` + C ABI | ✅ Linux-verified (`bispell_core_tests`) |
| C# WinUI 3 shell (check / suggest / apply) | ✅ Source complete; binary smoke on **Windows host** |
| Settings + tray + AppData | ✅ U5 |
| Phase 1: min word length + lexicon manage (remove/unignore) | ✅ P1-SETTINGS / P1-LEXUI |
| Phase 2: global hotkey + clipboard replace + GLUE | ✅ P2-SETTINGS / P2-HOTKEY / P2-CLIP / P2-GLUE (`BISPELL_SMOKE` skips hotkey) |
| Dictionary SoT packaging | ✅ Swift Resources → CMake stage + csproj copy |
| CI (Linux core only) | ✅ `.github/workflows/windows-core.yml` |
| Windows release zip + smoke | ✅ `.github/workflows/windows-release.yml` |
| macOS Swift product | ✅ Unchanged |
| U6 UIA probe | ⬜ Optional post-MVP |

**Environment note:** full WinUI binary smoke (build + F5 on Windows) is **not** run on the Linux orchestrator or the `windows-core` GitHub Action; use the manual checklist in [`WINDOWS_PHASES.md`](WINDOWS_PHASES.md) on a Windows host with VS 2022 + Windows App SDK.
