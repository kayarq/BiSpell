# Native `bispell_core` for P/Invoke

The C# WinUI host loads **`bispell_core.dll`** via P/Invoke (`BiSpell.Interop.NativeMethods`).

## Build & stage

From a **VS 2022 Developer PowerShell** (or any shell with `cmake` + MSVC on PATH):

```powershell
cd windows\app\scripts
.\build-native.ps1 -Platform x64 -Config Release
```

This produces:

```
windows/app/native/x64/bispell_core.dll
windows/app/native/bispell_core.dll
```

`BiSpell.App.csproj` copies the DLL to the app output directory when present.

## Manual CMake

```bat
cmake -S windows -B windows\build-msvc-x64 -G "Visual Studio 17 2022" -A x64 -DBISPELL_BUILD_SHARED=ON
cmake --build windows\build-msvc-x64 --config Release --target bispell_core_shared
copy windows\build-msvc-x64\core\Release\bispell_core.dll windows\app\native\x64\
```

## Symbols

All exports match `windows/core/include/bispell/c_api.h` (Cdecl, UTF-8 strings, UTF-16 ranges).

Do not commit built `.dll` files (they are gitignored).
