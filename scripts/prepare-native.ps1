# LapCont — Build — Verified native Opus assets for Android probes
# License: MIT
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools\native-opus'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
$taskZip = Join-Path $taskTools 'opussharp.natives.1.6.1.4.zip'
if (!(Test-Path -LiteralPath $taskZip)) {
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/opussharp.natives/1.6.1.4/opussharp.natives.1.6.1.4.nupkg' -OutFile $taskZip
}
$taskExpected = '6FC12E30910DFE87A0DF0C62C7F1D76A869301C8692EC8D5331841C5F2D2EEAA'
if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash -ne $taskExpected) { throw 'Native package checksum mismatch' }
Expand-Archive -LiteralPath $taskZip -DestinationPath (Join-Path $taskTools 'package') -Force
foreach ($taskAbi in @(@('arm64-v8a', 'android-arm64'), @('x86_64', 'android-x64'))) {
    $taskDestination = Join-Path $taskRoot ('android\app\src\main\jniLibs\' + $taskAbi[0])
    New-Item -ItemType Directory -Force -Path $taskDestination | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskTools ('package\runtimes\' + $taskAbi[1] + '\native\libopus.so')) -Destination $taskDestination
}
$taskNotices = Join-Path $taskRoot 'android\app\src\main\assets\notices'
New-Item -ItemType Directory -Force -Path $taskNotices | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination $taskNotices
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'licenses') -File | Copy-Item -Destination $taskNotices
Write-Output 'Prepared pinned Opus native assets: arm64 baseline and x86_64 supplementary emulator probe.'
Write-Output 'See THIRD_PARTY_NOTICES.md; these native libraries are not licensed under the project MIT license.'
