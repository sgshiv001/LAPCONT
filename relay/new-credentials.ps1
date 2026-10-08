# LapCont — Relay — Generate independent operator-owned route credentials; no fixed production token
# License: MIT
param([Parameter(Mandatory)][string]$PcId, [Parameter(Mandatory)][string]$PhoneId, [Parameter(Mandatory)][string]$PrivateOutputDirectory)
$ErrorActionPreference = 'Stop'
if ($PcId -notmatch '^[a-fA-F0-9]{32}$' -or $PhoneId -notmatch '^[a-fA-F0-9]{32}$') { throw 'Use enrolled PC/phone IDs' }
$taskFolder = [IO.Path]::GetFullPath($PrivateOutputDirectory)
if (Test-Path -LiteralPath $taskFolder) { throw 'Use a new private directory; do not overwrite existing operator credentials' }
New-Item -ItemType Directory -Path $taskFolder | Out-Null
if ($IsWindows) {
    $taskAcl = [Security.AccessControl.DirectorySecurity]::new(); $taskAcl.SetAccessRuleProtection($true,$false)
    $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($taskSid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $taskFolder -AclObject $taskAcl
}
$taskRoles = @('agent','phone'); $taskHashes = @()
foreach ($taskRole in $taskRoles) {
    $taskToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [IO.File]::WriteAllText((Join-Path $taskFolder "$taskRole.token"), $taskToken)
    $taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskToken)))
    $taskHashes += @{sha256=$taskHash;route=$PcId;phone=$PhoneId;role=$taskRole}
}
$taskHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskFolder 'routes.json')
Write-Output 'Created private agent/phone tokens and the hash-only routes.json. Never publish token files or put tokens in URLs.'
