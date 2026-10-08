# LapCont — Uninstaller — Preserve identities by default; explicit purge removes only verified product paths
# License: MIT
[CmdletBinding(SupportsShouldProcess)]
param([switch]$PurgePairings)
$ErrorActionPreference='Stop'
$owner=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$owner.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run from Administrator PowerShell.' }
$target=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'LapCont'))
$data=[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'LapCont/Service'))
if($target -ne ([IO.Path]::GetFullPath($env:ProgramFiles)+'\LapCont') -or $data -ne ([IO.Path]::GetFullPath($env:ProgramData)+'\LapCont\Service')) { throw 'Resolved product path check failed' }
if(!$PSCmdlet.ShouldProcess($target,'Stop and remove LapCont installation')) { return }
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq (Join-Path $target 'companion/LapCont.Session.exe') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if(Get-Service LapCont -ErrorAction SilentlyContinue) { Stop-Service LapCont; & sc.exe delete LapCont; if($LASTEXITCODE -ne 0) { throw 'Service removal failed' } }
Get-NetFirewallRule -Name LapCont-LAN -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name LapCont -ErrorAction SilentlyContinue
if(Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
if($PurgePairings -and (Test-Path -LiteralPath $data)) { Remove-Item -LiteralPath $data -Recurse -Force }
'Removed. Pairings were preserved unless -PurgePairings was requested. Remove companion startup in other Windows accounts separately.'
