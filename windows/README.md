# BiSpell — Windows

Parallel **Windows** path for BiSpell. The macOS Swift product under `Sources/` is **unchanged** and remains the primary platform.

This tree hosts a portable C++ spell core (algorithm parity with Swift `BiSpellCore`) and a **thin C# WinUI 3** shell that P/Invokes the core C ABI for check / suggest / apply. It is **not** a port of the SwiftUI Notes UI.

## Stack

| Layer | Choice | Notes |
|--------|--------|--------|
| Spell core | **C++17** + CMake | Portable; builds on Linux CI and MSVC |
| C ABI | `windows/core/include/bispell/c_api.h` | Stable exports for P/Invoke |
| Static lib | `bispell_core` (always) | Linked by `ctest` on Linux/Windows; optional C++ hosts |
| Shared lib | `bispell_core.dll` (`bispell_core_shared`, `BISPELL_BUILD_SHARED`) | Loaded by C# host; optional (`-DBISPELL_BUILD_SHARED=OFF`) |
| UI shell | **C# WinUI 3** (Windows App SDK) | **Product path** — unpackaged F5; XAML ergonomics |
| Dictionaries | Same `.dic` / `.aff` as macOS | **SoT:** `Sources/BiSpellCore/Resources/Dictionaries/` |
| User data | `%APPDATA%\BiSpell\` | `settings.json`, `user-lexicon.json` |
| Tray | WinForms `NotifyIcon` | Show window / Quit (unpackaged); balloon on clipboard utility |
| Global hotkey | Win32 `RegisterHotKey` | **Ctrl+Alt+.** primary; **Win+Shift+.** fallback; UIA-first + clipboard utility |
| UIA assist (Phase 3) | Soft-fail COM `CUIAutomation` | ValuePattern read/write on focused control; Tier A/B/C; clipboard fallback |
| As-you-type (Phase 4) | Editor-only debounce + popup | `asYouTypeEnabled` + `debounceMilliseconds`; **not** system-wide |

**Future alternative (not dual product path):** a C++/WinRT shell could static-link `bispell_core` in one MSBuild solution later. Keep a single app tree under `windows/app/` (C# today); do not maintain a parallel incomplete C++/WinRT product.

### Relation to Swift `BiSpellCore`

| Swift (`Sources/BiSpellCore`) | Windows (`windows/core`) |
|------------------------------|---------------------------|
| `Models`, `Tokenizer` | Ported types + tokenizer |
| `HunspellDictionary` | Same stem-list / restricted-edit behavior (not full affix engine) |
| `LanguageTagger` | Heuristics only (no Apple NaturalLanguage) |
| `SpellEngine`, `UserLexicon`, settings subset | Ported; exposed via C ABI |
| Notes / AX / overlay / templates | **Out of scope** for Windows MVP |

## Layout

```
windows/
  README.md                 ← this file
  CMakeLists.txt            ← core + tests (+ shared DLL option)
  core/                     ← portable C++ spell library
    include/bispell/c_api.h ← P/Invoke contract
    src/
  app/                      ← C# WinUI 3 shell (Windows host only)
    BiSpell.sln
    BiSpell.App/            ← WinUI project + Interop/ + Services/ (settings, tray)
    native/                 ← staged bispell_core.dll (build output; gitignored)
    scripts/                ← build-native.ps1, stage-dictionaries.ps1
  assets/Dictionaries/      ← optional CMake/manual mirror of SoT
  tests/                    ← core unit tests (Linux + Windows)
