# LapCont — QA — Temporary fixture provisioning into the probe app's sandbox
# License: MIT
param([Parameter(Mandatory=$true)][string]$FixtureDirectory, [string]$Serial = 'emulator-5580', [string]$Adb = 'adb')
$ErrorActionPreference = 'Stop'
$taskFixture = (Resolve-Path -LiteralPath $FixtureDirectory).Path
$taskPackage = 'com.lapcont.app.phase0'
& $Adb -s $Serial reverse tcp:7443 tcp:7443
if ($LASTEXITCODE -ne 0) { throw 'adb reverse failed' }
& $Adb -s $Serial push (Join-Path $taskFixture 'fixture.json') /data/local/tmp/lapcont-phase0-fixture.json
if ($LASTEXITCODE -ne 0) { throw 'Fixture push failed' }
& $Adb -s $Serial push (Join-Path $taskFixture 'phone.p12') /data/local/tmp/lapcont-phase0-phone.p12
if ($LASTEXITCODE -ne 0) { throw 'Identity push failed' }
& $Adb -s $Serial shell run-as $taskPackage mkdir -p cache
if ($LASTEXITCODE -ne 0) { throw 'App cache creation failed; install the debug probe APK first' }
& $Adb -s $Serial shell run-as $taskPackage cp /data/local/tmp/lapcont-phase0-fixture.json cache/fixture.json
if ($LASTEXITCODE -ne 0) { throw 'Fixture copy failed' }
& $Adb -s $Serial shell run-as $taskPackage cp /data/local/tmp/lapcont-phase0-phone.p12 cache/phone.p12
if ($LASTEXITCODE -ne 0) { throw 'Identity copy failed' }
& $Adb -s $Serial shell rm /data/local/tmp/lapcont-phase0-fixture.json /data/local/tmp/lapcont-phase0-phone.p12
if ($LASTEXITCODE -ne 0) { throw 'Temporary device fixture cleanup failed' }
Write-Output 'Temporary test identities provisioned. Clear app data after tests to remove them.'
