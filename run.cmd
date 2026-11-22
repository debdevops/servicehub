@echo off
rem ServiceHub for Windows Command Prompt (or double-click): runs run.ps1 without changing any system setting.
rem Extra words are passed on, for example:  run.cmd -Check     run.cmd -NoInstall
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
if errorlevel 1 pause
