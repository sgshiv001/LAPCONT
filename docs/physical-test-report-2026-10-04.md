# Physical product testing — 4 October 2026

Historical checkpoint. The [6 October follow-up](physical-test-report-2026-10-06.md) records
the later reproduced Android crash and verified fix, current physical BLE/UI checks, DHCP
endpoint repair, installed Windows talk-back burst/Stop-race and owner-heard voice checks,
and actual service restart/crash recovery.
Statements below describe the evidence available at the earlier checkpoint.

This follow-up uses the installed Windows product and the owner's POCO phone. It supplements
[the emulator/product report](product-test-report-2026-10-03.md); it does not turn unexecuted
release gates into passes. Results below are updated as each physical check completes.

## Environment and installation

- Windows 11 x64, ordinary-user WPF companion, actual `LapCont` SCM service running as
  LocalSystem from `C:\Program Files\LapCont\service\LapCont.Service.exe --product-service`.
- Service startup is Automatic, with restart recovery at 5/30/60 seconds and a 24-hour reset.
  TCP 4433 belongs to the installed service. The companion verifies the SCM process/account
  before connecting through its separate authenticated control/media pipes.
- POCO 23122PCD1I, Android API36, physical ARM64, 4 KiB system pages. Product and temporary
  instrumentation APKs installed successfully after the owner approved USB installation.
- The missing x64 ASP.NET Core 8.0.31 prerequisite was installed from Microsoft's official
  installer after SHA-512 and Microsoft Authenticode verification. Installation returned 0;
  no restart was requested. Desktop/Core 8.0.31 were already present.
- ProgramFiles grants SYSTEM/Administrators full control and ordinary users read/execute.
  An ordinary-user attempt to open the protected ProgramData identity was denied; no contents
  were exposed. Current-user companion startup is registered. Other-user deployment remains
  untested.

The first full install exposed a Windows PowerShell 5.1 native argument quoting failure in
`sc.exe create`. The installer now uses a raw `ProcessStartInfo` argument string preserving
the quoted executable path. The actual elevated installation passed with that fix. Explicit
`-ResumeIncompleteInstall` accepts only an incomplete tree whose existing service/companion
files match the same reviewed package by SHA-256; it refuses a redirected root or unexpected
files. This was exercised against the interrupted installation.

## Pairing and network

The first manual pairing attempt reported “Carrier failed” before the PC received an approval
request. Its displayed QR contained an old PC address; the active campus network was also Public,
where the default Private-only LapCont firewall rule does not allow inbound connections.

A fresh QR and an owner-approved temporary Public rule permitted only TCP 4433, the installed
service executable, and this phone's exact campus IPv4 address. The Windows network remains
Public. The owner scanned and approved the phone and confirmed successful pairing. Android
displayed **Route: LAN**, the actual unlocked Windows state and all five locally selected grants.
Two established connections were observed from the real phone to the installed service. No
adb reverse route was present. This is physical campus LAN evidence, not USB-loopback or WAN
evidence. DHCP changes can invalidate the narrow test rule.

Proximity auto-lock remains paused, phone participation disabled, and locked-session media
permission off. Existing user pairing is preserved; destructive enrollment revocation is not
run against this retained phone.

The Android pairing UI now distinguishes connecting from waiting for PC approval, resets its
progress message after failure/cancellation, and gives actionable network/secure-connection
errors instead of showing the carrier's internal error text. Pin verification and normal
Windows sign-in requirements are retained.

## Executed phone checks and pending checks

| Check | Result |
|---|---|
| Manual fresh QR, local PC approval, real campus LAN | Passed |
| Non-exportable ARM64 Keystore EC signing, protected-secret lifecycle | Passed |
| Strict QR unknown-field and identity-mismatch rejection | Passed |
| Dashboard/pairing labels and cancellation | Passed |
| Normal Android microphone permission prompt | Owner grant verified on retry; permission UI check passed |
| Saved-enrollment scoped commands and normal sign-in semantics | Passed on campus LAN and cable route |
| 480p/720p native decode, PC microphone, physical talk-back and expiry | Passed on the physical ARM64 phone through the explicit cable QA route |
| Actual product phone Lock, matching SCM completion, normal sign-in | Lock completion/stop-on-lock/locked capture denial passed; the four-minute sign-in wait expired, so the combined test did not pass |
| Physical Bluetooth advertising/calibration/walk-away | Eight real calibration buckets passed after the scanner fix; longer rotation/walk-away checks remain incomplete |
| Repeated Refresh / monitoring connection ownership | Owner confirmed stable repeated Refresh on actual campus LAN after the fix; two unchanged LAN sockets observed with cable routing removed |
| Service restart, full uninstall/reinstall, logoff/suspend, multi-user/RDP | Pending |
| Public CA relay/mobile-data WAN and optional Firebase delivery | No operator deployment/account supplied |

