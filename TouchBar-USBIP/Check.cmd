@echo off
pwsh.exe -NoProfile -File "%~dp0Launch.ps1" -Mode Check
type "%~dp0launcher.log"
pause
