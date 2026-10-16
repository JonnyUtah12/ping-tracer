# Pong Plotter uninstaller (per-user). Removes the program, shortcuts, autostart entry and uninstall entry; asks before deleting collected data.
Add-Type -AssemblyName System.Windows.Forms
$silent = ($env:PP_SETUP_SILENT -eq '1')   # test switch: assume Yes to removal, No to deleting data
$dir = Join-Path $env:LOCALAPPDATA 'Programs\Pong Plotter'
$data = Join-Path $env:LOCALAPPDATA 'Pong Plotter'
$ask = if ($silent) { 'Yes' } else { [System.Windows.Forms.MessageBox]::Show('Remove Pong Plotter from this computer?', 'Pong Plotter uninstall', 'YesNo', 'Question') }
if ($ask -ne 'Yes') { exit 0 }
$delData = $false
if ((Test-Path $data) -and -not $silent) {
  $delData = ([System.Windows.Forms.MessageBox]::Show("Also delete your collected data (history, targets, settings, saved sessions)?`n`n$data`n`nChoose No to keep it for a later reinstall.", 'Pong Plotter uninstall', 'YesNo', 'Warning') -eq 'Yes')
}
Get-Process PongPlotter, pphelper, pptray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PongPlotter' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PongPlotter' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Pong Plotter.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pong Plotter.lnk') -Force -ErrorAction SilentlyContinue
if ($delData) { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue }
# this script runs from the folder being removed, so finish the deletion from a detached cmd after a short delay
Start-Process cmd -ArgumentList '/c', "ping -n 3 127.0.0.1 >nul & rd /s /q `"$dir`"" -WindowStyle Hidden
if (-not $silent) { [System.Windows.Forms.MessageBox]::Show('Pong Plotter was removed.', 'Pong Plotter uninstall', 'OK', 'Information') | Out-Null }
