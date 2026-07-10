@echo off
setlocal
set PLATFORM=%~1
if "%PLATFORM%"=="" set PLATFORM=x64
set CONFIG=%~2
if "%CONFIG%"=="" set CONFIG=Release
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-native.ps1" -Platform %PLATFORM% -Config %CONFIG%
exit /b %ERRORLEVEL%
