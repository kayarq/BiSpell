@echo off
cd /d "%~dp0"
echo Starting BiSpell from:
echo   %CD%
echo.
echo If you see "Windows App Runtime" missing: use the 0.1.2+ zip (self-contained).
echo Startup log:    %TEMP%\BiSpell-startup.log
echo Startup status: %TEMP%\BiSpell-startup.status  (ok / fail:...)
echo Exit codes: 0 = clean quit; 1 = startup failure
echo Smoke: set BISPELL_SMOKE=1 to skip error MessageBoxes (CI).
echo.
BiSpell.App.exe
set ERR=%ERRORLEVEL%
echo.
echo Exit code: %ERR%
if exist "%TEMP%\BiSpell-startup.status" (
  echo ---- %TEMP%\BiSpell-startup.status ----
  type "%TEMP%\BiSpell-startup.status"
)
if exist "%TEMP%\BiSpell-startup.log" (
  echo ---- %TEMP%\BiSpell-startup.log ----
  type "%TEMP%\BiSpell-startup.log"
)
pause
