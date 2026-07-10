# BiSpell — Windows

Parallel **Windows** path for BiSpell. The macOS Swift product under `Sources/` is **unchanged** and remains the primary platform.

This tree hosts a portable C++ spell core (algorithm parity with Swift `BiSpellCore`) and a **WinUI 3** shell for check / suggest / apply. It is **not** a port of the SwiftUI Notes UI.

## Stack

| Layer | Choice | Notes |
|--------|--------|--------|
| Spell core | **C++17** + CMake | Portable; builds on Linux CI and MSVC |
| UI shell | **WinUI 3** (C++/WinRT preferred) | Check text, misspellings, suggestions, apply |
| Dictionaries | Same `.dic` / `.aff` as macOS | **SoT:** `Sources/BiSpellCore/Resources/Dictionaries/` (CMake stages; see below) |
| User data | `%APPDATA%\BiSpell\` | Lexicon + settings (when implemented) |

### Relation to Swift `BiSpellCore`

| Swift (`Sources/BiSpellCore`) | Windows (`windows/core`) |
|------------------------------|---------------------------|
| `Models`, `Tokenizer` | Ported types + tokenizer |
| `HunspellDictionary` | Same stem-list / restricted-edit behavior (not full affix engine) |
| `LanguageTagger` | Heuristics only (no Apple NaturalLanguage) |
| `SpellEngine`, `UserLexicon`, settings subset | Ported in later units |
| Notes / AX / overlay / templates | **Out of scope** for Windows MVP |

Logic is reimplemented for parity; sources are not shared via interop.

## Layout

```
windows/
  README.md              ← this file
  CMakeLists.txt         ← top-level (core + tests; app guarded on WIN32 later)
  .gitignore
  core/                  ← portable C++ spell library
    include/bispell/     ← public headers
    src/
  app/                   ← WinUI 3 shell (Windows host only)
  assets/Dictionaries/   ← optional CMake mirror of SoT (not the primary source)
  tests/                 ← core unit tests (Linux + Windows)
```

### Dictionaries (source of truth vs mirrors)

| Location | Role |
|----------|------|
| `Sources/BiSpellCore/Resources/Dictionaries/` | **Source of truth (SoT)** — edit / vendor dicts here only (shared with macOS Swift) |
| `windows/build/tests/Dictionaries/` | **CMake stage** — `tests/CMakeLists.txt` copies SoT `.dic`/`.aff` next to test binaries (`BISPELL_DICT_DIR`) for hermetic `ctest` |
| `windows/assets/Dictionaries/` | **Optional mirror** — CMake may copy SoT `.dic` here for packaging consumers; do not treat as SoT |

Do not hand-edit blobs under `assets/` or the build tree as the primary copy. Configure/build stages from the Swift Resources path automatically.

## Build

### Portable core + tests (Linux, macOS, or Windows)

Once core sources exist (units after this scaffold):

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

Requires a C++17 toolchain (g++, clang++, or MSVC) and CMake ≥ 3.20.

### WinUI 3 app (Windows host only)

**Prerequisites**

- Visual Studio 2022 with Desktop C++ and Windows App SDK / WinUI workload
- Windows App SDK
- CMake (optional for core; VS for the app)

Open the solution under `windows/app/` when it exists, or follow steps in [`docs/WINDOWS.md`](../docs/WINDOWS.md). The orchestrator / Linux CI does **not** build WinUI.

## Status

**U2 core in place** — portable C++17 `bispell_core` (models, tokenizer, HunspellDictionary) + Linux/MSVC-friendly tests. WinUI shell still later.

Encoding contract: internal strings are **UTF-8**; token/misspelling ranges are **UTF-16 code units** (see `windows/core/include/bispell/encoding.hpp`).

See [`docs/WINDOWS.md`](../docs/WINDOWS.md) for MVP vs non-goals, dictionary paths, fork notes, and the full build matrix.

## macOS

Continue using the root README and SwiftPM:

```bash
swift build -c release
swift test
```
