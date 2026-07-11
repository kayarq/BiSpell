# Windows MVP phases (U1–U7)

Checklist for the fork-friendly Windows path under [`windows/`](../windows/). macOS delivery remains in [`PHASES.md`](PHASES.md) and is **unchanged**.

**MVP “done”** = U1–U5 + U7 complete. **Phase 2** (clipboard hotkey) and **Phase 3** (UIA assist) are post-MVP product utilities. Historical **U6** (UIA probe research) is **subsumed** by Phase 3 hotkey UIA — not continuous monitoring.

| Unit | Title | Status | Notes |
|------|--------|--------|--------|
| **U1** | Fork-friendly scaffold & platform docs | ✅ | `windows/README.md`, `docs/WINDOWS.md`, root README Windows section, `.gitignore` |
| **U2** | Portable core — models, tokenizer, dictionary | ✅ | C++17 `bispell_core`; Linux `ctest` |
| **U3** | SpellEngine, language heuristics, lexicon, settings | ✅ | C ABI (`c_api.h`); lexicon under AppData / injectable paths |
| **U4** | WinUI 3 shell — check / suggest / apply | ✅ | C# WinUI 3 + P/Invoke; unpackaged F5 |
| **U5** | Settings persistence, Windows paths | ✅ | `settings.json` + AppData; tray/hide-to-tray **removed** in v0.2.1 (close = full quit) |
| **U6** | UI Automation (historical optional probe) | ✅ subsumed | Phase 3 productizes hotkey ValuePattern UIA + probe button (not always-on overlay) |
| **U7** | Integration glue, dictionary packaging, CI notes | ✅ | Docs finalize, SoT note, Linux CI workflow, CMake top-level |
| **P2** | Global hotkey + clipboard batch fix | ✅ | Ctrl+Alt+. / Win+Shift+.; `BISPELL_SMOKE` skips hotkey |
| **P3** | UIA assist + UIA-first hotkey + tiers A/B/C + probe | ✅ | `uiaAssistEnabled`; soft-fail COM; smoke skips UIA + hotkey |
| **P4** | Editor as-you-type + suggestion popup | ✅ | `asYouTypeEnabled` + debounce UI; editor-only; smoke no-ops timers |

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
7. Toggle TR/EN / max suggestions (under **Advanced**) → close window → relaunch → settings persist (`%APPDATA%\BiSpell\settings.json`).
8. Close (**X** / Alt+F4): process exits fully; no tray icon remains.

Full detail: [`windows/README.md`](../windows/README.md), [`docs/WINDOWS.md`](WINDOWS.md).

### Phase 2 / 3 utility checklist (Windows host)

1. Settings: **Global hotkey**, **Clipboard replace**, **UIA assist** (all default on; keys `globalHotkeyEnabled`, `clipboardReplaceEnabled`, `uiaAssistEnabled`).
2. **Tier A (Notepad):** focus edit with `I recieve mail. merhabaa dünya` → hotkey → in-place fix when replace on.
3. **UIA off:** uncheck UIA assist → copy text → hotkey → clipboard fixed (Phase 2).
4. **Tier C:** apps without ValuePattern still work via copy → hotkey → paste.
5. **Probe focused control:** status + CrashLog show `tier=` / read/write; no modal.
6. **Smoke:** `BISPELL_SMOKE=1` launch OK; no hotkey register; UIA paths soft no-op (no hang).

### Phase 4 as-you-type checklist (Windows host)

1. **Live check:** Clear editor → type `I recieve mail. merhabaa dünya` → within ~1s after pause, list shows both misses (as-you-type on; keys `asYouTypeEnabled`, `debounceMilliseconds`).
2. **Popup:** Nearest miss (e.g. `recieve`) → popup suggestions → press **1** or **Enter** → word fixed → list updates.
3. **Esc:** Open popup → Esc → dismissed; text unchanged.
4. **Debounce UI:** Set debounce to 800 ms → rapid type → one refresh after quiet period (not per keystroke).
5. **Toggle off:** Uncheck as-you-type → type more errors → list does not auto-update until F7.
6. **F7 still works** with as-you-type off or on.
7. **Utility hotkey:** Notepad / clipboard path still works (Phase 3 checklist subset).
8. **Lexicon:** Add word from misspelling → recheck clears it.
9. **Smoke env:** `BISPELL_SMOKE=1` launch OK; no as-you-type timer spam in startup log.
10. **Settings persist:** as-you-type + debounce survive relaunch.

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
- Continuous system-wide UI Automation monitoring or overlay underlines (Phase 3 is **hotkey-triggered** ValuePattern only)
- WPF / WinForms full UI port; system tray / hide-to-tray (removed v0.2.1)
- Full libhunspell affix engine
- Deleting Swift or dual-maintaining divergent dictionary blobs

## Related docs

- [`WINDOWS.md`](WINDOWS.md) — architecture & build matrix
- [`windows/README.md`](../windows/README.md) — implementer quick start
- [`PHASES.md`](PHASES.md) — macOS phases (separate track)
