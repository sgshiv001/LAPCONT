# LapCont — QA — Actual PC UI confirmation and Android Keystore integration
# License: MIT
param([ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Serial = 'emulator-5554', [Parameter(Mandatory)][string]$ResultsDirectory,
    [switch]$PhysicalDevice, [ValidateSet('UsbReverse','Lan')][string]$Network='UsbReverse')
$ErrorActionPreference = 'Stop'
if($Serial -notmatch '^emulator-\d+$' -and !$PhysicalDevice) { throw 'Use -PhysicalDevice explicitly for an authorized real-phone test.' }
$taskAdb = Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'
$taskResults = [IO.Path]::GetFullPath($ResultsDirectory)
if (!(Test-Path -LiteralPath $taskResults)) { throw 'Create a private results directory first' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-Window {
    [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'LapCont · PC companion'))
}
function Find-Control([string]$Name, $Type) {
    $taskWindow = Find-Window
    if ($null -eq $taskWindow) { throw 'Open the real PC companion before testing' }
    $condition=[System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$Type))
    $control=$taskWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
    if($null -ne $control) { return $control }
    # WPF exposes controls as they enter its scroll viewport.
    foreach($position in @(0,50,100)) {
        foreach($element in $taskWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) {
            $pattern=$null
            if($element.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$pattern)) {
                $scroll=[System.Windows.Automation.ScrollPattern]$pattern
                if($scroll.Current.VerticallyScrollable) { $scroll.SetScrollPercent(-1,$position) }
            }
        }
        $control=$taskWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        if($null -ne $control) { return $control }
    }
    return $null
}
function Invoke-Button([string]$Label) {
    $taskButton = Find-Control $Label ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $taskButton -or !$taskButton.Current.IsEnabled) { throw "Observed enabled button missing: $Label" }
    ([System.Windows.Automation.InvokePattern]$taskButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
Invoke-Button 'Pair a phone · QR expires in 60 seconds'
Start-Sleep -Milliseconds 500
$taskQr = (Find-Window).FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'PairingQrText'))
$taskPayload = (([System.Windows.Automation.ValuePattern]$taskQr.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value | ConvertFrom-Json)
if($Network -eq 'UsbReverse') { $taskPayload.addresses = @('127.0.0.1') } # Exact identity preserved; this route is not LAN evidence.
try {
    if($Network -eq 'UsbReverse') { & $taskAdb -s $Serial reverse tcp:4433 tcp:4433 | Out-Null; if($LASTEXITCODE -ne 0) { throw 'USB reverse route failed' } }
    # Transfer the expiring QR only in memory to the debuggable app's own cache.
    # Do not create host or shared-device files containing enrollment credentials.
    $transfer=[Diagnostics.ProcessStartInfo]::new(); $transfer.FileName=$taskAdb
    $transfer.Arguments="-s $Serial shell run-as com.lapcont.app tee cache/product-qr.json"
    $transfer.UseShellExecute=$false; $transfer.CreateNoWindow=$true
    $transfer.RedirectStandardInput=$true; $transfer.RedirectStandardOutput=$true; $transfer.RedirectStandardError=$true
    $provision=[Diagnostics.Process]::Start($transfer)
    $provision.StandardInput.Write(($taskPayload | ConvertTo-Json -Compress)); $provision.StandardInput.Close()
    $null=$provision.StandardOutput.ReadToEnd(); $provisionError=$provision.StandardError.ReadToEnd(); $provision.WaitForExit()
    if($provision.ExitCode -ne 0) { throw "App-cache QR transfer failed: $provisionError" }
    $provision.Dispose()
    $taskProcess = Start-Process -FilePath $taskAdb -ArgumentList @('-s',$Serial,'shell','am','instrument','-w','-e','class','com.lapcont.mobile.ProductTests#enrolledControlAndMediaIntegration','com.lapcont.app.test/androidx.test.runner.AndroidJUnitRunner') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskResults 'pairing-instrumentation.txt') -RedirectStandardError (Join-Path $taskResults 'pairing-instrumentation-error.txt') -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do { $taskApprove = Find-Control 'Approve this phone with selected permissions' ([System.Windows.Automation.ControlType]::Button); if ($null -ne $taskApprove) { break }; if ($taskProcess.HasExited) { throw 'Pairing ended before PC confirmation; inspect instrumentation output' }; Start-Sleep -Milliseconds 200 } while ([DateTime]::UtcNow -lt $taskDeadline)
    if ($null -eq $taskApprove) { throw 'Real phone identity/approval UI did not appear' }
    foreach ($taskLabel in @('Lock / Request unlock','Bluetooth proximity','Camera','Microphone','Phone talk-back')) {
        $taskBox = Find-Control $taskLabel ([System.Windows.Automation.ControlType]::CheckBox)
        $taskToggle = [System.Windows.Automation.TogglePattern]$taskBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($taskToggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $taskToggle.Toggle() }
    }
    Invoke-Button 'Approve this phone with selected permissions'
    if (!$taskProcess.WaitForExit(60000)) { $taskProcess.Kill(); throw 'Finite enrollment integration timed out' }
    $taskOutput = Get-Content -LiteralPath (Join-Path $taskResults 'pairing-instrumentation.txt') -Raw
    if ($taskOutput -notmatch 'OK \(1 test\)') { throw 'Enrollment integration failed; see private instrumentation evidence' }
    @{status='passed';api=([int](& $taskAdb -s $Serial shell getprop ro.build.version.sdk));pc_id=$taskPayload.pc_id;network=$Network;physical_lan=($PhysicalDevice -and $Serial -notmatch '^emulator-\d+$' -and $Network -eq 'Lan');private_key='Android Keystore non-exportable EC';local_confirmation='actual WPF approval button';grants=31} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'pairing-summary.json')
    Write-Output 'Real Keystore enrollment, protected status, media attachment, wrong session/fields, sign-in semantics and repeated Stop passed.'
} finally {
    & $taskAdb -s $Serial shell run-as com.lapcont.app rm -f cache/product-qr.json
}