POCO denied the instrumentation helper's automatic permission grant and adb input injection.
Those restrictions were respected. The harness no longer requires microphone access for
Keystore/QR/status tests. The real media/talk test requires the app's ordinary microphone grant.
An initial four-test matrix passed three checks and stopped the media test before capture
because that grant was absent. A separate UI test opened Android's own microphone dialog;
its 90-second operator wait expired with no grant. That is not a successful media test or
evidence of an audio/decoder failure. The permission dialog remained visible afterward.

On retry Android reported the normal microphone grant present. The permission UI check and
saved-enrollment scope check passed. The first campus media run decoded 64 images, three
keyframes and 1,264 audio packets, and sent 12 phone talk packets, with zero receiver failures;
its final lease/status phase lost the connection. A visible monitoring service was also active.
The PC then switched from campus Wi-Fi to USB tethering, so an isolated retry could not establish
the old LAN route. These interrupted runs are retained as partial evidence, not complete passes.

An explicit in-memory `qaUsbReverse` route reuses the real phone's retained Keystore identity
and pinned PC certificate without changing its saved enrollment. The physical cable route then
passed the full finite media/Stop/expiry test at both presets. One 720p startup attempt timed
out after USB re-enumeration cleared adb reverse; restoring the route passed the same test.

| Physical cable test | 480p | 720p |
|---|---:|---:|
| Requested width × height | 640 × 480 | 1280 × 720 |
| Requested FPS / bitrate | 30 / 1 Mbps | 30 / 2 Mbps |
| Actual encoder path | MF hardware | MF hardware |
| Rendered ImageReader images | 61 | 64 |
| Keyframes, including explicit recovery | 3 | 3 |
| Decoded PC audio packets | 1,462 | 1,624 |
| Sent phone talk packets | 11 | 10 |
| Protected status median / p95, 20 samples | 11.749 / 16.269 ms | 11.015 / 15.398 ms |
| Test duration | 44.088 s (includes scope check) | 44.131 s |

Both runs verified repeat Stop, explicit keyframe recovery, the five-second talk lease and
30-second capture lease. These are physical ARM64/native media results with an installed
LocalSystem service. The timings measure cable command round trips, not Wi-Fi latency,
capture-to-render delay, measured sustained FPS/bitrate, or audible speaker confirmation.
Metadata is in `testing/physical-measurements-480p-usb.json`,
`testing/physical-measurements-720p-usb.json` and `testing/physical-scope-usb.json`.

The installed-service Lock test reached the checkpoint for matching accepted/completed responses,
the actual SCM SessionLock event, capture stopping on lock, sign-in-required Request unlock and
locked capture rejection. It then timed out waiting four minutes for the normal SessionUnlock
event. A later Windows user session (4 instead of 3) was unlocked; the companion was relaunched
and saved-enrollment scoped commands passed. This does not prove the original session's paired
lock/unlock sequence. The partial evidence is `testing/physical-lock-partial.json`.

## Connection stability and reported app closing

The owner reported repeated disconnects while the PC was already listed. Refresh and foreground
monitoring startup previously replaced the current client, closing its authenticated connection;
callbacks from the closed client could overwrite the new client's status. The repository now
reuses its existing client for status refresh and monitoring, ignores callbacks from replaced
clients, and wakes a pending reconnect when the owner retries. Changing connection settings
still explicitly replaces the old client and stops its active media. The phone enrollment,
Keystore identity, certificate pin and Windows sign-in requirements remain intact.

The update was installed without clearing phone data. The owner confirmed that the listed PC
stays Connected when Refresh is repeated. Both control and media TCP sockets were observed from
the phone's actual campus address to the installed service, with unchanged remote ports, and
continued after the temporary adb reverse route was removed. This is real LAN evidence.

