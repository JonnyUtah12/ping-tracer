# PingTracer per-user installer (no administrator rights needed). Run by the IExpress setup package.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$silent = ($env:PP_SETUP_SILENT -eq '1')   # test switch: no dialogs, don't launch the app
$src = $PSScriptRoot
$version = (Get-Content (Join-Path $src 'version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }
$dir = Join-Path $env:LOCALAPPDATA 'Programs\PingTracer'
$exe = Join-Path $dir 'PingTracer.exe'
try {
  # an upgrade replaces the exe, so stop any running copy first (user data is stored elsewhere and is kept)
  Get-Process PingTracer, pphelper, pptray -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 800
  # migrate from previous product names ("Ping Plotter", "Pong Plotter"): keep data, remove old program/shortcuts/registry entries
  $newData = Join-Path $env:LOCALAPPDATA 'PingTracer'
  foreach ($legacy in @('Ping Plotter', 'Pong Plotter')) {
    $oldProg = Join-Path $env:LOCALAPPDATA ('Programs\' + $legacy); $oldData = Join-Path $env:LOCALAPPDATA $legacy
    if ((Test-Path $oldData) -and -not (Test-Path $newData)) { Move-Item $oldData $newData -ErrorAction SilentlyContinue }
    Get-Process ($legacy -replace ' ','') -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ($legacy -replace '[ .]','') -ErrorAction SilentlyContinue
    Remove-Item ('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + ($legacy -replace '[ .]','')) -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:APPDATA ('Microsoft\Windows\Start Menu\Programs\' + $legacy + '.lnk')) -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) ($legacy + '.lnk')) -Force -ErrorAction SilentlyContinue
    if (Test-Path $oldProg) { Remove-Item $oldProg -Recurse -Force -ErrorAction SilentlyContinue }
  }
  New-Item -ItemType Directory -Force $dir | Out-Null
  Copy-Item (Join-Path $src 'PingTracer.exe') $exe -Force
  Copy-Item (Join-Path $src 'uninstall.ps1') $dir -Force
  Copy-Item (Join-Path $src 'uninstall.cmd') $dir -Force
  Set-Content (Join-Path $dir 'installed.flag') $version   # tells the app to keep its data in %LOCALAPPDATA%\PingTracer

  $wsh = New-Object -ComObject WScript.Shell
  $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
  foreach ($lnkPath in @((Join-Path $startMenu 'PingTracer.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'PingTracer.lnk'))) {
    $l = $wsh.CreateShortcut($lnkPath); $l.TargetPath = $exe; $l.WorkingDirectory = $dir; $l.Description = 'Continuous traceroute and ping monitor'; $l.IconLocation = "$exe,0"; $l.Save()
  }

  $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PingTracer'
  New-Item -Path $key -Force | Out-Null
  $size = [int]((Get-Item $exe).Length / 1KB)
  $props = @{ DisplayName = 'PingTracer'; DisplayVersion = $version; Publisher = 'JonnyUtah12'; InstallLocation = $dir; DisplayIcon = $exe
              UninstallString = "`"$(Join-Path $dir 'uninstall.cmd')`""; EstimatedSize = $size }
  foreach ($k in $props.Keys) { New-ItemProperty -Path $key -Name $k -Value $props[$k] -Force | Out-Null }
  New-ItemProperty -Path $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

  if ($hadAutostart) { New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PingTracer' -Value "`"$exe`" --background" -Force | Out-Null }
  if (-not $silent) { Start-Process $exe }
  if (-not $silent) { [System.Windows.Forms.MessageBox]::Show("PingTracer was installed to:`n$dir`n`nShortcuts were added to the Start menu and desktop. It is starting now and keeps running in the system tray.", 'PingTracer setup', 'OK', 'Information') | Out-Null }
} catch {
  if (-not $silent) { [System.Windows.Forms.MessageBox]::Show("Installation failed:`n$($_.Exception.Message)", 'PingTracer setup', 'OK', 'Error') | Out-Null }
  Write-Error $_.Exception.Message
  exit 1
}
