<#
.SYNOPSIS
  Build bispell_core.dll (shared) for the C# WinUI host and stage it under windows/app/native.

.DESCRIPTION
  Requires a C++ toolchain (MSVC recommended).
  On GitHub Actions, use with ilammy/msvc-dev-cmd (Ninja + cl.exe).
  Locally, prefers Ninja if available; otherwise Visual Studio generators
  (18 / 17).

.EXAMPLE
  .\build-native.ps1
  .\build-native.ps1 -Platform x64 -Config Release
#>
param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $PSScriptRoot          # windows/app
$WindowsDir = Split-Path -Parent $AppDir            # windows
$RepoRoot = Split-Path -Parent $WindowsDir
$BuildDir = Join-Path $WindowsDir "build-msvc-$Platform"
$StageDir = Join-Path $AppDir "native\$Platform"

$ArchMap = @{
    "x64"   = "x64"
    "x86"   = "Win32"
    "ARM64" = "ARM64"
}
$VsArch = $ArchMap[$Platform]

function Test-Command($Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

# Prefer Ninja + existing cl.exe (CI after msvc-dev-cmd, or VS Developer PowerShell).
$useNinja = (Test-Command "ninja") -and (Test-Command "cl")

Write-Host "==> Configuring CMake ($Platform / $Config) in $BuildDir"
if ($useNinja) {
    Write-Host "    Generator: Ninja (cl from environment)"
    cmake -S $WindowsDir -B $BuildDir `
        -G Ninja `
        -DCMAKE_BUILD_TYPE=$Config `
        -DBISPELL_BUILD_SHARED=ON
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (Ninja)" }

    Write-Host "==> Building bispell_core_shared"
    cmake --build $BuildDir --target bispell_core_shared
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed (Ninja)" }
} else {
    $generators = @(
        "Visual Studio 18 2026",
        "Visual Studio 18 2025",
        "Visual Studio 17 2022"
    )
    $configured = $false
    foreach ($gen in $generators) {
        Write-Host "    Trying generator: $gen -A $VsArch"
        cmake -S $WindowsDir -B $BuildDir `
            -G $gen -A $VsArch `
            -DBISPELL_BUILD_SHARED=ON
        if ($LASTEXITCODE -eq 0) {
            $configured = $true
            break
        }
        # Wipe partial configure and try next generator
        if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
    }
    if (-not $configured) {
        throw "cmake configure failed: no usable Visual Studio generator (and Ninja+cl not available)."
    }

    Write-Host "==> Building bispell_core_shared"
    cmake --build $BuildDir --config $Config --target bispell_core_shared
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed (VS generator)" }
}

# Locate DLL (single-config Ninja vs multi-config VS)
$candidates = @(
    (Join-Path $BuildDir "core\$Config\bispell_core.dll"),
    (Join-Path $BuildDir "core\bispell_core.dll"),
    (Join-Path $BuildDir "$Config\bispell_core.dll"),
    (Join-Path $BuildDir "bispell_core.dll")
)
$dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) {
    # Recursive fallback
    $found = Get-ChildItem -Path $BuildDir -Filter "bispell_core.dll" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($found) { $dll = $found.FullName }
}
if (-not $dll) {
    Write-Error "bispell_core.dll not found after build. Searched:`n$($candidates -join "`n")"
}

New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Copy-Item -Force $dll (Join-Path $StageDir "bispell_core.dll")
Copy-Item -Force $dll (Join-Path $AppDir "native\bispell_core.dll")

Write-Host "==> Staged: $StageDir\bispell_core.dll"
Write-Host "Done. Build BiSpell.App in VS; the csproj copies the DLL to output when present."
