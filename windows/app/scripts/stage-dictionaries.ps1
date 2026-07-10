<#
.SYNOPSIS
  Stage TR/EN dictionaries into windows/assets/Dictionaries from the Swift SoT.
#>
$ErrorActionPreference = "Stop"
$WindowsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RepoRoot = Split-Path -Parent $WindowsDir
$SoT = Join-Path $RepoRoot "Sources\BiSpellCore\Resources\Dictionaries"
$Dest = Join-Path $WindowsDir "assets\Dictionaries"

if (-not (Test-Path $SoT)) {
    Write-Error "Source of truth not found: $SoT"
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
Copy-Item -Force (Join-Path $SoT "*.dic") $Dest
Copy-Item -Force (Join-Path $SoT "*.aff") $Dest
Write-Host "Staged dictionaries to $Dest"
Get-ChildItem $Dest | Format-Table Name, Length
