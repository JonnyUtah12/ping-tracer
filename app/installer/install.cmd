@echo off
rem Launched by the IExpress setup package from its extraction folder. Output is logged for troubleshooting.
cd /d "%~dp0"
echo [%date% %time%] setup started in %CD% >> "%TEMP%\pp-setup.log"
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0install.ps1" >> "%TEMP%\pp-setup.log" 2>&1
echo [%date% %time%] install.ps1 exit code %ERRORLEVEL% >> "%TEMP%\pp-setup.log"
