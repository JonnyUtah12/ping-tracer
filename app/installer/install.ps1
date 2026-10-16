# Pong Plotter per-user installer (no administrator rights needed). Run by the IExpress setup package.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$silent = ($env:PP_SETUP_SILENT -eq '1')   # test switch: no dialogs, don't launch the app
$src = $PSScriptRoot
$version = (Get-Content (Join-Path $src 'version.txt') -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $version) { $version = '1.4.0' }
$dir = Join-Path $env:LOCALAPPDATA 'Programs\Pong Plotter'
$exe = Join-Path $dir 'PongPlotter.exe'
try {
  # an upgrade replaces the exe, so stop any running copy first (user data is stored elsewhere and is kept)
  Get-Process PongPlotter, pphelper, pptray -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 800
  # migrate from the product's previous name ("Ping Plotter"): keep its data, remove its program, shortcuts and registry entries
  $oldProg = Join-Path $env:LOCALAPPDATA 'Programs\Ping Plotter'; $oldData = Join-Path $env:LOCALAPPDATA 'Ping Plotter'; $newData = Join-Path $env:LOCALAPPDATA 'Pong Plotter'
  $hadAutostart = $null -ne (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PingPlotter' -ErrorAction SilentlyContinue)
  Get-Process PingPlotter -ErrorAction SilentlyContinue | Stop-Process -Force
  if ((Test-Path $oldData) -and -not (Test-Path $newData)) { Move-Item $oldData $newData -ErrorAction SilentlyContinue }
  Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PingPlotter' -ErrorAction SilentlyContinue
  Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PingPlotter' -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Ping Plotter.lnk') -Force -ErrorAction SilentlyContinue
  Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Ping Plotter.lnk') -Force -ErrorAction SilentlyContinue
  if (Test-Path $oldProg) { Remove-Item $oldProg -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory -Force $dir | Out-Null
  Copy-Item (Join-Path $src 'PongPlotter.exe') $exe -Force
  Copy-Item (Join-Path $src 'uninstall.ps1') $dir -Force
  Copy-Item (Join-Path $src 'uninstall.cmd') $dir -Force
  Set-Content (Join-Path $dir 'installed.flag') $version   # tells the app to keep its data in %LOCALAPPDATA%\Pong Plotter

  $wsh = New-Object -ComObject WScript.Shell
  $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
  foreach ($lnkPath in @((Join-Path $startMenu 'Pong Plotter.lnk'), (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pong Plotter.lnk'))) {
    $l = $wsh.CreateShortcut($lnkPath); $l.TargetPath = $exe; $l.WorkingDirectory = $dir; $l.Description = 'Continuous traceroute and ping monitor'; $l.IconLocation = "$exe,0"; $l.Save()
  }

  $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PongPlotter'
  New-Item -Path $key -Force | Out-Null
  $size = [int]((Get-Item $exe).Length / 1KB)
  $props = @{ DisplayName = 'Pong Plotter'; DisplayVersion = $version; Publisher = 'Unofficial'; InstallLocation = $dir; DisplayIcon = $exe
              UninstallString = "`"$(Join-Path $dir 'uninstall.cmd')`""; EstimatedSize = $size }
  foreach ($k in $props.Keys) { New-ItemProperty -Path $key -Name $k -Value $props[$k] -Force | Out-Null }
  New-ItemProperty -Path $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

  if ($hadAutostart) { New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PongPlotter' -Value "`"$exe`" --background" -Force | Out-Null }
  if (-not $silent) { Start-Process $exe }
  if (-not $silent) { [System.Windows.Forms.MessageBox]::Show("Pong Plotter was installed to:`n$dir`n`nShortcuts were added to the Start menu and desktop. It is starting now and keeps running in the system tray.", 'Pong Plotter setup', 'OK', 'Information') | Out-Null }
} catch {
  if (-not $silent) { [System.Windows.Forms.MessageBox]::Show("Installation failed:`n$($_.Exception.Message)", 'Pong Plotter setup', 'OK', 'Error') | Out-Null }
  Write-Error $_.Exception.Message
  exit 1
}
