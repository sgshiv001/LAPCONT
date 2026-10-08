# LapCont — Development — Explicit local user coordinator and visible companion, no machine installation
# License: MIT
param([string]$DataDirectory=(Join-Path $env:LOCALAPPDATA 'LapCont/Development'),[ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)) { $dotnet=(Get-Command dotnet -ErrorAction Stop).Source }
$service=Join-Path $root "windows/LapCont.Service/bin/$Configuration/net8.0-windows10.0.19041.0/LapCont.Service.dll"
$companion=Join-Path $root "windows/LapCont.Session/bin/$Configuration/net8.0-windows10.0.19041.0/LapCont.Session.dll"
foreach($file in @($service,$companion)) { if(!(Test-Path -LiteralPath $file)) { throw "Build the Windows $Configuration solution first." } }
if(Get-NetTCPConnection -State Listen -LocalPort 4433 -ErrorAction SilentlyContinue) { throw 'Port 4433 is already in use. Close the existing development host or use the installed companion.' }
$data=[IO.Path]::GetFullPath($DataDirectory)
New-Item -ItemType Directory -Path $data -Force | Out-Null
$hostProcess=Start-Process -FilePath $dotnet -ArgumentList ('"{0}" --console --data-directory "{1}"' -f $service,$data) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $data 'host-output.txt') -RedirectStandardError (Join-Path $data 'host-error.txt') -PassThru
$companionProcess=Start-Process -FilePath $dotnet -ArgumentList ('"{0}" --development' -f $companion) -WindowStyle Hidden -PassThru
@{service_pid=$hostProcess.Id;companion_pid=$companionProcess.Id;data_directory=$data} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'development-processes.json')
'Development host and companion started. Pair locally. This mode does not receive SCM session callbacks; use the installed service for authoritative lock-event testing.'