The owner's repeated app-closing report was also checked against Android ApplicationExitInfo.
The recent entries record deliberate instrumentation start/finish force-stops, and one
`SwipeUpClean` termination during the longer BLE test. The retained history contains no recorded
Java/native crash or ANR; this does not guarantee all future exits are harmless. Automated phone
runs were stopped while the owner used the app, and the normal app was left open. The isolated
test clients could compete with a manually opened connection, so those interrupted BLE runs
are not continuous-connectivity passes. A regression harness now uses the actual application's
repository and checks repeated Refresh/monitor startup while a live lease is active; it compiles
but was not rerun after pausing disruptive phone automation. Its first run failed in the QA
dependency-access setup; that setup was corrected to use the Activity's existing ViewModel.

## Physical Bluetooth checkpoint

The first desk check incorrectly expected a healthy scanner while Pause was on. Pause deliberately
stops observation. After correcting that setup, the installed companion reported
`ObjectDisposedException`: a background worker read the PC ID from a disposed configuration
document. It now copies the ID before starting the worker. The full Windows build passed with
zero warnings/errors and all 35 tests passed. The installed companion was updated through normal
administrator approval, with all 33 package files verified and existing identities preserved.

With phone Bluetooth on and the phone beside the PC, the real Android advertiser and Windows
observer collected eight calibration buckets. The seven-sample median was −56 dBm and the
calibrated away threshold was −68 dBm. The checkpoint's eight RSSI values were
−58, −56, −56, −58, −52, −62, −56, −52 dBm. The later rotation check was interrupted and is
not a pass. Auto-lock Pause was restored to On, phone participation to Off and locked media
remained Off. This is a desk calibration result, not a walk-away, return or OEM background result.

No capture-to-render latency, sustained CPU/power, thermals, audible physical talk-back,
Bluetooth walk-away, public WAN, or production signing result is inferred from command RTT,
successful compilation, emulator results or a permission prompt.

The updated default and optional-push debug/optimized release builds passed. Android lint
reports zero errors and 18 warnings; all six transport unit tests passed. An initial lint run
failed inside its Kotlin analysis engine; a full rerun of the lint tasks passed without disabling
checks or changing dependency pins. The current optimized APK
was aligned for 16 KiB, signed with the QA debug key and verified with APK Signature Scheme v3.
All ten ARM64/x86_64 native libraries have load-segment alignment of at least 16 KiB; 13 notice
files are present and no private key/QR/fixture/UI-capture entries are included. These package
checks are in `testing/physical-packaging.json`. The earlier optimized emulator smoke result
is explicitly historical and was not rerun on this current physical build.

The 480p/720p physical media measurements above were taken before the later connection-ownership
update. The current installed debug APK and downloadable debug APK have identical SHA-256 hashes;
the owner-confirmed LAN Refresh check applies to that update. No new media/endurance or optimized
physical smoke pass is inferred from the rebuild.

The fresh-QR QA helper now supports explicitly selected physical devices and records LAN versus
USB-reverse routes. It transfers QR contents in memory to app-private cache, with no host/shared
phone credential files. New saved-enrollment scope checks and an opt-in phone-Lock/SCM/sign-in
test compile successfully. Scope checks passed; the Lock/sign-in test remains a partial result.

## Evidence, retained state and limitations

Non-secret evidence is kept under ignored `artifacts/physical-*` files, including service install,
runtime verification, installed ACL/firewall checks, and instrumentation transcripts. No video
or audio samples are recorded by these tests; live decoding uses memory and counters.

The service, companion and paired phone are retained for continued testing. The temporary
phone-only Public firewall rule is still active while physical testing continues. The permanent
installer rule remains Private/LocalSubnet.

Automatic approval review rejected deletion of `artifacts/physical-phone-preinstall-ui.xml`
with “blocked by policy.” It is an unintended pre-install UI capture and may contain unrelated
phone UI metadata; it is not included in release packages or published test evidence. It remains
for owner-controlled manual cleanup. No alternate deletion or overwrite was attempted to bypass
the rejection. An exact-file, owner-run helper is provided at
`artifacts/cleanup-accidental-phone-ui.ps1` and was not executed. The five older protected QA stores listed in `testing/cleanup.json` also remain
for the previously supplied manual cleanup script.

This is a test release. Complete the remaining [physical acceptance checklist](physical-device-checklist.md),
production signing, deployment/operator configuration and runtime migration before distribution
or reliance on automatic locking.
