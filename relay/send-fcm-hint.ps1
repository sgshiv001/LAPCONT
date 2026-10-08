# LapCont — Optional push — Trusted operator HTTP v1 sender; no credentials in APK
# License: MIT
param([Parameter(Mandatory)][string]$ProjectId,[Parameter(Mandatory)][string]$RegistrationToken,[Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$PcId,[Parameter(Mandatory)][string]$AccessToken)
$ErrorActionPreference='Stop'
if($ProjectId -notmatch '^[a-z][a-z0-9-]{4,60}$') { throw 'Invalid Firebase project ID' }
# Obtain this short-lived OAuth token on the trusted operator host with Google Auth/ADC.
# The data-only hint exposes no user, lock state, command, media, or Windows credentials.
$payload=@{message=@{token=$RegistrationToken;data=@{pc_id=$PcId};android=@{priority='HIGH';ttl='30s'}}} | ConvertTo-Json -Depth 5 -Compress
try { $null=Invoke-RestMethod -Method Post -Uri "https://fcm.googleapis.com/v1/projects/$ProjectId/messages:send" -Headers @{Authorization="Bearer $AccessToken"} -ContentType 'application/json' -Body $payload; 'Hint submitted. Delivery and PC verification are independent.' }
finally { $payload=$null; $AccessToken=$null; $RegistrationToken=$null }
