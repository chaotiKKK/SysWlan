@echo off
if not exist "%~dp0artifacts\windows\SysWlan.App.exe" (
  echo Bitte zuerst powershell -File scripts\build.ps1 ausfuehren.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\windows\SysWlan.App.exe"
