# BiSpell

Offline Turkish + English spell-check assistant for macOS 14+.

Menu bar app that watches the focused text field (Accessibility), underlines misspellings, and offers local Hunspell-dictionary suggestions.

## Features

- **As-you-type suggestions** (markers + popup)
- **System-wide best-effort** via Accessibility
- **Fully offline** Hunspell word lists (TR + EN)
- **Spelling only** (no grammar)
- Personal dictionary / ignore / ignore-in-app
- App denylist (Terminal, 1Password, …)
- Launch at login
- Hotkey **⌥⌘.** — check selection or jump to first mistake; press again to accept the top suggestion
- Optional clipboard replace fallback
- Settings window: denylist editor, personal dictionary & ignored-word management

## Build & run

```bash
cd ~/BiSpell
swift build -c release
swift test

# Run (dev)
swift run BiSpell
```

Package as `.app` (recommended for Accessibility + Launch at Login):

```bash
./Scripts/package-app.sh
open dist/BiSpell.app
```

Grant **Privacy & Security → Accessibility** to BiSpell when prompted.

## Tests

```bash
swift test
```

## Support matrix

While running, use **Probe Frontmost App Support** from the menu. Results are saved to:

`~/Library/Application Support/BiSpell/support-matrix.json`

## License

Personal use. Dictionaries from [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses). The same TR/EN word lists are shared by the macOS Swift bundle and the Windows path — see [dictionary source of truth](#dictionaries-source-of-truth).

## Windows (parallel path)

**macOS remains the primary platform** and is **unchanged** (`Package.swift`, `Sources/`, SwiftPM). A fork-friendly Windows path lives under [`windows/`](windows/): portable C++17 spell core (algorithm parity with Swift `BiSpellCore`) + **C# WinUI 3** shell for **in-app Notes**, check / suggest / apply, settings, and tray. It is **editor-only** (no system-wide hotkey/UIA) and **not** a port of the full macOS Notes/templates product.

| Doc | Contents |
|-----|----------|
| [`windows/README.md`](windows/README.md) | Quick start, layout, build steps, app smoke |
| [`docs/WINDOWS.md`](docs/WINDOWS.md) | Architecture, MVP vs non-goals, AppData, fork notes |
| [`docs/WINDOWS_PHASES.md`](docs/WINDOWS_PHASES.md) | Windows MVP phase checklist (U1–U7) |

### Portable core (Linux / macOS / Windows)

```bash
cmake -S windows -B windows/build -DCMAKE_BUILD_TYPE=Release
cmake --build windows/build --target bispell_core_tests
cd windows/build && ctest --output-on-failure
```

Requires CMake ≥ 3.20 and a C++17 compiler (g++ / clang++ / MSVC). Optional CI: [`.github/workflows/windows-core.yml`](.github/workflows/windows-core.yml) (Linux core tests only).

### WinUI app — download a release (recommended)

You do **not** need Visual Studio or CMake on your PC. CI builds the Windows package on GitHub-hosted runners:

1. Open the repo **[Releases](https://github.com/kayarq/BiSpell/releases)** (or this fork’s Releases).
2. Download **`BiSpell-*-win-x64.zip`**, unzip, run **`BiSpell.App.exe`**.
3. Smoke: paste `I recieve mail. merhabaa dünya` → **Check** (or F7) → apply a suggestion.

Workflow: [`.github/workflows/windows-release.yml`](.github/workflows/windows-release.yml) (`workflow_dispatch` or tag `v*-windows`).

### WinUI app — build yourself (optional, needs disk space)

1. VS 2022 + Desktop C++ + Windows App SDK / WinUI + .NET 8.
2. `windows\app\scripts\build-native.ps1` → stages `bispell_core.dll`.
3. Open `windows/app/BiSpell.sln` → **Debug|x64** → **F5**, or `dotnet publish` / `package-release.ps1` as in [`windows/README.md`](windows/README.md).

Full WinUI binary build is **not** possible from Linux; use the release workflow above.

### Dictionaries (source of truth)

| Location | Role |
|----------|------|
| **`Sources/BiSpellCore/Resources/Dictionaries/`** | **Single source of truth** (`.dic` / `.aff`) |
| `windows/build/…/Dictionaries/` | CMake stage for `ctest` |
| App output `Dictionaries/` | Copied from SoT by `BiSpell.App.csproj` |
| `windows/assets/Dictionaries/` | Optional mirror (gitignored blobs; staged by CMake / `stage-dictionaries.ps1`) |

Do **not** hand-maintain a second divergent dictionary set. Licenses: [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses).

## Documentation

- [`plan.md`](plan.md) — original product & architecture plan (also at `docs/plan.md`)
- [`docs/PHASES.md`](docs/PHASES.md) — macOS phase delivery checklist
- [`docs/WINDOWS.md`](docs/WINDOWS.md) — Windows platform path (C++ core + WinUI 3)
- [`docs/WINDOWS_PHASES.md`](docs/WINDOWS_PHASES.md) — Windows MVP unit checklist

## Notes

BiSpell includes a **plain-text notes** window (sidebar list + editor):

- Open from the Dock / app launch, or menu bar **Open Notes**
- **Manual Save** (toolbar or **⌘S**) to `~/Library/Application Support/BiSpell/Notes/`
- Search, create, delete notes
- After you finish a word (space/punct), a **popup appears near the typo** with suggestions
- **⌘1** = best fix, **⌘2–⌘5** = other choices (works while typing; Esc dismisses)
- **⌥⌘.** still opens suggestions at the caret if needed
- System-wide spell-check in other apps still uses menu bar / **⌥⌘.** when Notes is not focused

Shortcuts: **⌘N** new note, **⌘S** save.

## Learned corrections

When you accept a spelling fix **in the Notes window**, BiSpell appends to:

`~/Library/Application Support/BiSpell/corrections.json`

Each entry is `{ wrong, correct, count, firstCorrectedAt, lastCorrectedAt }`. Counts rise when you repeat the same fix — useful later for a personal most-common-misspellings dictionary.

## Lock & templates

- **Lock selection** (toolbar): selected text becomes read-only but still **selectable/copyable** (highlighted).
- **Unlock**: selection or caret inside a locked span.
- Sidebar **Templates** section is separate from **Notes**.
- **New → New Template**, or **Move to Templates** on a note.
- **New Note from Template** (context menu / menu) copies body + locked spans into a new editable note.

## Notes appearance (terminal UI)

Themes (command strip → Theme):
- **Phosphor** (default) — green CRT
- **Amber** — vintage amber terminal
- **Cyan** — ice / modern CLI
- **Paper Mono** — light day terminal

Writing fonts: **SF Mono** (default), **Menlo**, **Avenir Next**.

Chrome uses a custom command-strip (chips), not stock toolbar buttons.

