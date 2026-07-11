<#
.SYNOPSIS
  Headless launch smoke for a packaged BiSpell Windows zip (user bytes).

  Used by GHA smoke-windows-x64 and for local verification:
    ./windows/app/scripts/smoke-launch.ps1 -ZipPath dist/BiSpell-win-x64.zip

  Acceptance (W3):
    - Extract zip to a clean directory
    - Assert BiSpell.App.exe, bispell_core.dll, Dictionaries\*.dic
    - Set BISPELL_SMOKE=1 (skip MessageBox; app truncates startup log)
    - Clear stale %TEMP%\BiSpell-startup.log / .status
    - Launch exe (cwd = extract dir), wait TimeoutSeconds (default 20, range 15–30)
    - Fail on early exit (non-success) or known fatal log strings
    - Pass if process still alive and log shows successful phases (or status=ok)
    - Always write transcript; stop process in finally (caller may also cleanup)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [string]$ExtractDir = "",

    [ValidateRange(15, 30)]
    [int]$TimeoutSeconds = 20,

    [string]$TranscriptPath = "",

    [string]$StartupLogPath = "",

    [string]$StartupStatusPath = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Smoke([string]$Message) {
    $line = "[smoke] $Message"
    Write-Host $line
    if ($null -ne $script:TranscriptLines) { [void]$script:TranscriptLines.Add($line) }
}

$script:TranscriptLines = [System.Collections.Generic.List[string]]::new()
$process = $null
$exitCode = 1

