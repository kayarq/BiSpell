@echo off
cd /d "%~dp0"
echo Starting BiSpell from %CD%
echo If the window never appears, open: %%TEMP%%\BiSpell-startup.log
echo.
BiSpell.App.exe
echo.
echo Exit code: %ERRORLEVEL%
echo Log file: %TEMP%\BiSpell-startup.log
if exist "%TEMP%\BiSpell-startup.log" type "%TEMP%\BiSpell-startup.log"
pause
