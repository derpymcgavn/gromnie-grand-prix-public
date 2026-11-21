@echo off
setlocal
title Gromnie Grand Prix Server
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-GGPrix-Server.ps1" %*
set "relay_exit=%errorlevel%"
if not "%relay_exit%"=="0" if "%~1"=="" pause
exit /b %relay_exit%