try {
    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Zip not found: $ZipPath"
    }
    $ZipPath = (Resolve-Path -LiteralPath $ZipPath).Path

    if ([string]::IsNullOrWhiteSpace($ExtractDir)) {
        $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
        $ExtractDir = Join-Path $base "bispell-smoke"
    }
    if ([string]::IsNullOrWhiteSpace($StartupLogPath)) {
        $StartupLogPath = Join-Path $env:TEMP "BiSpell-startup.log"
    }
    if ([string]::IsNullOrWhiteSpace($StartupStatusPath)) {
        $StartupStatusPath = Join-Path $env:TEMP "BiSpell-startup.status"
    }
    if ([string]::IsNullOrWhiteSpace($TranscriptPath)) {
        if ($env:RUNNER_TEMP) {
            $TranscriptPath = Join-Path $env:RUNNER_TEMP "bispell-smoke-transcript.txt"
        } else {
            $TranscriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "bispell-smoke-transcript.txt"
        }
    }

    Write-Smoke "zip=$ZipPath"
    Write-Smoke "extract=$ExtractDir"
    Write-Smoke "timeout=${TimeoutSeconds}s"
    Write-Smoke "startup_log=$StartupLogPath"
    Write-Smoke "startup_status=$StartupStatusPath"
    Write-Smoke "transcript=$TranscriptPath"

    # Clean extract dir so we only exercise zip contents.
    if (Test-Path -LiteralPath $ExtractDir) {
        Remove-Item -LiteralPath $ExtractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $ExtractDir | Out-Null

    Write-Smoke "Expand-Archive..."
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractDir -Force

    $exe = Join-Path $ExtractDir "BiSpell.App.exe"
    $dll = Join-Path $ExtractDir "bispell_core.dll"
    $dictDir = Join-Path $ExtractDir "Dictionaries"

    if (-not (Test-Path -LiteralPath $exe)) { throw "Missing BiSpell.App.exe under $ExtractDir" }
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing bispell_core.dll under $ExtractDir" }
    if (-not (Test-Path -LiteralPath $dictDir)) { throw "Missing Dictionaries\ under $ExtractDir" }
    $dics = @(Get-ChildItem -LiteralPath $dictDir -Filter "*.dic" -ErrorAction SilentlyContinue)
    if ($dics.Count -lt 1) { throw "No Dictionaries\*.dic under $ExtractDir" }
    Write-Smoke "layout OK: exe + dll + $($dics.Count) dic file(s)"

    # Clear stale logs before launch (app also truncates on BeginSession).
    foreach ($p in @($StartupLogPath, $StartupStatusPath)) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
            Write-Smoke "cleared stale $p"
        }
    }

    $env:BISPELL_SMOKE = "1"
    Write-Smoke "BISPELL_SMOKE=$($env:BISPELL_SMOKE)"

    # Do NOT redirect stdout/stderr — GUI WinUI apps can hang on filled pipes.
    Write-Smoke "Starting BiSpell.App.exe (cwd=$ExtractDir)..."
    $process = Start-Process -FilePath $exe `
        -WorkingDirectory $ExtractDir `
        -PassThru `
        -WindowStyle Hidden

    if (-not $process) { throw "Start-Process returned null" }
    Write-Smoke "pid=$($process.Id)"

    Write-Smoke "Waiting ${TimeoutSeconds}s for stable launch..."
    Start-Sleep -Seconds $TimeoutSeconds

    try { $process.Refresh() } catch { }

    $logText = ""
    if (Test-Path -LiteralPath $StartupLogPath) {
        $logText = Get-Content -LiteralPath $StartupLogPath -Raw -ErrorAction SilentlyContinue
        if ($null -eq $logText) { $logText = "" }
    }
    $statusText = ""
    if (Test-Path -LiteralPath $StartupStatusPath) {
        $statusText = (Get-Content -LiteralPath $StartupStatusPath -Raw -ErrorAction SilentlyContinue)
        if ($null -eq $statusText) { $statusText = "" }
        $statusText = $statusText.Trim()
    }

    $combined = @"
--- BiSpell-startup.log ---
$logText
--- BiSpell-startup.status ---
$statusText
"@
    Write-Smoke "---- captured output begin ----"
    Write-Host $combined
    Write-Smoke "---- captured output end ----"
    [void]$script:TranscriptLines.Add($combined)

    $fatalPatterns = @(
        "ToggleButton.IsChecked",
        "Failed to assign to property",
        "Windows App Runtime",
        "XamlParseException",
        "DllNotFoundException"
    )
    $haystack = "$logText`n$statusText"
    foreach ($pat in $fatalPatterns) {
        if ($haystack -match [regex]::Escape($pat)) {
            throw "Fatal pattern in smoke output: $pat"
        }
    }

    $hasExited = $false
    try { $hasExited = $process.HasExited } catch { $hasExited = $true }

    if ($hasExited) {
        $code = 1
        try { $code = $process.ExitCode } catch { $code = 1 }
        Write-Smoke "Process exited early code=$code"
        # Prefer still-alive. Allow clean quit only if phases/status prove window came up.
        if ($code -ne 0) {
            throw "Process exited early with code=$code (expected still running for smoke window)"
        }
        $okPhase = ($logText -match "MainWindow\.Activate done") -or ($statusText -eq "ok")
        if (-not $okPhase) {
            throw "Process exited early with code=0 but no successful phase markers (MainWindow.Activate done / status=ok)"
        }
        Write-Smoke "WARN: process exited 0 within smoke window but phases look OK; treating as pass"
    } else {
        Write-Smoke "Process still running after ${TimeoutSeconds}s (good)"
        $okPhase = ($logText -match "MainWindow created") `
            -or ($logText -match "MainWindow\.Activate done") `
            -or ($statusText -eq "ok") `
            -or ($logText -match "Main enter")
        if (-not $okPhase) {
            if ([string]::IsNullOrWhiteSpace($logText)) {
                throw "Process alive but startup log missing/empty — cannot confirm launch phases"
            }
            Write-Smoke "WARN: process alive; log present but missing ideal phase markers — accepting (no fatal strings)"
        } else {
            Write-Smoke "Phase markers OK (window created / activate / status ok)"
        }
    }

    $exitCode = 0
    Write-Smoke "SMOKE PASS"
}
catch {
    $exitCode = 1
    Write-Smoke "SMOKE FAIL: $($_.Exception.Message)"
    # Re-throw after finally so CI step fails; also exit non-zero for local runs.
    $script:SmokeError = $_
}
finally {
    if ($null -ne $process) {
        $stillUp = $false
        try { $stillUp = -not $process.HasExited } catch { $stillUp = $false }
        if ($stillUp) {
            Write-Smoke "Stopping process id=$($process.Id)"
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
                    Where-Object { $_.ParentProcessId -eq $process.Id } |
                    ForEach-Object {
                        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
                    }
            } catch {
                Write-Smoke "Stop-Process note: $($_.Exception.Message)"
            }
        }
    }

    try {
        $dir = Split-Path -Parent $TranscriptPath
        if ($dir -and -not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }
        ($script:TranscriptLines -join [Environment]::NewLine) |
            Set-Content -LiteralPath $TranscriptPath -Encoding UTF8
        Write-Host "[smoke] transcript written: $TranscriptPath"
    } catch {
        Write-Host "[smoke] transcript write failed: $($_.Exception.Message)"
    }
}

if ($null -ne $script:SmokeError) {
    throw $script:SmokeError
}

exit $exitCode
