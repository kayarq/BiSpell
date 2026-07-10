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
| User data | `%APPDATA%\BiSpell\` | `user-lexicon.json` |

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
    BiSpell.App/            ← WinUI project + Interop/
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
7. **Add to dictionary** / **Ignore** on a nonsense token → re-check no longer flags it.
8. Toggle TR/EN checkboxes to limit languages; re-check.

### Keyboard

| Key | Action |
|-----|--------|
| **F7** | Run spell-check |
| **Enter** | Apply selected (or top) suggestion when focus is not in the editor |

### Interop contract

P/Invoke layer: `windows/app/BiSpell.App/Interop/`

| Managed | Native (`c_api.h`) |
|---------|-------------------|
| `BispellEngine.Create` | `bispell_engine_create` |
| `BispellEngine.Check` | `bispell_engine_check` + `bispell_check_result_free` |
| `BispellEngine.Suggestions` | `bispell_engine_suggestions` + `bispell_string_list_free` |
| `AddToDictionary` / `IgnoreWord` | `bispell_engine_add_to_dictionary` / `ignore_word` |
| `UpdateSettings` | `bispell_engine_update_settings` |
| ranges | `utf16_location` / `utf16_length` (C# string indices) |

Strings across the ABI are **UTF-8**; editor apply uses **UTF-16 code units**.

Environment override for dictionaries: `BISPELL_DICT_DIR`.

---

## Status

**U4:** C# WinUI 3 shell + real P/Invoke to `c_api.h`, shared DLL CMake target, dictionary packaging, documented Windows build. No Notes port.

Encoding contract: internal strings are **UTF-8**; token/misspelling ranges are **UTF-16 code units** (see `windows/core/include/bispell/encoding.hpp`).

**Binary smoke:** WinUI app build/F5 cannot run on the Linux orchestrator. On a Windows host: `build-native.ps1` → F5 → paste `I recieve mail. merhabaa dünya` (see App usage above). This is an environment limitation, not a code defect in the C# shell tree.

See [`docs/WINDOWS.md`](../docs/WINDOWS.md) for MVP vs non-goals, fork notes, and the full build matrix.

## macOS

Continue using the root README and SwiftPM:

```bash
swift build -c release
swift test
```
