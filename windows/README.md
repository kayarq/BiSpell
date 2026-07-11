# BiSpell — Windows

Parallel **Windows** path for BiSpell. The macOS Swift product under `Sources/` is **unchanged** and remains the primary platform.

This tree hosts a portable C++ spell core (algorithm parity with Swift `BiSpellCore`) and a **thin C# WinUI 3** shell that P/Invokes the core C ABI for check / suggest / apply, plus a minimal in-app **Notes** surface. It is **editor-only** (no system-wide hotkey or UIA injection) and **not** a port of the full macOS Notes/templates product.

## Stack

| Layer | Choice | Notes |
|--------|--------|--------|
| Spell core | **C++17** + CMake | Portable; builds on Linux CI and MSVC |
| C ABI | `windows/core/include/bispell/c_api.h` | Stable exports for P/Invoke |
| Static lib | `bispell_core` (always) | Linked by `ctest` on Linux/Windows; optional C++ hosts |
| Shared lib | `bispell_core.dll` (`bispell_core_shared`, `BISPELL_BUILD_SHARED`) | Loaded by C# host; optional (`-DBISPELL_BUILD_SHARED=OFF`) |
| UI shell | **C# WinUI 3** (Windows App SDK) | **Product path** — unpackaged F5; XAML ergonomics |
| Dictionaries | Same `.dic` / `.aff` as macOS | **SoT:** `Sources/BiSpellCore/Resources/Dictionaries/` |
| User data | `%APPDATA%\BiSpell\` | `settings.json`, `user-lexicon.json`, `Notes\*.txt` |
| Lifecycle | Close = full quit | No tray / hide-to-tray (v0.2.1+); quieter unsigned Defender story |
| Notes MVP | Plain-text files under `Notes\` | Sidebar list + editor; title = first line |
| As-you-type | In-note debounce + popup | Length-aware: quiet on delete / mid-word; popup on word boundary |

**Future alternative (not dual product path):** a C++/WinRT shell could static-link `bispell_core` in one MSBuild solution later. Keep a single app tree under `windows/app/` (C# today); do not maintain a parallel incomplete C++/WinRT product.

### Relation to Swift `BiSpellCore`

| Swift (`Sources/BiSpellCore`) | Windows (`windows/core`) |
|------------------------------|---------------------------|
| `Models`, `Tokenizer` | Ported types + tokenizer |
| `HunspellDictionary` | Same stem-list / restricted-edit behavior (not full affix engine) |
| `LanguageTagger` | Heuristics only (no Apple NaturalLanguage) |
| `SpellEngine`, `UserLexicon`, settings subset | Ported; exposed via C ABI |
| macOS Notes templates / locks / taxonomy / AX overlay | **Out of scope** on Windows |
| In-app plain Notes + spell | **In scope** (Phase 5 MVP) |

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
    BiSpell.App/            ← WinUI project + Interop/ + Services/ (settings, notes)
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
3. Create **New** if the notes list is empty, then paste/type: `I recieve mail. merhabaa dünya`
4. Press **F7** or **Check** → expect `recieve`, `merhabaa` in the misspellings list; status badge shows count.
5. Select `recieve` → suggestions (ideally include `receive`).
6. **Enter** or **double-click** a suggestion → text updated at the **UTF-16** range; auto re-check.
7. **Add to dictionary** / **Ignore** on a nonsense token → re-check no longer flags it; word appears in the lexicon panel (below).
8. Toggle **Spell-check enabled** / TR/EN / **As-you-type**; open **Advanced** for **Max suggestions** / **Min word length** / **Debounce**; re-check. Close window (**X**) and relaunch → settings still applied.
9. **Save (Ctrl+S)** or switch notes → note files under `%APPDATA%\BiSpell\Notes\`.
10. Closing the window (**X** / Alt+F4) **fully quits** the process (no tray icon; no hide-to-tray).

### Settings

Settings card on the main window (persisted to AppData):

| Control | Effect |
|---------|--------|
| **Spell-check enabled** | Master gate for check |
| **Turkish** / **English** | Language dictionaries |
| **As-you-type check** | Debounced live check in the **active note editor** only. Off → F7 still works. JSON: `asYouTypeEnabled` |
| **Advanced → Max suggestions** | Caps suggestion list (1–20) |
| **Advanced → Min word length** | Tokens shorter than this are **skipped** on check (1–10, default 2) |
| **Advanced → Debounce (ms)** | Applies after **typing pause** and after **delete settle** (0–5000, default **250**). JSON: `debounceMilliseconds` |

There is **no** global hotkey, clipboard-replace utility, or UIA assist in the product UI. Older `settings.json` keys (`globalHotkeyEnabled`, `clipboardReplaceEnabled`, `uiaAssistEnabled`) are ignored if present.

### Notes MVP (Phase 5 — in-app only)

| Piece | Behavior |
|-------|----------|
| Sidebar | List of notes (title = first non-empty line, truncated, or **Untitled**); **New** / **Delete** / select |
| Editor | Active note body; same as-you-type + misspellings + suggestions as the spell loop |
| Storage | `%APPDATA%\BiSpell\Notes\*.txt` (UTF-8) |
| Save | **Ctrl+S** / **Save** button; **auto-save** when switching notes or quitting |
| Scope | No templates, locks, taxonomy, or markdown preview |

### As-you-type (editor / note only)

Length-aware scheduling so holding backspace does not thrash the popup:

| Edit | Behavior |
|------|----------|
| **Delete / backspace** (length ↓) | Immediately **hide** suggestion popup; cancel short debounce; **QuietRecheck** after `max(Debounce, 450)` ms — updates misspelling **list only** (no popup) |
| **Insert letter/digit** (mid-word) | After normal debounce: **QuietRecheck** (list only, no popup) |
| **Insert whitespace / punctuation** (word boundary) | After normal debounce: **FullAsYouType** (list + nearest + popup) |
| **Same length** (selection replace) | FullAsYouType after debounce |
| **Programmatic text** | Apply / note load set `_suppressAsYouType` (no check storm) |
| **Smoke** | `BISPELL_SMOKE=1` → schedule no-op; debouncer never arms timers |

**F7** still runs an immediate full check. This is **not** system-wide monitoring.

### Lexicon manage UI

Collapsible expander **Personal dictionary** (collapsed by default).

| List | How words get there | Manage action |
|------|---------------------|---------------|
| **Dictionary** | Select a misspelling → **Add to dictionary** | **Remove selected** |
| **Ignored** | Select a misspelling → **Ignore** | **Unignore selected** |

### Persistence (AppData)

| Path | Contents |
|------|----------|
| `%APPDATA%\BiSpell\settings.json` | `isEnabled`, TR/EN, `maxSuggestions`, `minWordLength`, `debounceMilliseconds`, `asYouTypeEnabled` |
| `%APPDATA%\BiSpell\user-lexicon.json` | Personal dictionary + ignore list |
| `%APPDATA%\BiSpell\Notes\*.txt` | Note bodies (title derived from first non-empty line) |

### Process lifecycle (v0.2.1+)

- **X** / Alt+F4 → full quit: persist settings + dirty note → dispose runtime resources → `Environment.Exit(0)`.
- **No system tray** and **no hide-to-tray** — process does not stay resident after close.
- Unsigned release zips may still show SmartScreen / Defender prompts; downloading from the fork **Releases** page is recommended.

### Keyboard

| Key | Action |
|-----|--------|
| **F7** | Run spell-check on the active note |
| **Ctrl+S** | Save active note |
| **Enter** | Apply selected (or top) suggestion when focus is not in the editor; when suggestion popup is open, applies top/selected |
| **1–5** | When suggestion popup is open: apply that suggestion index |
| **Esc** | When suggestion popup is open: dismiss without applying |

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

**In-tree product:** portable spell core + WinUI **Notes + editor spell** shell. Out-of-app hotkey/UIA/clipboard utility has been **removed** (editor-only). Checklist history: [`docs/WINDOWS_PHASES.md`](../docs/WINDOWS_PHASES.md).

| Unit | Summary |
|------|---------|
| U2–U3 | C++ core, C ABI, `ctest` on Linux |
| U4 | C# WinUI 3 + P/Invoke, dictionary packaging, unpackaged F5 |
| U5 | Settings persistence, AppData paths (tray later dropped) |
| Phase 1 | Min word length + lexicon manage UI |
| Phase 4 | As-you-type (length-aware quiet/full) + suggestion popup |
| Phase 5 | Notes MVP (sidebar + `%APPDATA%\\BiSpell\\Notes\\`) |
| v0.2.1 | Close = full quit; no tray; UI denser + Advanced expander |
| Removed | Global hotkey, clipboard replace utility, UIA assist, tray |

Encoding contract: internal strings are **UTF-8**; token/misspelling ranges are **UTF-16 code units**.

**Binary smoke:** WinUI app build/F5 cannot run on the Linux orchestrator or GHA `windows-core`. On a Windows host: `build-native.ps1` → F5. Or download a **windows-release** zip.

See [`docs/WINDOWS.md`](../docs/WINDOWS.md) for MVP vs non-goals, fork notes, and the full build matrix.

## macOS (unchanged)

Primary product remains SwiftPM. Windows work does **not** alter `Package.swift` or `Sources/`:

```bash
swift build -c release
swift test
```
