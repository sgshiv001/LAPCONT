# LapCont — QA — Finite, administrator-only SCM/session event test
# License: MIT
param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$ResultsDirectory,
    [Parameter(Mandatory=$true)][int]$SessionId,
    [ValidateRange(30,300)][int]$WaitSeconds = 180
)
$ErrorActionPreference = 'Stop'
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$taskIdentity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This finite SCM test needs an administrator shell. It creates and removes only LapCont.Phase0.'
}
$taskPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$taskResults = (Resolve-Path -LiteralPath $ResultsDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $taskPublish 'LapCont.Service.exe'))) { throw 'Publish the self-contained win-x64 service first' }
if (Get-Service -Name 'LapCont.Phase0' -ErrorAction SilentlyContinue) { throw 'A LapCont.Phase0 service already exists; it will not be replaced' }
$taskProgramData = [IO.Path]::GetFullPath([Environment]::GetFolderPath('CommonApplicationData'))
$taskDeployment = Join-Path $taskProgramData ('LapContPhase0Qa-'+[Guid]::NewGuid().ToString('N'))
$taskCreated = $false
$taskReport = [ordered]@{ status='preparing'; target_session=$SessionId; service_created=$false; service_started=$false; lock_observed=$false; unlock_observed=$false; service_removed=$false; deployment_removed=$false }
function Save-TestStatus {
    $taskReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'scm-status.json') -Encoding UTF8
}
try {
    New-Item -ItemType Directory -Path $taskDeployment | Out-Null
    $taskAcl = [Security.AccessControl.DirectorySecurity]::new()
    $taskAcl.SetAccessRuleProtection($true,$false)
    foreach ($taskSidType in @([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,[Security.Principal.WellKnownSidType]::LocalSystemSid)) {
        $taskSid = [Security.Principal.SecurityIdentifier]::new($taskSidType,$null)
        $taskRule = [Security.AccessControl.FileSystemAccessRule]::new($taskSid,[Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $taskAcl.AddAccessRule($taskRule)
    }
    Set-Acl -LiteralPath $taskDeployment -AclObject $taskAcl
    Copy-Item -Path (Join-Path $taskPublish '*') -Destination $taskDeployment -Recurse
    $taskJournal = Join-Path $taskDeployment 'journal'
    $taskExe = Join-Path $taskDeployment 'LapCont.Service.exe'
    $taskBinPath = '"'+$taskExe+'" --service --journal-directory "'+$taskJournal+'"'
    & sc.exe create LapCont.Phase0 binPath= $taskBinPath start= demand obj= LocalSystem | Out-File (Join-Path $taskResults 'scm-create.txt')
    if ($LASTEXITCODE -ne 0) { throw 'SCM service creation failed' }
    $taskCreated=$true; $taskReport.service_created=$true
    Start-Service -Name LapCont.Phase0
    (Get-Service -Name LapCont.Phase0).WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(15))
    $taskReport.service_started=$true; $taskReport.status='waiting_for_local_lock_and_sign_in'; Save-TestStatus
    $taskDeadline=[DateTime]::UtcNow.AddSeconds($WaitSeconds)
    while ([DateTime]::UtcNow -lt $taskDeadline) {
        $taskEvents=@(Get-ChildItem -LiteralPath $taskJournal -Filter 'sessions-*.jsonl' -ErrorAction SilentlyContinue | ForEach-Object {
            Get-Content -LiteralPath $_.FullName | ForEach-Object { $_ | ConvertFrom-Json }
        } | Where-Object { $_.windows_session_id -eq $SessionId })
        $taskLock=$taskEvents | Where-Object { $_.reason -eq 'SessionLock' } | Select-Object -First 1
        $taskUnlock=$taskEvents | Where-Object { $_.reason -eq 'SessionUnlock' -and $taskLock -and ([DateTimeOffset]$_.observed_at_utc -gt [DateTimeOffset]$taskLock.observed_at_utc) } | Select-Object -First 1
        $taskReport.lock_observed=($null -ne $taskLock); $taskReport.unlock_observed=($null -ne $taskUnlock)
        if ($taskLock -and $taskUnlock) { $taskReport.status='scm_lock_unlock_verified'; break }
        Start-Sleep -Seconds 1
    }
    if ($taskReport.status -eq 'waiting_for_local_lock_and_sign_in') { $taskReport.status='timed_out_waiting_for_windows_events' }
} catch {
    $taskReport.status='failed'; $taskReport.error_type=$_.Exception.GetType().Name; $taskReport.error=$_.Exception.Message
} finally {
    if ($taskCreated) {
        try {
            $taskService=Get-Service -Name LapCont.Phase0 -ErrorAction SilentlyContinue
            if ($taskService -and $taskService.Status -ne 'Stopped') {
                Stop-Service -Name LapCont.Phase0
                $taskService.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(15))
            }
            & sc.exe delete LapCont.Phase0 | Out-File (Join-Path $taskResults 'scm-delete.txt')
            if ($LASTEXITCODE -ne 0) { throw 'SCM service removal failed' }
            $taskReport.service_removed=$true
        } catch { $taskReport.cleanup_error=$_.Exception.Message }
    }
    try {
        if ($taskJournal -and (Test-Path -LiteralPath $taskJournal)) {
            Get-ChildItem -LiteralPath $taskJournal -Filter 'sessions-*.jsonl' | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskResults ('scm-'+$_.Name))
            }
        }
        # Delete only this new, GUID-named deployment, resolved under the known ProgramData root.
        if ((Test-Path -LiteralPath $taskDeployment) -and (-not $taskCreated -or $taskReport.service_removed)) {
            $taskResolved=(Resolve-Path -LiteralPath $taskDeployment).Path
            if ($taskResolved -ne $taskDeployment -or [IO.Path]::GetDirectoryName($taskResolved) -ne $taskProgramData -or
                [IO.Path]::GetFileName($taskResolved) -notmatch '^LapContPhase0Qa-[0-9a-f]{32}$' -or
                ((Get-Item -LiteralPath $taskResolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Deployment cleanup path verification failed' }
            Remove-Item -LiteralPath $taskResolved -Recurse -Force
            $taskReport.deployment_removed=$true
        }
    } catch { $taskReport.deployment_cleanup_error=$_.Exception.Message }
    Save-TestStatus
}
if ($taskReport.status -ne 'scm_lock_unlock_verified' -or -not $taskReport.service_removed) { exit 1 }
