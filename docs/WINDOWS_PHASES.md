# Windows MVP phases (U1–U7)

Checklist for the fork-friendly Windows path under [`windows/`](../windows/). macOS delivery remains in [`PHASES.md`](PHASES.md) and is **unchanged**.

**MVP “done”** = U1–U5 + U7 complete. **U6** (UI Automation probe) is optional stretch and is **not** required for MVP.

| Unit | Title | Status | Notes |
|------|--------|--------|--------|
| **U1** | Fork-friendly scaffold & platform docs | ✅ | `windows/README.md`, `docs/WINDOWS.md`, root README Windows section, `.gitignore` |
| **U2** | Portable core — models, tokenizer, dictionary | ✅ | C++17 `bispell_core`; Linux `ctest` |
| **U3** | SpellEngine, language heuristics, lexicon, settings | ✅ | C ABI (`c_api.h`); lexicon under AppData / injectable paths |
| **U4** | WinUI 3 shell — check / suggest / apply | ✅ | C# WinUI 3 + P/Invoke; unpackaged F5 |
| **U5** | Settings persistence, tray, Windows paths | ✅ | `settings.json`, tray show/quit, hide-to-tray |
| **U6** | UI Automation probe (system-wide feasibility) | ⬜ optional | Post-MVP research; not a product gate |
| **U7** | Integration glue, dictionary packaging, CI notes | ✅ | Docs finalize, SoT note, Linux CI workflow, CMake top-level |

## MVP acceptance matrix

| Check | Host | How |
|-------|------|-----|
| Portable core builds & tests pass | **Linux** (or any C++17) | See [Clean clone — core tests](#clean-clone--core-tests-linux) |
| App builds; check / suggest / apply with TR+EN | **Windows 10/11** | See [Windows app checklist](#windows-app-manual-checklist) |
| macOS Swift path intact | macOS preferred; tree check anywhere | `Package.swift` + `Sources/` present; on macOS: `swift test` |
| Dictionary license note retained | any | Root README + `docs/WINDOWS.md` → wooorm/dictionaries |
| Single dictionary SoT | any | `Sources/BiSpellCore/Resources/Dictionaries/` only |
| Fork-friendly layout | any | New code under `windows/` + docs; no removal of Swift app |

## Clean clone — core tests (Linux)

From a clean checkout (no pre-existing `windows/build`):

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

Expected: **6/6** tests pass (`test_encoding`, `test_tokenizer`, `test_dictionary`, `test_language_tagger`, `test_lexicon`, `test_spell_engine`).

Prerequisites: CMake ≥ 3.20, C++17 (g++ or clang++), Threads.

Optional GitHub Actions: [`.github/workflows/windows-core.yml`](../.github/workflows/windows-core.yml) runs the same commands on `ubuntu-latest` (core only; **no** WinUI).

## Windows app (manual checklist)

Requires a **Windows** host. Linux CI does **not** compile WinUI.

### Prerequisites

- Visual Studio 2022: Desktop C++, Windows App SDK / WinUI, .NET desktop
- .NET 8 SDK
- CMake ≥ 3.20 (VS bundled is fine)

### Build

```powershell
cd windows\app\scripts
.\build-native.ps1 -Platform x64 -Config Release
# Then either F5 in VS (windows/app/BiSpell.sln, Debug|x64)
# or: dotnet build ..\BiSpell.App\BiSpell.App.csproj -c Release -p:Platform=x64
```

Dictionaries: copied from SoT by the csproj into output `Dictionaries\`. Optional mirror: `.\stage-dictionaries.ps1`.

### Smoke (MVP UI)

1. Launch BiSpell (unpackaged).
2. Status line shows engine ready (or a clear error if DLL / dicts missing).
3. Paste: `I recieve mail. merhabaa dünya`
4. **F7** / **Check** → misspellings include `recieve`, `merhabaa`.
5. Select `recieve` → suggestions (prefer including `receive`) → **Apply** / Enter / double-click.
6. **Add to dictionary** on a nonsense token → re-check clean.
7. Toggle TR/EN / max suggestions → quit → relaunch → settings persist (`%APPDATA%\BiSpell\settings.json`).
8. Tray: **Show BiSpell** / **Quit**; close window hides to tray.

Full detail: [`windows/README.md`](../windows/README.md), [`docs/WINDOWS.md`](WINDOWS.md).

## macOS (unchanged)

```bash
swift test
swift build -c release
# optional: ./Scripts/package-app.sh
```

Windows units **must not** delete or rename Swift sources, change `Package.swift` platforms, or rewrite Notes/AX as a second product. Primary product remains the menu-bar + Notes app on macOS 14+.

## Dictionary packaging (U7)

| Location | Role |
|----------|------|
| `Sources/BiSpellCore/Resources/Dictionaries/` | **Source of truth** |
| CMake `configure_file` → `windows/build/tests/Dictionaries/` | Hermetic `ctest` |
| `BiSpell.App.csproj` → app output `Dictionaries/` | Bundled TR+EN for WinUI |
| `windows/assets/Dictionaries/` | Optional packaging mirror (blobs gitignored) |

Licenses: same as root README — [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses).

## CMake layout note

```text
windows/CMakeLists.txt
  add_subdirectory(core)    # always
  add_subdirectory(tests)   # always
  # C# WinUI under windows/app is NOT a CMake target (BiSpell.sln / dotnet).
  # if(WIN32) only prints a status pointer to windows/app.
```

Shared DLL for P/Invoke: `bispell_core_shared` (`-DBISPELL_BUILD_SHARED=ON`, default ON).

## CI limitations

| Workflow / host | What runs |
|-----------------|-----------|
| Linux (orchestrator / GHA `windows-core.yml`) | CMake core + `ctest` only |
| Windows developer machine | Core + DLL + WinUI F5 / smoke |
| macOS | SwiftPM (existing); optional CMake core |

There is **no** required Windows runner for MVP gate. Documented manual checklist above is the Windows UI acceptance path.

## Explicit non-goals (still)

- Notes / templates / locks / taxonomy / markdown library
- System-wide UI Automation overlay (U6 probe only if pursued later)
- WPF / WinForms full UI port (tray may use WinForms `NotifyIcon` only)
- Full libhunspell affix engine
- Deleting Swift or dual-maintaining divergent dictionary blobs

## Related docs

- [`WINDOWS.md`](WINDOWS.md) — architecture & build matrix
- [`windows/README.md`](../windows/README.md) — implementer quick start
- [`PHASES.md`](PHASES.md) — macOS phases (separate track)
