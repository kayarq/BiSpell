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
│  app/   WinUI 3 shell ──► links / PInvokes core             │
│         Check text │ Suggestions │ Apply │ Tray │ Settings  │
└─────────────────────────────────────────────────────────────┘
```

- **Core**: C++17 static library, no WinRT/Win32 UI headers in public APIs.
- **Ranges**: UTF-16 code unit offsets (parity with macOS `NSRange` for text APIs).
- **UI**: WinUI 3 native shell for the spell loop — **not** a SwiftUI Notes / taxonomy / templates port.

## Stack choice

| Decision | Choice | Why |
|----------|--------|-----|
| Core language | C++17 + CMake | Same tests on Linux orchestrator and MSVC; link into C++/WinRT |
| UI | WinUI 3 | Modern first-party Windows UI; not WPF/WinForms/full C# SwiftUI clone |
| Dictionary engine | Algorithm parity with Swift `HunspellDictionary` | Stem word-list + restricted-edit suggestions; **not** full libhunspell affix expansion for MVP |
| System spell API | Optional later | No `NSSpellChecker` equivalent required for MVP |

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
3. WinUI 3 shell: multiline editor, run check, list misspellings, suggestions, apply, add/ignore, minimal settings; optional tray.
4. Docs and CMake so core tests run on Linux; app builds on a Windows host.

### Explicit non-goals

- Notes window, templates, locks, taxonomy, markdown library, terminal themes.
- System-wide Accessibility / UI Automation overlay in other apps (optional probe only later).
- WPF / WinForms / full SwiftUI-in-C# clone of the macOS UI.
- Full libhunspell affix engine (unless proven drop-in superior without breaking parity tests).
- macOS `SystemSpellSuggester` / `NSSpellChecker` equivalent on Windows MVP.
- Changing `Package.swift` platforms or deleting/renaming Swift sources.
- Requiring Linux CI to compile WinUI.

## Dictionary source path

**Canonical source (do not fork long-term):**

```
Sources/BiSpellCore/Resources/Dictionaries/
  en_US.aff
  en_US.dic
  tr.aff
  tr.dic
```

Windows packaging should either:

- Copy into `windows/assets/Dictionaries/` at **CMake configure/build** time, or
- Reference the Swift path via CMake variables / configure_file.

Do **not** maintain two hand-edited divergent dictionary blobs.

Licenses: same as root README — dictionaries from [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses).

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

**Portable core (any host with C++17), once implemented:**

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

**WinUI app (Windows only):**

```text
# Install VS 2022 + Windows App SDK / WinUI as above
# Open windows/app solution (when present) or build via MSBuild/CMake as documented in windows/README.md
# Prefer unpackaged F5 for local dev if configured
```

**macOS (must keep working):**

```bash
swift test
swift build -c release
```

## Layout under `windows/`

| Path | Role |
|------|------|
| `windows/core/` | Portable C++ spell library (`include/bispell/`, `src/`) |
| `windows/app/` | WinUI 3 shell |
| `windows/assets/Dictionaries/` | Build-copied or staged dicts (source of truth remains Swift Resources) |
| `windows/tests/` | Core unit tests |
| `windows/CMakeLists.txt` | Top-level: core + tests always; app `if(WIN32)` later |
| `windows/README.md` | Windows-facing quick start |

## Fork / branch notes

- Keep **`main`** (or your primary branch) **macOS-clean**: Swift tree untouched; Windows is additive under `windows/` + docs.
- Optional long-lived branch name: `windows-platform` for Windows-focused work; merge docs/`windows/` without stripping `Sources/`.
- Optional: publish a GitHub fork for Windows packaging; not required for in-tree development.
- **Do not** delete or rename Swift sources in ways that break `swift build` / `Package.swift`.
- Implementers: stay inside `windows/` for code; document-only links to Swift for algorithm reference.

## Status

Scaffold and documentation only. C++ core, tests, and WinUI app land in subsequent units. See also [`windows/README.md`](../windows/README.md) and the dual-code plan for unit order (U2 core → U3 engine → U4 shell → …).
