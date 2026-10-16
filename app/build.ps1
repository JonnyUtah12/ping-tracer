# Builds dist\PingTracer.exe (self-contained: Node runtime + server + UI + native probe helper).
# Requires: Node 20+ on PATH, internet for `npx postject`. The probe helper is compiled with the
# .NET Framework compiler that ships with Windows (no SDK needed).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$fw\csc.exe" /nologo /target:exe /optimize /r:System.Security.dll /out:helper\pphelper.exe helper\pphelper.cs
& "$fw\csc.exe" /nologo /target:winexe /optimize /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:helper\pptray.exe helper\pptray.cs
New-Item -ItemType Directory -Force dist | Out-Null
node --experimental-sea-config sea-config.json
Copy-Item (Get-Command node).Source dist\PingTracer.exe -Force
npx -y postject@1.0.0-alpha.6 dist\PingTracer.exe NODE_SEA_BLOB dist\sea-prep.blob --sentinel-fuse NODE_SEA_FUSE_fce680ab2cc467b6e072b8b5df1996b2
Remove-Item dist\sea-prep.blob
Write-Host "Built dist\PingTracer.exe"
