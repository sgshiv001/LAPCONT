# LapCont — Installer — Administrator service/firewall setup with explicit user companion startup
# License: MIT
[CmdletBinding(SupportsShouldProcess)]
param([string]$PackageDirectory=$PSScriptRoot,[switch]$CompanionAtSignIn,[switch]$ResumeIncompleteInstall)
$ErrorActionPreference='Stop'
$owner=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$owner.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installer from an Administrator PowerShell window.' }
$source=[IO.Path]::GetFullPath($PackageDirectory)
foreach($file in @('service/LapCont.Service.exe','companion/LapCont.Session.exe')) { if(!(Test-Path -LiteralPath (Join-Path $source $file))) { throw 'Use the generated Windows release package.' } }
$runtimes=& dotnet --list-runtimes
if(!($runtimes -match 'Microsoft.WindowsDesktop.App 8\.') -or !($runtimes -match 'Microsoft.AspNetCore.App 8\.')) { throw 'Install x64 .NET 8 Desktop and ASP.NET Core runtimes first. See docs/setup.md.' }
if(Get-Service LapCont -ErrorAction SilentlyContinue) { throw 'LapCont is already installed. Uninstall or perform a documented upgrade preserving ProgramData first.' }
$target=Join-Path $env:ProgramFiles 'LapCont'
if(!$PSCmdlet.ShouldProcess($target,'Install LapCont service, companion and private-network listener')) { return }
if(Test-Path -LiteralPath $target) {
  if(!$ResumeIncompleteInstall) { throw 'The installation directory already exists. Use -ResumeIncompleteInstall only for a verified incomplete install of this same package.' }
  if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing a redirected installation directory.' }
  foreach($existing in (Get-ChildItem -LiteralPath $target -File -Recurse)) {
    $relative=$existing.FullName.Substring($target.Length+1)
    if($relative -notmatch '^(service|companion)\\') { throw 'Unexpected file in incomplete installation.' }
    $expected=Join-Path $source $relative
    if(!(Test-Path -LiteralPath $expected) -or (Get-FileHash -LiteralPath $existing.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash) { throw 'Incomplete installation does not match this package.' }
  }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
$targetAcl=[Security.AccessControl.DirectorySecurity]::new(); $targetAcl.SetAccessRuleProtection($true,$false)
foreach($rule in @(@{sid='S-1-5-18';rights='FullControl'},@{sid='S-1-5-32-544';rights='FullControl'},@{sid='S-1-5-32-545';rights='ReadAndExecute'})) {
  $targetAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($rule.sid),$rule.rights,'ContainerInherit,ObjectInherit','None','Allow'))
}
Set-Acl -LiteralPath $target -AclObject $targetAcl
Copy-Item -LiteralPath (Join-Path $source 'service'),(Join-Path $source 'companion') -Destination $target -Recurse -Force
$servicePath=Join-Path $target 'service/LapCont.Service.exe'
if($servicePath.Contains('"')) { throw 'Invalid service executable path.' }
# Preserve the executable's embedded quotes on Windows PowerShell 5.1 and PowerShell 7.
$create=[Diagnostics.ProcessStartInfo]::new()
$create.FileName=Join-Path $env:SystemRoot 'System32/sc.exe'
$create.Arguments='create LapCont binPath= "\"{0}\" --product-service" start= auto obj= LocalSystem DisplayName= "LapCont secure coordinator"' -f $servicePath
$create.UseShellExecute=$false; $create.CreateNoWindow=$true; $create.RedirectStandardOutput=$true; $create.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($create)
$creationOutput=$process.StandardOutput.ReadToEnd(); $creationError=$process.StandardError.ReadToEnd(); $process.WaitForExit()
if($process.ExitCode -ne 0) { throw "Service installation failed ($($process.ExitCode)): $creationOutput $creationError" }
$process.Dispose()
& sc.exe description LapCont 'Coordinates enrolled phones; desktop actions require the per-user companion.'
& sc.exe failure LapCont reset= 86400 actions= restart/5000/restart/30000/restart/60000
New-NetFirewallRule -Name 'LapCont-LAN' -DisplayName 'LapCont authenticated LAN control' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 4433 -Profile Private -RemoteAddress LocalSubnet -Program $servicePath | Out-Null
Start-Service LapCont
if($CompanionAtSignIn) { New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name LapCont -Value ('"'+(Join-Path $target 'companion/LapCont.Session.exe')+'"') -PropertyType String -Force | Out-Null }
'Installed. Open the companion in each Windows user session that will pair a phone. Automatic startup applies only to the installing Windows user.'
