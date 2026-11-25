@echo off
rem ServiceHub - Windows launcher for Command Prompt AND PowerShell.
rem   Command Prompt:  run            (or run.cmd)
rem   PowerShell:      .\run.cmd
rem Hands over to run.ps1 with the execution policy bypassed for this one process only (nothing is changed
rem system-wide), so a fresh Windows machine needs no setup first. Arguments pass straight through: -Check, -NoInstall, ...
setlocal
where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo x powershell.exe was not found. Windows PowerShell 5.1 ships with Windows 10/11 - repair Windows, or install PowerShell 7: https://aka.ms/powershell
  exit /b 1
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
exit /b %ERRORLEVEL%
