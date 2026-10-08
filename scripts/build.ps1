# LapCont — Build — Reproducible Phase 0 build and deterministic tests
# License: MIT
param([switch]$Android, [switch]$PrepareNative)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
Push-Location $taskRoot
try {
    & $taskDotnet build LapCont.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows build failed' }
    & $taskDotnet test windows/Agent.Tests/Agent.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'C# tests failed' }
    if ($Android) {
        if ($PrepareNative) { & (Join-Path $PSScriptRoot 'prepare-native.ps1') }
        Push-Location android
        try { & .\gradlew.bat :transport:test :app:assembleDebug :mobile:assembleDebug :mobile:assembleRelease :mobile:lintDebug; if ($LASTEXITCODE -ne 0) { throw 'Android build/tests failed' } }
        finally { Pop-Location }
    }
} finally { Pop-Location }
