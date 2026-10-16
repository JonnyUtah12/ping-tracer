@echo off
rem Runs the uninstaller from a temp copy so the install folder can be deleted.
copy /y "%~dp0uninstall.ps1" "%TEMP%\pp-uninstall.ps1" >nul
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%TEMP%\pp-uninstall.ps1"
