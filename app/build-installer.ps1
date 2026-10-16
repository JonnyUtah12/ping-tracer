# Builds dist\PingTracer-Setup.exe with Windows' built-in IExpress (no extra tools). Run build.ps1 first.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$exe = Join-Path $PSScriptRoot 'dist\PingTracer.exe'
if (-not (Test-Path $exe)) { throw 'dist\PingTracer.exe not found - run build.ps1 first' }
$version = '1.0.0'
$stage = Join-Path $env:TEMP 'pp-setup-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item $exe $stage
Copy-Item installer\install.ps1, installer\install.cmd, installer\uninstall.ps1, installer\uninstall.cmd $stage
Set-Content (Join-Path $stage 'version.txt') $version
$final = Join-Path $PSScriptRoot 'dist\PingTracer-Setup.exe'
$out = Join-Path $env:TEMP 'PingTracer-Setup.exe'   # built at a space-free path (IExpress is picky), then copied
if (Test-Path $out) { Remove-Item $out -Force }
$files = 'PingTracer.exe', 'install.ps1', 'install.cmd', 'uninstall.ps1', 'uninstall.cmd', 'version.txt'
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Install PingTracer $version for the current user?
DisplayLicense=
FinishMessage=
TargetName=$out
FriendlyName=PingTracer Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd
UserQuietInstCmd=install.cmd
$((0..($files.Count - 1) | ForEach-Object { "FILE$_=`"$($files[$_])`"" }) -join "`r`n")
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
$((0..($files.Count - 1) | ForEach-Object { "%FILE$_%=" }) -join "`r`n")
"@
$sedPath = Join-Path $stage 'setup.sed'
Set-Content $sedPath $sed -Encoding ASCII
$p = Start-Process "$env:WINDIR\System32\iexpress.exe" -ArgumentList '/N', '/Q', $sedPath -Wait -PassThru -WindowStyle Hidden
Write-Host "iexpress exit code $($p.ExitCode)"
if (-not (Test-Path $out)) { throw 'IExpress did not produce the setup file' }
Copy-Item $out $final -Force
Write-Host ("Built {0} ({1:N1} MB)" -f $final, ((Get-Item $final).Length / 1MB))
