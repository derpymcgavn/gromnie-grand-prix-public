@echo off
setlocal
title Gromnie Grand Prix Connection Check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-GGPrix-Server.ps1" -Check %*
set "relay_exit=%errorlevel%"
if "%~1"=="" pause
exit /b %relay_exit%
