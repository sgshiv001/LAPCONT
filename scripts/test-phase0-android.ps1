# LapCont — QA — Real emulator instrumentation, UI permissions, Stop and Home
# License: MIT
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^emulator-\d+$')][string]$Serial,
    [Parameter(Mandatory=$true)][string]$FixtureDirectory,
    [Parameter(Mandatory=$true)][string]$ResultsDirectory,
    [string]$Adb = 'adb'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskResults = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$taskPackage = 'com.lapcont.app.phase0'
function Invoke-Adb([string[]]$Arguments) {
    $taskOutput = @(& $Adb -s $Serial @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "adb operation failed: $($Arguments[0])" }
    return $taskOutput
}
$taskApi = [int]((Invoke-Adb @('shell','getprop','ro.build.version.sdk')) -join '').Trim()
if ($taskApi -lt 29) { throw 'Phase 0 requires API 29 or later' }
function Save-Ui([string]$Name) {
    for ($taskAttempt = 1; $taskAttempt -le 3; $taskAttempt++) {
        $taskRaw = (Invoke-Adb @('exec-out','uiautomator','dump','/dev/tty')) -join "`n"
        Set-Content -LiteralPath (Join-Path $taskResults "api$taskApi-$Name.xml") -Value $taskRaw
        $taskXml = [regex]::Match($taskRaw, '(?s)<hierarchy.*?</hierarchy>').Value
        if ($taskXml) { return [xml]$taskXml }
        $taskReport.ui_bridge_retries += 1
        Set-Content -LiteralPath (Join-Path $taskResults "api$taskApi-$Name-attempt$taskAttempt.xml") -Value $taskRaw
        if ($taskAttempt -lt 3) { Start-Sleep -Milliseconds 500 }
    }
    throw 'No actual UI hierarchy after three bounded attempts'
}
function Tap-Ui($Ui, [string]$Label, [switch]$ResourceId) {
    $taskNode = @($Ui.SelectNodes('//node') | Where-Object {
        if ($ResourceId) { $_.'resource-id' -eq $Label } else { $_.text -eq $Label }
    })[0]
    if (!$taskNode -or $taskNode.enabled -ne 'true') { throw "Enabled observed UI node missing: $Label" }
    $taskBounds = @([regex]::Matches($taskNode.bounds, '\d+') | ForEach-Object { [int]$_.Value })
    if ($taskBounds.Count -ne 4) { throw 'UI node has invalid bounds' }
    Invoke-Adb @('shell','input','tap',([int](($taskBounds[0]+$taskBounds[2])/2)).ToString(),([int](($taskBounds[1]+$taskBounds[3])/2)).ToString()) | Out-Null
}
function Require-Text($Ui, [string]$Text) {
    if (!(@($Ui.SelectNodes('//node') | ForEach-Object { [string]$_.text }) -contains $Text)) { throw "Expected UI state missing: $Text" }
}
function Open-Probe {
    Invoke-Adb @('shell','am','start','-n',"$taskPackage/com.lapcont.app.MainActivity") | Out-Null
}
function Check-Home($Ui, [string]$Action, [string]$Name) {
    $taskBefore = (Invoke-Adb @('shell','pidof',$taskPackage)) -join ''
    Tap-Ui $Ui $Action
    Invoke-Adb @('shell','input','keyevent','3') | Out-Null
    Start-Sleep -Milliseconds 2500
    $taskActivity = (Invoke-Adb @('shell','dumpsys','activity','activities')) | Select-String 'topResumedActivity|mResumedActivity'
    ($taskActivity -join "`n") | Set-Content (Join-Path $taskResults "api$taskApi-$Name-home-activity.txt")
    if (($taskActivity -join '') -notmatch 'launcher') { throw 'Home foreground was not independently observed' }
    Open-Probe
    $taskUi = Save-Ui "$Name-background-stopped"
    $taskTexts = @($taskUi.SelectNodes('//node') | ForEach-Object { [string]$_.text })
    $taskAfter = (Invoke-Adb @('shell','pidof',$taskPackage)) -join ''
    if ($taskTexts -contains 'Probe stopped') { return 'cancelled' }
    if (($taskTexts -contains 'Choose a probe. Physical-device compatibility is unverified.') -and $taskBefore -ne $taskAfter) { return 'new_process_idle' }
    throw 'Home left an unexpected probe state; a completed capture is not accepted as cancellation'
}
$taskReport = [ordered]@{ api=$taskApi; instrumentation_passed=0; microphone_permission_denial=$false; microphone_permission_recovery=$false; microphone_home=$null; ble=$null; ble_home=$null; repeated_stop=$false; surface_frames=0; ui_bridge_retries=0; app_data_cleared=$false; reverse_removed=$false }
$taskBtInitial = $null
try {
    # This app contains only disposable Phase 0 fixtures. Never use this on a product app or physical phone.
    Invoke-Adb @('shell','pm','clear',$taskPackage) | Out-Null
    Invoke-Adb @('install','-r',(Join-Path $taskRoot 'android/app/build/outputs/apk/debug/app-debug.apk')) | Out-Null
    Invoke-Adb @('install','-r',(Join-Path $taskRoot 'android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk')) | Out-Null
    & (Join-Path $PSScriptRoot 'provision-emulator-fixture.ps1') -FixtureDirectory $FixtureDirectory -Serial $Serial -Adb $Adb
    Invoke-Adb @('logcat','-b','all','-c') | Out-Null
    $taskInstrumentation = Invoke-Adb @('shell','am','instrument','-w','-r',"$taskPackage.test/androidx.test.runner.AndroidJUnitRunner")
    $taskInstrumentation | Set-Content (Join-Path $taskResults "android-api$taskApi-final-instrumentation.txt")
    if (($taskInstrumentation -join '') -notmatch 'OK \(8 tests\)') { throw 'Require all eight actual instrumentation tests, not just an adb exit code' }
    $taskReport.instrumentation_passed = 8
    Open-Probe
    $taskUi = Save-Ui 'final-before-mic'
    Tap-Ui $taskUi 'Probe microphone / Opus (1.5 seconds)'
    $taskPermission = Save-Ui 'final-mic-permission'
    Tap-Ui $taskPermission 'com.android.permissioncontroller:id/permission_deny_button' -ResourceId
    $taskDenied = Save-Ui 'final-mic-denied'
    Require-Text $taskDenied 'Microphone permission denied. Other probes remain available.'
    $taskReport.microphone_permission_denial = $true
    Tap-Ui $taskDenied 'Probe microphone / Opus (1.5 seconds)'
    $taskPermission = Save-Ui 'final-mic-retry-permission'
    $taskAllow = if ($taskApi -ge 30) { 'permission_allow_one_time_button' } else { 'permission_allow_button' }
    Tap-Ui $taskPermission "com.android.permissioncontroller:id/$taskAllow" -ResourceId
    Start-Sleep -Milliseconds 1600
    $taskMic = Save-Ui 'final-mic-completed'
    $taskMicResult = @($taskMic.SelectNodes('//node') | Where-Object { $_.text -like 'AudioRecord*' })[0].text
    if (!$taskMicResult) { throw 'AudioRecord/Opus packet production was not observed' }
    $taskReport.microphone_permission_recovery = $true
    $taskReport.microphone_result = $taskMicResult
    $taskReport.microphone_home = Check-Home $taskMic 'Probe microphone / Opus (1.5 seconds)' 'final-mic'
    $taskUi = Save-Ui 'final-before-ble'
    Tap-Ui $taskUi 'Advertise BLE test beacon (8 seconds)'
    if ($taskApi -ge 31) {
        $taskPermission = Save-Ui 'final-ble-permission'
        Tap-Ui $taskPermission 'com.android.permissioncontroller:id/permission_deny_button' -ResourceId
        $taskDenied = Save-Ui 'final-ble-denied'
        Require-Text $taskDenied 'Bluetooth permission denied. Other probes remain available.'
        Tap-Ui $taskDenied 'Advertise BLE test beacon (8 seconds)'
        $taskPermission = Save-Ui 'final-ble-retry-permission'
        Tap-Ui $taskPermission 'com.android.permissioncontroller:id/permission_allow_button' -ResourceId
        $taskRunning = Save-Ui 'final-ble-running'
        Require-Text $taskRunning 'Probe running…'
        Tap-Ui $taskRunning 'Stop all probes'
        $taskStopped = Save-Ui 'final-ble-stopped'
        Require-Text $taskStopped 'Probe stopped'
        $taskReport.ble = 'permission_denial_recovery_and_stop_verified_on_emulator'
        $taskReport.ble_home = Check-Home $taskStopped 'Advertise BLE test beacon (8 seconds)' 'final-ble'
        $taskBtInitial = ((Invoke-Adb @('shell','settings','get','global','bluetooth_on')) -join '').Trim()
        Invoke-Adb @('shell','svc','bluetooth','disable') | Out-Null
        Start-Sleep -Milliseconds 800
        $taskUi = Save-Ui 'final-bt-off-initial'
        Tap-Ui $taskUi 'Advertise BLE test beacon (8 seconds)'
        $taskUi = Save-Ui 'final-bt-off-result'
        Require-Text $taskUi 'BLE_UNAVAILABLE: Bluetooth is off'
        $taskReport.bluetooth_off = 'explicit_unavailable'
    } else {
        $taskUi = Save-Ui 'final-ble-unavailable'
        $taskBleResult = @($taskUi.SelectNodes('//node') | Where-Object { $_.text -like 'BLE_UNAVAILABLE:*' })[0].text
        if (!$taskBleResult) { throw 'Expected this API 29 AOSP emulator to report its unavailable BLE adapter' }
        $taskReport.ble = $taskBleResult
    }
    $taskUi = Save-Ui 'final-before-repeat-stop'
    Tap-Ui $taskUi 'Stop all probes'
    Tap-Ui $taskUi 'Stop all probes'
    $taskUi = Save-Ui 'final-repeat-stop'
    Require-Text $taskUi 'Probe stopped'
    $taskReport.repeated_stop = $true
    Tap-Ui $taskUi 'Probe H.264 encode → decode'
    Start-Sleep -Milliseconds 1000
    $taskUi = Save-Ui 'final-video-ui'
    if ((@($taskUi.SelectNodes('//node') | ForEach-Object { [string]$_.text }) -join '') -notmatch '30 encoded, 30 decoded synthetic frames') { throw 'Surface did not decode all 30 generated frames' }
    $taskReport.surface_frames = 30
    Invoke-Adb @('shell','screencap','-p','/data/local/tmp/lapcont-final-video.png') | Out-Null
    Invoke-Adb @('pull','/data/local/tmp/lapcont-final-video.png',(Join-Path $taskResults "api$taskApi-final-video-screen.png")) | Out-Null
    Invoke-Adb @('shell','rm','/data/local/tmp/lapcont-final-video.png') | Out-Null
    $taskReport.status = 'passed'
} catch {
    $taskReport.status = 'failed'; $taskReport.error = $_.Exception.Message
    throw
} finally {
    if ($taskBtInitial -eq '1') { & $Adb -s $Serial shell svc bluetooth enable | Out-Null }
    if ($taskBtInitial -eq '0') { & $Adb -s $Serial shell svc bluetooth disable | Out-Null }
    $taskLog = @(& $Adb -s $Serial logcat -d -s LapContPhase0:I AndroidRuntime:E)
    ($taskLog -join "`n") | Set-Content (Join-Path $taskResults "android-api$taskApi-final-log.txt")
    $taskCrash = @(& $Adb -s $Serial logcat -d -b crash)
    ($taskCrash -join "`n") | Set-Content (Join-Path $taskResults "android-api$taskApi-final-crash.txt")
    $taskReport.app_fatal_signatures = @($taskCrash | Select-String 'Process: com.lapcont.app.phase0|>>> com.lapcont.app.phase0 <<<').Count
    $taskReport.emulator_uwb_crash_records = @($taskCrash | Select-String 'Cmdline: /vendor/bin/hw/android.hardware.uwb-service').Count
    & $Adb -s $Serial shell am force-stop $taskPackage | Out-Null
    & $Adb -s $Serial shell pm clear $taskPackage | Out-Null
    $taskReport.app_data_cleared = $LASTEXITCODE -eq 0
    & $Adb -s $Serial reverse --remove tcp:7443 | Out-Null
    $taskReport.reverse_removed = $LASTEXITCODE -eq 0
    $taskReport | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskResults "api$taskApi-final-ui-summary.json")
}
$taskReport | ConvertTo-Json -Depth 4
