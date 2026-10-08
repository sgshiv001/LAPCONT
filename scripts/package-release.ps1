# LapCont — Release — Framework-dependent Windows bundle and unsigned Android release APK
# License: MIT
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)) { $dotnet='dotnet' }
$out=Join-Path $root 'artifacts/release-v0.1.0'
New-Item -ItemType Directory -Path $out -Force | Out-Null
foreach($component in @('Service','Session')) {
  $folder=if($component -eq 'Service') { 'service' } else { 'companion' }
  & $dotnet publish (Join-Path $root "windows/LapCont.$component/LapCont.$component.csproj") -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $out $folder)
  if($LASTEXITCODE -ne 0) { throw "Publish failed: $component" }
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'),(Join-Path $root 'README.md'),(Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'docs'),(Join-Path $root 'licenses') -Destination $out -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts/install-windows.ps1'),(Join-Path $root 'scripts/uninstall-windows.ps1') -Destination $out -Force
Compress-Archive -Path (Join-Path $out '*') -DestinationPath (Join-Path $root 'artifacts/LapCont-Windows-v0.1.0.zip') -Force
'Windows package created; x64 .NET 8 Desktop and ASP.NET Core runtimes are separate prerequisites.'
