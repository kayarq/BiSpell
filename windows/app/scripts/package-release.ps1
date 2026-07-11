<#
.SYNOPSIS
  Local equivalent of CI windows-release: native DLL + publish + zip under dist/.

  Prefer GitHub Actions (.github/workflows/windows-release.yml) if you do not want
  VS/CMake on your C: drive — runners build and attach the setup + zip to a Release.

  Zip packaging is always produced. If Inno Setup (iscc) is available, also builds
  BiSpell-{Version}-win-x64-setup.exe (+ stable BiSpell-win-x64-setup.exe) via
  build-installer.ps1 (optional locally; CI installs Inno and always builds the setup).
#>
param(
    [string]$Version = "0.1.0",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$OutRoot = "",
    # When $true, fail if iscc is missing. Default $false: zip still succeeds without Inno.
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$AppDir = Split-Path -Parent $PSScriptRoot
$WindowsDir = Split-Path -Parent $AppDir
$RepoRoot = Split-Path -Parent $WindowsDir
if (-not $OutRoot) { $OutRoot = Join-Path $RepoRoot "dist" }

Set-Location $RepoRoot
& (Join-Path $PSScriptRoot "build-native.ps1") -Platform $Platform -Config Release

$publishDir = Join-Path $OutRoot "BiSpell-win-$Platform"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish (Join-Path $AppDir "BiSpell.App\BiSpell.App.csproj") `
  -c Release -r "win-$Platform" `
  -p:Platform=$Platform `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  --self-contained true `
  -o $publishDir

$dll = @(
  (Join-Path $AppDir "native\$Platform\bispell_core.dll"),
  (Join-Path $AppDir "native\bispell_core.dll")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dll) { throw "bispell_core.dll not found" }
Copy-Item -Force $dll (Join-Path $publishDir "bispell_core.dll")

$dictSrc = Join-Path $RepoRoot "Sources\BiSpellCore\Resources\Dictionaries"
$dictOut = Join-Path $publishDir "Dictionaries"
New-Item -ItemType Directory -Force -Path $dictOut | Out-Null
Copy-Item -Force (Join-Path $dictSrc "*") $dictOut

$zip = Join-Path $OutRoot "BiSpell-$Version-win-$Platform.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -Force
Write-Host "Package: $zip"

# Optional installer: call build-installer.ps1 when Inno Setup is present.
# CI uses explicit steps (choco + build-installer); this is a local convenience.
$buildInstaller = Join-Path $PSScriptRoot "build-installer.ps1"
$isccHint = $null
foreach ($name in @("iscc", "ISCC.exe")) {
    $c = Get-Command $name -ErrorAction SilentlyContinue
    if ($c -and $c.Source) { $isccHint = $c.Source; break }
}
if (-not $isccHint) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    )) {
        if ($p -and (Test-Path -LiteralPath $p)) { $isccHint = $p; break }
    }
}

if ($isccHint -or $RequireInstaller) {
    Write-Host "Building installer (iscc present or -RequireInstaller)..."
    # Throws on missing iscc / iscc failure / missing output (ErrorActionPreference Stop).
    # build-installer.ps1 also writes stable BiSpell-win-x64-setup.exe next to the versioned EXE.
    & $buildInstaller -SourceDir $publishDir -Version $Version -OutDir $OutRoot
    $setup = Join-Path $OutRoot "BiSpell-$Version-win-$Platform-setup.exe"
    if (-not (Test-Path -LiteralPath $setup)) {
        throw "Expected installer missing after build-installer.ps1: $setup"
    }
    $stable = Join-Path $OutRoot "BiSpell-win-$Platform-setup.exe"
    if (-not (Test-Path -LiteralPath $stable)) {
        Copy-Item -Force -LiteralPath $setup -Destination $stable
    }
    Write-Host "Installer: $setup"
    Write-Host "Stable alias: $stable"
} else {
    Write-Host "Skipping installer: iscc / Inno Setup 6 not found (zip only). Install Inno Setup or use CI."
}
