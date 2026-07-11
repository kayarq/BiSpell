# BiSpell Windows installer (Inno Setup)

Declarative [Inno Setup 6](https://jrsoftware.org/isinfo.php) script that packages the unpackaged self-contained publish tree into a setup EXE.

| File | Role |
|------|------|
| [`BiSpell.iss`](BiSpell.iss) | Installer definition (`[Setup]`, `[Files]`, `[Icons]`, `[Tasks]`, `[Run]`) |

## Prerequisites

1. **Payload tree** already built (same contents as the portable zip):

   ```text
   dist\BiSpell-win-x64\
     BiSpell.App.exe
     bispell_core.dll
     Dictionaries\...
     (Windows App SDK self-contained runtime files)
   ```

   Produce it with:

   ```powershell
   .\windows\app\scripts\package-release.ps1 -Version 0.2.1
   ```

2. **Inno Setup 6** with `iscc` on `PATH`  
   - Local: install from [jrsoftware.org](https://jrsoftware.org/isdl.php)  
   - CI: e.g. `choco install innosetup -y`

## Compile

From the **repo root** (defines resolve relative to the `.iss` file):

```powershell
iscc /DMyAppVersion=0.2.1 windows\installer\BiSpell.iss
```

Optional path overrides (defaults shown):

```powershell
iscc `
  /DMyAppVersion=0.2.1 `
  /DSourceDir=..\..\dist\BiSpell-win-x64 `
  /DOutputDir=..\..\dist `
  windows\installer\BiSpell.iss
```

| Define | Default | Meaning |
|--------|---------|---------|
| `MyAppVersion` | `0.1.0` | Embedded version + output filename |
| `SourceDir` | `..\..\dist\BiSpell-win-x64` | Published app tree (relative to this folder) |
| `OutputDir` | `..\..\dist` | Where the setup EXE is written |

**Output:** `dist\BiSpell-{version}-win-x64-setup.exe`

Example: `BiSpell-0.2.1-win-x64-setup.exe`

## Wizard options (three)

| Option | Mechanism | Default |
|--------|-----------|---------|
| **Desktop shortcut** | Task `desktopicon` → `{autodesktop}` icon | Unchecked |
| **Taskbar pin guidance** | Task `taskbar` (honest label + finish text) | Unchecked |
| **Start BiSpell after install** | `[Run]` postinstall checkbox | Checked once (`checkedonce`); skipped when silent (`skipifsilent`) |

### Start Menu (always)

A **Start Menu** entry under the BiSpell program group is **always** created via `[Icons]` (`{group}\BiSpell`). It is **not** gated by any task. Uninstall also adds a Start Menu uninstall entry.

### Desktop shortcut

Created **only if** the user checks **Create a desktop shortcut** (`desktopicon`).

### Taskbar — honesty policy

Modern Windows **does not allow installers to reliably pin** an app to the taskbar. This script:

- Offers a **taskbar** checkbox whose **label states the limitation** and that the user pins themselves from the Start Menu.
- When that task is **checked**, a **minimal `[Code]`** hook updates **`FinishedLabel`** with step-by-step pin guidance (Start Menu → right-click BiSpell → **Pin to taskbar**).
- Does **not** use COM / shell pin APIs and does **not** claim that a pin was applied.
- Keeps the Start Menu shortcut **independent** of the taskbar task so the pin path always exists after install.

If you check the taskbar option, treat it as “remind me how to pin,” not “pin for me.”

### Launch after install

`[Run]` starts `{app}\BiSpell.App.exe` with:

`Flags: nowait postinstall skipifsilent checkedonce`

Silent/unattended installs do not auto-launch.

## Privileges

| Setting | Value |
|---------|--------|
| `PrivilegesRequired` | `admin` (default install under Program Files) |
| `PrivilegesRequiredOverridesAllowed` | `dialog` (wizard can offer a non-elevated install) |

## App identity & upgrades

- **`AppId`**: fixed GUID in `BiSpell.iss` — do **not** change it or upgrades will not find the previous install.
- **`OutputBaseFilename`**: `BiSpell-{#MyAppVersion}-win-x64-setup`

## Uninstall & user data

User data lives under **`%APPDATA%\BiSpell\`** (settings, lexicon, notes).

**Uninstall does not delete AppData.** There is no `[UninstallDelete]` (or other rule) targeting `%APPDATA%\BiSpell`. Users keep notes and settings across reinstalls; they may remove that folder manually if desired.

## Layout

```text
windows/installer/
  BiSpell.iss    ← this script
  README.md      ← this file
```

Relative defaults from `windows/installer/`:

```text
SourceDir  →  ../../dist/BiSpell-win-x64
OutputDir  →  ../../dist
```

## Notes

- Declarative-first: `[Setup]`, `[Files]`, `[Icons]`, `[Tasks]`, `[Run]` do the install work.
- **Minimal `[Code]` only**: `CurPageChanged` expands `FinishedLabel` when the `taskbar` task is checked (guidance text; no pin API).
- No custom wizard icon required (default Inno icon).
- Architecture: 64-bit (`x64compatible` / install-in-64-bit-mode).
- Portable zip remains a secondary distribution; the setup EXE is the primary Release asset once CI wires `iscc`.
