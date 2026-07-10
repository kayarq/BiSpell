# Dictionary mirror (optional)

**Source of truth (SoT):**  
`Sources/BiSpellCore/Resources/Dictionaries/`  
(`en_US.dic`, `en_US.aff`, `tr.dic`, `tr.aff`)

This directory is an **optional packaging mirror** only:

- CMake may stage `.dic` / `.aff` here at configure time for consumers that cannot reach the Swift Resources path.
- `windows/app/scripts/stage-dictionaries.ps1` copies from SoT into this folder.
- The WinUI app prefers SoT via `BiSpell.App.csproj`; it falls back here only if SoT is missing.
- Binary `.dic` / `.aff` files under this path are **gitignored** — do not treat them as a second hand-edited master.

## License

Dictionaries originate from [wooorm/dictionaries](https://github.com/wooorm/dictionaries) (respective dictionary licenses). See also the root `README.md` license section.
