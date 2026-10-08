# LapCont — Diagnostics — Metadata allowlist, excludes tokens, identities, personal event fields and media
# License: MIT
param([string]$DataDirectory=(Join-Path $env:LOCALAPPDATA 'LapCont/Development'),[Parameter(Mandatory)][string]$OutputFile)
$ErrorActionPreference='Stop'
$data=[IO.Path]::GetFullPath($DataDirectory)
$records=@()
foreach($file in (Get-ChildItem -LiteralPath $data -Filter 'diagnostics-*.jsonl' -File | Select-Object -Last 3)) {
  foreach($line in (Get-Content -LiteralPath $file.FullName -Tail 500)) {
    try { $row=$line | ConvertFrom-Json; $detail=[string]$row.detail
      if($detail -match '^(coordinator started|channel=(pair|control|media) error=|IPC (control|media) session=|capture failed session=|companion availability error=|relay revocation pending error=)') { $records+=@{at_utc=$row.at_utc;detail=$detail.Substring(0,[Math]::Min(180,$detail.Length))} }
    } catch { }
  }
}
@{app='LapCont';version='0.1.0';windows_build=[Environment]::OSVersion.Version.ToString();records=$records;contains_media=$false;contains_credentials=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ([IO.Path]::GetFullPath($OutputFile))
'Diagnostic summary exported.'
