<#
.SYNOPSIS
  Local equivalent of CI windows-release: native DLL + publish + zip under dist/.

  Prefer GitHub Actions (.github/workflows/windows-release.yml) if you do not want
  VS/CMake on your C: drive — runners build and attach the zip to a Release.
#>
param(
    [string]$Version = "0.1.0",
    [ValidateSet("x64")]
    [string]$Platform = "x64",
    [string]$OutRoot = ""
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
