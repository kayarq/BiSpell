<#
.SYNOPSIS
  Build bispell_core.dll (shared) for the C# WinUI host and stage it under windows/app/native.

.DESCRIPTION
  Requires CMake + MSVC (VS 2022 Developer PowerShell recommended).
  Default platform: x64.

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

Write-Host "==> Configuring CMake ($Platform / $Config) in $BuildDir"
cmake -S $WindowsDir -B $BuildDir `
    -G "Visual Studio 17 2022" -A $VsArch `
    -DBISPELL_BUILD_SHARED=ON

Write-Host "==> Building bispell_core_shared"
cmake --build $BuildDir --config $Config --target bispell_core_shared

# Locate DLL (multi-config generators put it under config subdir)
$candidates = @(
    (Join-Path $BuildDir "core\$Config\bispell_core.dll"),
    (Join-Path $BuildDir "core\bispell_core.dll"),
    (Join-Path $BuildDir "$Config\bispell_core.dll"),
    (Join-Path $BuildDir "bispell_core.dll")
)
$dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) {
    Write-Error "bispell_core.dll not found after build. Searched:`n$($candidates -join "`n")"
}

New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Copy-Item -Force $dll (Join-Path $StageDir "bispell_core.dll")
# Also stage flat path for simple copy
Copy-Item -Force $dll (Join-Path $AppDir "native\bispell_core.dll")

Write-Host "==> Staged: $StageDir\bispell_core.dll"
Write-Host "Done. Build BiSpell.App in VS; the csproj copies the DLL to output when present."