```

### Dictionaries (source of truth vs mirrors)

| Location | Role |
|----------|------|
| `Sources/BiSpellCore/Resources/Dictionaries/` | **Source of truth (SoT)** |
| `windows/build/tests/Dictionaries/` | CMake stage for `ctest` |
| App output `Dictionaries/` | Copied by `BiSpell.App.csproj` from SoT at build |
| `windows/assets/Dictionaries/` | Optional packaging mirror |

Do not hand-edit blobs under `assets/` as the primary copy.

---

## Prebuilt release (no VS/CMake on C:)

WinUI cannot be compiled on Linux, and a full Visual Studio install is large. Prefer a **GitHub Actions** package:

| Step | Action |
|------|--------|
| 1 | Download `BiSpell-*-win-x64.zip` from the repo **Releases** |
| 2 | Unzip anywhere |
| 3 | Run `BiSpell.App.exe` |

CI workflow: [`.github/workflows/windows-release.yml`](../.github/workflows/windows-release.yml)  
Manual: **Actions → windows-release → Run workflow** (needs push access to the fork).  
Local equivalent (Windows only): `windows/app/scripts/package-release.ps1`.

---

## Build

### 1. Portable core + tests (Linux, macOS, or Windows)

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

Shared library (for local smoke of the C ABI):

```bash
cmake --build windows/build --target bispell_core_shared
```

Requires C++17 (g++/clang++/MSVC) and CMake ≥ 3.20.

### 2. Native DLL for the C# host (Windows)

**Prerequisites:** Visual Studio 2022 with **Desktop development with C++**, CMake.

In **x64 Native Tools** or **Developer PowerShell for VS 2022**:

```powershell
cd windows\app\scripts
.\build-native.ps1 -Platform x64 -Config Release
```

This configures CMake with `-DBISPELL_BUILD_SHARED=ON`, builds `bispell_core_shared`, and stages:

```
windows/app/native/x64/bispell_core.dll
windows/app/native/bispell_core.dll
```

Manual equivalent:

```bat
cmake -S windows -B windows\build-msvc-x64 -G "Visual Studio 17 2022" -A x64 -DBISPELL_BUILD_SHARED=ON
cmake --build windows\build-msvc-x64 --config Release --target bispell_core_shared
mkdir windows\app\native\x64 2>nul
copy /Y windows\build-msvc-x64\core\Release\bispell_core.dll windows\app\native\x64\
```

The DLL must match the app platform (x64 app ↔ x64 DLL).

### 3. C# WinUI 3 app (Windows host only)

**Prerequisites**

- Windows 10 version 1809+ or Windows 11
- **Visual Studio 2022** with:
  - .NET desktop development
  - Windows application development / Windows App SDK (WinUI) workload
  - Desktop development with C++ (for the native DLL)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows App SDK runtime (usually installed with the workload; self-contained publish is enabled in the project)

**Steps**

1. Build and stage `bispell_core.dll` (section 2).
2. Open `windows/app/BiSpell.sln` in Visual Studio 2022.
3. Set configuration **Debug|x64** (or Release|x64).
4. Set startup project **BiSpell.App**.
5. **F5** — the app is configured as **unpackaged** (`WindowsPackageType=None`) for local dev.
6. Dictionaries are copied from SoT into the output `Dictionaries\` folder by the csproj.

**CLI build** (after native DLL is staged):

```bat
dotnet restore windows\app\BiSpell.App\BiSpell.App.csproj
dotnet build windows\app\BiSpell.App\BiSpell.App.csproj -c Release -p:Platform=x64
```

Output lands under `windows/app/BiSpell.App/bin/...`. Ensure `bispell_core.dll` and `Dictionaries\` sit next to the `.exe`.

**Optional:** stage a dictionary mirror:

```powershell
.\windows\app\scripts\stage-dictionaries.ps1
```

### Packaging note

| Mode | Setting | Use |
|------|---------|-----|
| **Unpackaged** (default) | `WindowsPackageType=None` | Local F5 / `dotnet build` |
| MSIX | Enable packaging in VS / set package type | Store or sideload (not required for MVP) |

Linux CI / orchestrator does **not** build WinUI.

---

## App usage (MVP smoke)

1. Launch BiSpell.
2. Confirm status line shows engine ready (or a clear error if DLL/dicts missing).
3. Paste: `I recieve mail. merhabaa dünya`
4. Press **F7** or **Check** → expect `recieve`, `merhabaa` in the misspellings list; status badge shows count.
5. Select `recieve` → suggestions (ideally include `receive`).
6. **Enter** or **double-click** a suggestion → text updated at the **UTF-16** range; auto re-check.
7. **Add to dictionary** / **Ignore** on a nonsense token → re-check no longer flags it; word appears in the lexicon panel (below).
8. Toggle **Spell-check enabled** / TR/EN / **Max suggestions** / **Min word length**; re-check. Quit and relaunch → settings still applied.
9. Tray icon: right-click → **Show BiSpell** / **Quit**. Closing the window hides to tray (not exit).

### Settings (Phase 1 + Phase 2)

Settings card on the main window (persisted to AppData):

| Control | Effect |
|---------|--------|
| **Spell-check enabled** | Master gate for check |
| **Turkish** / **English** | Language dictionaries |
| **Max suggestions** | Caps suggestion list (1–20) |
| **Min word length** | Tokens shorter than this are **skipped** on check (1–10, default 2). Raise to reduce noise on short tokens; lower to include 1-letter words. |
| **Global hotkey** | Shell-only. When on (default), register the utility hotkey (no app restart). Off → unregister. |
| **Clipboard replace** | Shell-only. When on (default), write fixed text back to the **clipboard** on fallback / Tier B clipboard write. For **UIA SetValue**, same switch is “auto-apply”: when **off**, UIA may still **read + check** but must **not** SetValue (status / window for review). |
| **UIA assist** | Shell-only. When on (default), try **focused-control UIA** (ValuePattern) before the Phase 2 clipboard path. Off → pure clipboard utility. |
| **As-you-type check** | Shell-only. When on (default), debounced live check in the **main editor** only (not other apps). Off → F7 / utility hotkey still work. JSON: `asYouTypeEnabled`. |
| **Debounce (ms)** | Wait after typing before re-checking (0–5000, default **250**). JSON: `debounceMilliseconds`. Also pushed to native settings. |

A caption under the utility toggles shows the **active binding** (e.g. `Ctrl+Alt+.`) or why registration failed / was skipped.

### Utility hotkey (Phase 2 + Phase 3)

System-wide hotkey: **UIA-first** (when **UIA assist** is on), then **clipboard** fallback (Phase 2).

| Binding | Role |
|---------|------|
| **Ctrl+Alt+.** | Primary |
| **Win+Shift+.** | Fallback if primary `RegisterHotKey` fails (another app owns the combo) |

#### Support tiers (A / B / C)

| Tier | Meaning | Typical apps | Hotkey behavior |
|------|---------|--------------|-----------------|
| **A** | ValuePattern **read + write** | Classic Notepad, many Win32 `EDIT` controls | In-place `SetValue` of corrected text (when Clipboard replace / auto-apply is on) |
| **B** | ValuePattern **read only** (or write fails) | Some rich / restricted fields | Read + spell; write via **clipboard** if replace is on (user pastes) |
| **C** | No UIA access | Many Chromium / Electron fields, elevated apps, password, no focus | **Identical** Phase 2 clipboard path (copy → hotkey → paste) |

**Not** continuous multi-app monitoring and **not** system-wide overlay underlines — utility is hotkey-triggered only. (Phase 4 as-you-type is **in-app editor only**.)

#### How to use

**Tier A (Notepad-style)**

1. Focus an edit field with typos (e.g. `I recieve mail. merhabaa dünya`).
2. Press the global hotkey (**Ctrl+Alt+.** or fallback).
3. With **UIA assist** + **Clipboard replace** on, the control is corrected **in place**; tray/status report the UIA path. Clipboard may stay untouched.

**Tier C / UIA off (Phase 2 clipboard)**

1. **Copy** text that has typos (any app).
2. Press the global hotkey.
3. BiSpell checks clipboard text, applies top suggestions, and — if **Clipboard replace** is on — writes fixed text back.
4. **Paste** where you want the corrected text.

**Probe focused control** (optional): button under the utility settings. Logs tier, control type, name, process, read/write capability to the status line and `CrashLog` (no modal). Useful when BiSpell is not focused — focus the target app first, then Alt-Tab and click Probe (or use while another window still holds focus if the probe runs before focus steals). Disabled when the engine is offline or in smoke mode.

#### Permissions / limitations

- **No admin required** for basic ValuePattern on same-integrity desktop apps.
- Elevated / higher-integrity targets may be inaccessible → soft Tier C → clipboard.
- Password / `IsPassword` fields are **never** read or written.
- Some UWP / Chromium apps expose little or no ValuePattern → honest **Tier C**.
- Self-focus (BiSpell’s own UI focused): UIA write of “foreign” fields is skipped; orchestrator prefers main editor text or clipboard.

Feedback: status line + tray balloon (path label `UIA` / `clipboard`, tier when useful). Empty clipboard, spell-check off, or zero misspellings get a short status/balloon and no write.

**Smoke / CI:** when `BISPELL_SMOKE=1` (set by `windows/app/scripts/smoke-launch.ps1` for GHA and local zip smoke), the app **never registers** the hotkey, **skips UIA COM** on utility/probe paths, **no-ops as-you-type debounce timers** (Phase 4), and skips MessageBox modals so headless launch cannot hang. Manual UIA/hotkey / as-you-type E2E is for interactive Windows only.

### As-you-type (Phase 4 — editor only)

While typing in the main editor (with **As-you-type check** on):

1. After **Debounce (ms)** of quiet typing, BiSpell re-checks the editor text (full document, or near-caret for large text).
2. The misspellings list updates; a **suggestion popup** appears for the miss nearest the caret.
3. **Enter** / **1–5** apply a suggestion; **Esc** dismisses. **F7** still runs an immediate full check.
4. Uncheck as-you-type to disable live checks without affecting the global hotkey.

This is **not** system-wide continuous monitoring — other apps still use Phase 2/3 hotkey + UIA/clipboard.

### Lexicon manage UI (Phase 1)

Collapsible expander **Personal dictionary & ignored words** under the check layout (live lists from the engine; not a startup modal — safe for headless smoke).

| List | How words get there | Manage action |
|------|---------------------|---------------|
| **Dictionary** | Select a misspelling → **Add to dictionary** | Select row → **Remove selected** → word can flag again on re-check |
| **Ignored** | Select a misspelling → **Ignore** | Select row → **Unignore selected** → word can flag again on re-check |

Remove / Unignore refresh the lists and re-run check immediately. File-backed lexicon survives relaunch under AppData (below).

### Persistence (AppData)

| Path | Contents |
|------|----------|
| `%APPDATA%\BiSpell\settings.json` | `isEnabled`, TR/EN, `maxSuggestions`, `minWordLength`, `debounceMilliseconds`, plus shell-only `globalHotkeyEnabled`, `clipboardReplaceEnabled`, `uiaAssistEnabled`, `asYouTypeEnabled` (utility + as-you-type flags default **true** when missing) |
| `%APPDATA%\BiSpell\user-lexicon.json` | Personal dictionary (`addedWords`) + ignore list (`ignoredWords`) |

- **Settings relaunch:** change Min word length / languages → quit → relaunch → UI and check behavior match.
- **Lexicon relaunch:** Add `BiSpellPersistXYZ` → quit → relaunch → same word not flagged; JSON lists it under `addedWords`.

Full steps: [`docs/WINDOWS.md`](../docs/WINDOWS.md) → *User data* / *Persistence smoke tests*.

### Tray

- Notification-area icon via WinForms `NotifyIcon` (no system-wide injection).
- **Show BiSpell** / double-click: bring main window forward.
- **Quit**: dispose tray and exit process.

### Keyboard

| Key | Action |
|-----|--------|
| **F7** | Run spell-check on the main editor |
| **Enter** | Apply selected (or top) suggestion when focus is not in the editor; when suggestion popup is open, applies top/selected popup suggestion |
| **1–5** | When suggestion popup is open: apply that suggestion index |
| **Esc** | When suggestion popup is open: dismiss without applying |
| **Ctrl+Alt+.** (or **Win+Shift+.** fallback) | Global: UIA-first + clipboard utility (Phase 2/3; disabled in smoke) |

### Interop contract

P/Invoke layer: `windows/app/BiSpell.App/Interop/`

| Managed | Native (`c_api.h`) |
|---------|-------------------|
| `BispellEngine.Create` | `bispell_engine_create` |
| `BispellEngine.Check` | `bispell_engine_check` + `bispell_check_result_free` |
| `BispellEngine.Suggestions` | `bispell_engine_suggestions` + `bispell_string_list_free` |
| `AddToDictionary` / `IgnoreWord` | `bispell_engine_add_to_dictionary` / `ignore_word` |
| `RemoveFromDictionary` / `UnignoreWord` | `bispell_engine_remove_from_dictionary` / `unignore_word` |
| `ListAddedWords` / `ListIgnoredWords` | `bispell_engine_list_added_words` / `list_ignored_words` (+ string-list free) |
| `UpdateSettings` | `bispell_engine_update_settings` |
| ranges | `utf16_location` / `utf16_length` (C# string indices) |

Strings across the ABI are **UTF-8**; editor apply uses **UTF-16 code units**.

Environment override for dictionaries: `BISPELL_DICT_DIR`.

---

## Status

**MVP integration (U1–U5 + U7):** complete in-tree. Full checklist: [`docs/WINDOWS_PHASES.md`](../docs/WINDOWS_PHASES.md).

| Unit | Summary |
|------|---------|
| U2–U3 | C++ core, C ABI, `ctest` on Linux |
| U4 | C# WinUI 3 + P/Invoke, dictionary packaging, unpackaged F5 |
| U5 | Settings persistence, tray show/quit, AppData paths |
| U7 | Docs finalize, SoT note, CMake top-level, `.github/workflows/windows-core.yml` |
| Phase 2 | Global hotkey + clipboard batch fix + settings toggles (P2-HOTKEY / P2-CLIP / P2-SETTINGS / P2-GLUE) |
| Phase 3 | UIA assist (ValuePattern) + UIA-first hotkey + tier A/B/C + probe button (P3-SETTINGS / P3-UIA / P3-GLUE / P3-PROBE); U6 research **productized** as hotkey UIA |
| Phase 4 | Editor as-you-type + suggestion popup + debounce UI (P4-SETTINGS / P4-DEBOUNCE / P4-POPUP / P4-GLUE); smoke no-ops timers |
| U6 | Historical “optional UIA probe” — **subsumed** by Phase 3 utility (not continuous monitoring) |

Encoding contract: internal strings are **UTF-8**; token/misspelling ranges are **UTF-16 code units** (see `windows/core/include/bispell/encoding.hpp`).

**Binary smoke:** WinUI app build/F5 cannot run on the Linux orchestrator or GHA `windows-core`. On a Windows host: `build-native.ps1` → F5 → paste `I recieve mail. merhabaa dünya` (see App usage above). This is an environment limitation, not a code defect in the C# shell tree.

See [`docs/WINDOWS.md`](../docs/WINDOWS.md) for MVP vs non-goals, fork notes, and the full build matrix.

## macOS (unchanged)

Primary product remains SwiftPM. Windows work does **not** alter `Package.swift` or `Sources/`:

```bash
swift build -c release
swift test
```
