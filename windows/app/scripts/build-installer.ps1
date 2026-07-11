<#
.SYNOPSIS
  Compile the Inno Setup installer (BiSpell-*-win-x64-setup.exe) from a published payload tree.

.DESCRIPTION
  Resolves iscc (PATH or common install locations), then runs:
    iscc /DMyAppVersion=... /DSourceDir=... /DOutputDir=... windows\installer\BiSpell.iss

  Clear error if Inno Setup Compiler (iscc) is not available.

.PARAMETER SourceDir
  Unpackaged self-contained publish tree (e.g. dist\BiSpell-win-x64).

.PARAMETER Version
  Version label embedded in the setup EXE name and AppVersion.

.PARAMETER OutDir
  Directory for BiSpell-{Version}-win-x64-setup.exe (default: parent of SourceDir, or dist/).

.EXAMPLE
  .\windows\app\scripts\build-installer.ps1 -SourceDir dist\BiSpell-win-x64 -Version 0.2.1 -OutDir dist
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$AppDir = Split-Path -Parent $PSScriptRoot
$WindowsDir = Split-Path -Parent $AppDir
$RepoRoot = Split-Path -Parent $WindowsDir
$IssPath = Join-Path $WindowsDir "installer\BiSpell.iss"

function Resolve-Iscc {
    $cmd = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        return $cmd.Source
    }
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        return $cmd.Source
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath $p)) {
            return $p
        }
    }
    return $null
}

if (-not (Test-Path -LiteralPath $IssPath)) {
    throw "Inno script not found: $IssPath"
}

if (-not (Test-Path -LiteralPath $SourceDir)) {
    throw "SourceDir not found: $SourceDir"
}
$SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path

$exeProbe = Join-Path $SourceDir "BiSpell.App.exe"
if (-not (Test-Path -LiteralPath $exeProbe)) {
    throw "SourceDir missing BiSpell.App.exe: $SourceDir"
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $parent = Split-Path -Parent $SourceDir
    if ($parent) { $OutDir = $parent } else { $OutDir = Join-Path $RepoRoot "dist" }
}
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version must be a non-empty string"
}

$iscc = Resolve-Iscc
if (-not $iscc) {
    throw @"
Inno Setup Compiler (iscc) not found on PATH and not under common install locations.

Install Inno Setup 6 and ensure ISCC.exe is on PATH, e.g.:
  - Local: https://jrsoftware.org/isdl.php  (add 'C:\Program Files (x86)\Inno Setup 6' to PATH)
  - CI:    choco install innosetup -y

Then re-run:
  .\windows\app\scripts\build-installer.ps1 -SourceDir <payload> -Version <ver> -OutDir <dir>
"@
}

$setupName = "BiSpell-$Version-win-x64-setup.exe"
$setupPath = Join-Path $OutDir $setupName
if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -Force -LiteralPath $setupPath
}

Write-Host "==> Compiling installer with iscc"
Write-Host "    iscc      = $iscc"
Write-Host "    iss       = $IssPath"
Write-Host "    Version   = $Version"
Write-Host "    SourceDir = $SourceDir"
Write-Host "    OutDir    = $OutDir"

# Inno resolves relative SourceDir/OutputDir against the .iss directory; pass absolute paths.
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DSourceDir=$SourceDir" `
    "/DOutputDir=$OutDir" `
    $IssPath

if ($LASTEXITCODE -ne 0) {
    throw "iscc failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Expected setup EXE missing after iscc: $setupPath"
}

$item = Get-Item -LiteralPath $setupPath
Write-Host "Installer: $($item.FullName) ($([math]::Round($item.Length / 1MB, 2)) MB)"

# Stable alias for CI / docs convenience (same bytes as versioned setup).
$stableName = "BiSpell-win-x64-setup.exe"
$stablePath = Join-Path $OutDir $stableName
Copy-Item -Force -LiteralPath $setupPath -Destination $stablePath
Write-Host "Stable alias: $stablePath"
