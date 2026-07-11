<#
.SYNOPSIS
  Build bispell_core.dll (shared) for the C# WinUI host and stage under windows/app/native.
#>
param(
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64",
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $PSScriptRoot
$WindowsDir = Split-Path -Parent $AppDir
$BuildDir = Join-Path $WindowsDir "build-msvc-$Platform"
$StageDir = Join-Path $AppDir "native\$Platform"

$ArchMap = @{ "x64" = "x64"; "x86" = "Win32"; "ARM64" = "ARM64" }
$VsArch = $ArchMap[$Platform]

function Test-Command($Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Get-VsCMakeGenerators {
    $help = & cmake --help 2>&1 | Out-String
    $gens = @()
    foreach ($line in ($help -split "`n")) {
        if ($line -match '^\*?\s*(Visual Studio \d+ \d{4})\s*=') {
            $gens += $Matches[1]
        }
    }
    return $gens
}

if (Test-Path $BuildDir) {
    Remove-Item -Recurse -Force $BuildDir
}

Write-Host "==> Configuring CMake ($Platform / $Config) in $BuildDir"

$configured = $false
$multiConfig = $false

# 1) Prefer a real Visual Studio generator (most reliable on windows-latest / VS 18)
$vsGens = Get-VsCMakeGenerators
Write-Host "    Available VS generators: $($vsGens -join ', ')"
foreach ($gen in $vsGens) {
    Write-Host "    Trying: $gen -A $VsArch"
    if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
    & cmake -S $WindowsDir -B $BuildDir -G $gen -A $VsArch -DBISPELL_BUILD_SHARED=ON
    if ($LASTEXITCODE -eq 0) {
        $configured = $true
        $multiConfig = $true
        break
    }
}

# 2) Ninja Multi-Config + MSVC (avoids single-config Ninja `$Config` lex bug with some CMake/MSVC combos)
if (-not $configured -and (Test-Command "ninja") -and (Test-Command "cl")) {
    Write-Host "    Trying: Ninja Multi-Config"
    if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
    & cmake -S $WindowsDir -B $BuildDir -G "Ninja Multi-Config" -DBISPELL_BUILD_SHARED=ON
    if ($LASTEXITCODE -eq 0) {
        $configured = $true
        $multiConfig = $true
    }
}

# 3) Classic single-config Ninja + CMAKE_BUILD_TYPE
if (-not $configured -and (Test-Command "ninja") -and (Test-Command "cl")) {
    Write-Host "    Trying: Ninja single-config"
    if (Test-Path $BuildDir) { Remove-Item -Recurse -Force $BuildDir }
    & cmake -S $WindowsDir -B $BuildDir -G Ninja `
        "-DCMAKE_BUILD_TYPE=$Config" `
        -DBISPELL_BUILD_SHARED=ON
    if ($LASTEXITCODE -eq 0) {
        $configured = $true
        $multiConfig = $false
    }
}

if (-not $configured) {
    throw "cmake configure failed: no usable generator (VS / Ninja Multi-Config / Ninja)."
}

Write-Host "==> Building bispell_core_shared (multiConfig=$multiConfig)"
if ($multiConfig) {
    & cmake --build $BuildDir --config $Config --target bispell_core_shared
} else {
    & cmake --build $BuildDir --target bispell_core_shared
}
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

$candidates = @(
    (Join-Path $BuildDir "core\$Config\bispell_core.dll"),
    (Join-Path $BuildDir "core\bispell_core.dll"),
    (Join-Path $BuildDir "$Config\bispell_core.dll"),
    (Join-Path $BuildDir "bispell_core.dll")
)
$dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) {
    $found = Get-ChildItem -Path $BuildDir -Filter "bispell_core.dll" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($found) { $dll = $found.FullName }
}
if (-not $dll) {
    throw "bispell_core.dll not found after build. Searched:`n$($candidates -join "`n")"
}

New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Copy-Item -Force $dll (Join-Path $StageDir "bispell_core.dll")
Copy-Item -Force $dll (Join-Path $AppDir "native\bispell_core.dll")
Write-Host "==> Staged: $StageDir\bispell_core.dll"
