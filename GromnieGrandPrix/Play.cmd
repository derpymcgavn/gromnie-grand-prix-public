@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1" %*
set "game_exit=%errorlevel%"
if not "%game_exit%"=="0" if "%~1"=="" pause
exit /b %game_exit%
