# LapCont — Full Phase 0 testing report

**Date:** 3 October 2026, Asia/Kolkata. Final cleanup audit: **05:08 IST**.
**Scope:** the enhanced specification's Phase 0 feasibility prototypes and architecture.
**Result:** testing is complete for the available PC and emulators. The final automated suites
and available hardware/UI checks passed. Physical-phone, Windows 10 and later-product gates
remain explicitly unverified. This is not a completed seven-phase product or a release sign-off.

The owner participated in the real Windows lock/sign-in and audible-speaker checks before the
unattended run. Those observations are included here. The unattended continuation did not log
off, suspend, reboot or lock the owner's Windows session.

## Final automated results

| Suite/check | Final result | Evidence |
|---|---|---|
| Windows locked dependency restore | Passed | `windows-locked-restore-final.txt` |
| Windows Debug and Release solution builds | Passed; **0 warnings, 0 errors** | `windows-debug-build-final.txt`, `windows-release-build-final.txt` |
| C# xUnit | **23 passed**, 0 failed, 0 skipped | `windows-tests-final.trx`, `windows-tests-final.txt` |
| Kotlin JVM JUnit | **3 passed**, 0 failures/errors/skips | `kotlin-final-results.xml` |
| Android 10 / API 29 instrumentation | **8 passed**, 0 failed | `android-api29-final-instrumentation.txt` |
| API 37 instrumentation | **8 passed**, 0 failed | `android-api37-final-instrumentation.txt` |
| Android debug app/test APK builds | Passed | `android-final-build-lint.txt` |
| Android lint | **0 errors, 6 warnings** | `android-final-lint.xml`, `android-final-lint.txt` |
| Android manual UI scenarios driven through actual UI-tree bounds | Passed on both targets, with the API 29 BLE adapter explicitly unavailable | `api29-final-ui-summary.json`, `api37-final-ui-summary.json` |
| Final APK signature | Passed, debug APK v2 signature | `apk-signature.txt` |
| APK native ZIP alignment | Passed at 16 KiB | `apk-16kb-zip-alignment.txt` |
| All six packaged native ELF files | Every inspected load segment aligned to 16,384 bytes | `apk-final-inspection.json` |

These totals represent **34 distinct automated cases**: 23 C#, 3 Kotlin and 8 Android cases.
Running the Android cases on two OS images produces **42 passing case executions** in the
final matrix. Builds, manual scenarios and earlier retries are separate; they are not added
to inflate that total. Instrumentation success was established from `OK (8 tests)`, rather
than adb's exit code alone.

The six lint warnings are dependency freshness notices: Compose BOM appears twice, plus
Activity, Lifecycle, coroutines and JNA. The tested dependency pins were retained. The actual
backup-rule and missing-icon warnings were fixed; warnings were not suppressed.

## Tested environment

| Component | Observed configuration |
|---|---|
| PC | Windows 11 Home Single Language, build 10.0.26300, x64 |
| Session | Opted-in ordinary-user interactive session 1; console session 1 |
| Windows toolchain | .NET SDK 8.0.425, runtime 8.0.31, C# 12 |
| Android toolchain | Android Studio JBR 25.0.3; Gradle 9.6.0; AGP 9.4.1; compile/target API 37; build tools 36.0.0 |
| Minimum Android runtime | Android 10 / API 29 AOSP x86_64, 4 KiB pages, `emulator-5582` |
| Newer Android runtime | API 37 x86_64, 16 KiB pages, existing Pixel_10 AVD, `emulator-5580` |
| Real PC camera | ASUS FHD webcam; negotiated 1280×720 NV12; software H.264 transform |
| Real PC microphone | WASAPI native 48 kHz stereo IEEE float, converted to 48 kHz mono |
| Real PC speaker | Explicit `Speakers (Realtek(R) Audio)` WASAPI endpoint |
| Bluetooth | Operational Windows LE/central observer; API 37 virtual advertiser; API 29 image has no adapter |
| Channel | Loopback-only WSS on port 7443 plus independent inner mutual-TLS control/media tunnels; adb reverse |
| Physical Android device | None connected |

The API 29 image and official command-line tools were installed for this test. SDK package
installation was verified from the actual image and a booted emulator, not a misleading
successful installer exit code. A separate QA AVD was created without replacing Pixel_10.
Temporary graphics diagnostics were tried on that QA AVD; final testing passed after restoring
its original graphics configuration. Product behavior does not require adb root or renderer changes.

See [compatibility and dependency decisions](compatibility.md) for full pins and lifecycle limits.

## Phase 0 platform results

### 1. Desktop lock and verified session events

**Passed on this PC.** The owner clicked the local lock action and signed back in normally.
The WPF companion recorded real WTS lock/unlock notifications for session 1. The first lock
was observed at 03:51:52 IST and the corresponding unlock at 03:52:55 IST. The second sequence
also matched the service observations. Duplicate WTS messages are preserved as raw evidence;
production event deduplication belongs to Phase 2.

A temporary LocalSystem service received actual SCM `SessionLock` and `SessionUnlock` at
03:57:17 and 03:57:20 IST, with verified WTS user lookup available and zero dropped events.
The finite helper installed into a fresh administrator/SYSTEM-controlled ProgramData folder,
then removed its own service and deployment. `scm-status.json` records both events and both
cleanup flags as true. No remote unlock was implemented or simulated.

### 2. Session-bound IPC

**Passed for the current session.** The named-pipe probe completed an actual round-trip and
checked the OS-reported client session and impersonated SID. Wrong expected session and wrong
expected SID were rejected. The ACL permits SYSTEM and the intended user and denies network SID.

This does not establish hostile second-user/RDP/network-client behavior or the future mutual
coordinator/companion handshake. Those require additional accounts/systems and Phase 1 integration.

### 3. Windows camera capture and H.264

**Passed after correcting real failures.** The final finite run captured and encoded **84**
access units from the actual webcam at **1280×720**, discarding **582,916 encoded bytes** in
memory. Capture duration was about **3.03 seconds after setup**. Hardware encoders were
enumerated, but this exercised the software fallback.

The local Stop handler cancelled capture and the camera reopened successfully. In the final
observed cancellation, completion followed Stop by about **0.33 seconds**. This is a single
host observation, not a percentile benchmark or a guarantee for other drivers.

### 4. Windows audio, Opus and speaker

**Microphone passed.** Actual WASAPI input converted 48 kHz stereo float to 48 kHz mono and
produced **73 Opus packets** in the final reopened run. Stop completed in about **0.10 seconds**
after the request and the endpoint reopened. No captured samples or packets were saved.

**Native Opus passed.** On Windows and both Android emulators, libopus **1.6.1** encoded 960
generated silent samples into a 3-byte packet and decoded exactly 960 samples.

**Realtek speaker passed audibly.** The owner confirmed hearing the one-second quiet generated
440 Hz tone through the explicitly selected Realtek endpoint. The default FxSound virtual
endpoint passed the playback API check but was inaudible to the owner, so that route is recorded
as an audibility limitation. The default endpoint and master volume were not changed.

### 5. Android codecs and visible Surface output

**Passed on both emulators.** Each instrumented codec check encoded and decoded exactly **30
generated 64×64 H.264 frames**. API 29 used `OMX.google.h264.encoder/decoder`; API 37 used
`c2.android.avc.encoder` and `c2.goldfish.h264.decoder`.

The separate UI flow rendered the generated gray pattern into an actual decoder Surface on
each target. Final screenshots were inspected, including readable controls and system bars.
The instrumented UI check now inspects actual composed screen pixels in the Stop-text region,
because semantics alone had passed while the older screen was blank.

The initial API 29 SurfaceView integration obscured the Compose controls. Hiding that surface
isolated the cause; changing emulator GPU modes did not resolve it. API 29 now uses Compose's
embedded external Surface, while newer versions use its external Surface. The decoder still
receives a real Android Surface. Embedded composition has additional GPU/bandwidth cost, which
has not been benchmarked on a physical phone. [Android surface guidance](https://developer.android.com/media/media3/ui/surface).

### 6. Android permission, Stop and lifecycle behavior

**Passed on both targets.** Real system microphone-denial dialogs produced the explicit denial
message. Granting permission then produced **74 packets on API 29** and **73 on API 37** in the
final run. API 37 used the actual “Only this time” option. AudioTrack accepted generated silence;
physical Android audibility remains unverified.

Starting another microphone probe and immediately pressing Home resulted in `Probe stopped`
on both targets. The launcher was independently verified as foreground before returning. Stop
can be pressed repeatedly, and a subsequent synthetic video probe still completes.

Testing found a delayed already-granted permission callback could start a probe across a quick
Home transition. Already-granted permissions now start directly; pending requests are tracked
and invalidated by Stop/background. Active probes also cancel on pause/user leave, while a real
permission dialog remains usable. Final denial, recovery and quick-Home scenarios all passed.
The [Activity leave callback](https://developer.android.com/reference/android/app/Activity#onUserLeaveHint())
supplements lifecycle teardown; it does not substitute for capture authorization in later phases.

### 7. BLE availability and cancellation

**Windows observer passed; physical matching is unverified.** The real Windows observer ran
for eight seconds without an error, with **zero matching test beacons**. This proves the observer
path started; it does not prove phone reception, RSSI calibration or proximity policy.

**API 37 emulator paths passed.** Real Bluetooth permission denial/recovery, advertisement
start, Stop, repeated Stop, restart, immediate Home cancellation and Bluetooth-off handling were
exercised. Off returned `BLE_UNAVAILABLE: Bluetooth is off`. Its original Bluetooth-on state
was restored before shutdown. Advertising in an emulator is supplementary to over-the-air testing.

**API 29 availability handling passed.** This AOSP image has no Bluetooth adapter and reported
`BLE_UNAVAILABLE: no adapter`. Its advertising and BLE-background checks are unavailable, not
marked as passes. A physical ARM64 phone is needed for the cross-device test.

### 8. End-to-end channel and malformed-input boundaries

**Passed using actual TLS providers.** Kotlin JVM and both Android OS providers interoperated
with C# Schannel on independent control and media WSS connections using **TLS 1.3**. Control
echoes include parameters; media echoes include the exact binary header and generated bytes.
No business operation or live PC feed was sent.

Negative tests rejected wrong PC pins, wrong phone identity/missing phone certificates, expired
or future pinned certificates, and authenticated context bound to the wrong PC or phone ID.
Android negative probes perform a valid exchange first, so an offline server cannot satisfy
an identity-rejection test. C# also rejected mutated and repeated actual TLS ciphertext.

The C# compatibility test forced a real mutually authenticated **TLS 1.2 ECDHE/RSA/GCM** exchange
on Windows 11. This proves the local fallback path, not Windows 10 compatibility or production
cipher enforcement on every supported OS.

Framing tests cover the common 40-byte media-header vector, partial reads, bounds before
allocation, malformed UTF-8, duplicate JSON keys, excessive nesting and the baseline command
names. Real Kestrel/client WebSocket tests reject text and oversized messages, split a
131,195-byte write into bounded messages without changing bytes, treat peer close as EOF and
honor read cancellation. Application request idempotency, revocation and connection budgets
remain later-phase implementation gates.

## Corrections made and rechecked

| Finding | Correction and result |
|---|---|
| Schannel rejected ephemeral imported test keys | Used disposable OS-backed user-key imports without persistent-key flags; actual handshakes passed |
| Schannel synchronous rejection alerts failed on the byte carrier | Added a bounded synchronous-write bridge; real missing-phone authentication regression passed |
| Malformed UTF-8 / nested duplicate JSON keys | Explicit validation and targeted tests passed |
| API 37 test-runner incompatibility | Pinned compatible runner/Espresso versions; actual instrumentation passed |
| Webcam cleanup masked the original error | Clarified source-reader ownership and preserved the original failure context |
| Legacy camera processing could not negotiate NV12 | Enabled advanced source-reader processing; actual negotiated capture passed |
| Encoder output buffer was clipped below the MFT requirement | Honored the transform's requirement under a separate 8 MiB working-buffer bound; wire frame limit remains 1 MiB |
| Default virtual speaker route was inaudible | Tested explicit Realtek route and obtained owner confirmation; default-route limitation retained |
| Service publishing disturbed locked RID state | Declared win-x64 runtime target; subsequent locked restore and both builds passed |
| Added Kotlin test helpers returned a non-void result | Corrected JUnit entry points to Unit/void; all eight tests actually ran |
| API 29 video surface hid the controls | Added compatible embedded Surface path and an actual screen-pixel regression check |
| Quick Home raced permission/capture start | Direct start for granted permissions, tracked pending requests and earlier active-probe cancellation; final scenarios passed |
| Backup/device-transfer lint warning | Added explicit exclusions for both legacy and Android 12+ formats; static lint warning cleared |
| Dependency AARs advertised native ABIs without Opus | Restricted native packaging to ARM64 and x86_64; final archive inspected |
| Missing launcher icon / unreadable light system-bar icons | Added vector icon and edge-to-edge system-bar handling; screenshots inspected |
| API 37 UI bridge briefly returned a null hierarchy | Recorded the failure; added three bounded fresh-state attempts; final full UI run passed with zero retries |

Camera fixes follow the actual Microsoft APIs for [source ownership](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-source-reader-disconnect-mediasource-on-shutdown),
[advanced video processing](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-source-reader-enable-advanced-video-processing)
and [MFT output-buffer requirements](https://learn.microsoft.com/en-us/windows/win32/api/mftransform/ns-mftransform-mft_output_stream_info).
Backup configuration follows [Android backup rules](https://developer.android.com/identity/data/autobackup).

The API 37 image repeatedly faulted in its own `/vendor/bin/hw/android.hardware.uwb-service`
because its serial device was unavailable. The final UI run recorded **11 UWB crash records**
and **zero LapCont fatal signatures**. API 29 recorded zero for both. This emulator-image issue
is retained in the evidence and prevents claiming an entirely error-free Android system log.
LapCont does not use UWB. It was not counted as an app test failure or hidden by disabling services.

## Packaging and measurements

The final debug APK is **33,554,659 bytes**, approximately **32.00 MiB**, with ARM64/x86_64
libopus/JNA/graphics assets and license notices. Its SHA-256 is saved in `apk-final-inspection.json`.
It contains no test PKCS12/fixture entries. Debug signature verification, 16 KiB native ZIP
alignment and all six native ELF load-segment alignment inspections passed.

This is the debug archive size, not installed size or a release-size result. The Android release
target under 30 MB remains unmeasured. Windows/relay release package-size targets are also
unmeasured. Native ARM64 packaging/alignment inspection does not replace physical execution.

There is no completed PC-to-phone live pipeline, real LAN/WAN benchmark, median/p95 media
latency, CPU/memory/battery benchmark, jitter/sync result or sustained-load claim. Short Stop
completion observations above are reported separately from end-to-end media latency.

## Final cleanup

The final audit verified:

- Both emulator app sandboxes contain no `phone.p12` or `fixture.json`; app data was cleared.
- Microphone and Bluetooth capture/advertise/connect runtime permissions were revoked by clearing the probe app.
- Both adb reverse mappings for port 7443 were removed.
- The loopback channel host stopped; port 7443 has no listener.
- All five test fixture folders from this test chain contain zero files.
- The WPF probe closed; no LapCont Session/ChannelProbe/Service test process remains.
- `LapCont.Phase0` is absent; its protected ProgramData deployment was removed by the finite SCM helper.
- Both QA emulators were shut down. Build tools, downloaded SDK images, the QA AVD configuration,
  APKs and ignored evidence are retained for reproducibility.

No PC camera/microphone media was saved. Diagnostic images show only the app and generated codec
pattern. Windows sign-in, default audio route, master volume and privacy settings were not weakened
or changed to produce a pass. SDK installation, local source fixes and temporary test fixtures were
the persistent/reversible work performed; no release was published and no commit was created.

## Remaining gates

| Gate | Why it remains open |
|---|---|
| Physical ARM64 Android 10/current Android, real speaker/headset routing and OEM behavior | No physical phone was connected |
| Real phone-to-PC BLE matching/RSSI, walk-away/return, calibration and multiple keys | Emulator advertising cannot establish an over-the-air match; proximity policy is Phase 3 |
| Windows 10, restricted accounts, multiple users and RDP | Only this Windows 11 session was available |
| Actual logoff, sleep/resume, unplugging, privacy-disabled/occupied devices and other drivers | Not exercised unattended on the owner's active desktop |
| Async hardware H.264 encoding | Enumerated only; software fallback was the capture path tested |
| FxSound default-route audibility | API call passed but the owner did not hear it; Realtek direct route passed |
| Pairing, protected production key storage, scoped grants/revocation and production IPC | Phase 1 is not implemented |
| Remote lock/event state reconstruction, proximity, live streams and push-to-talk leases | Phases 2–5 are not implemented |
| Real Go relay, separate networks/mobile data, Docker and optional FCM | Phase 6 is not implemented |
| Installer/uninstaller, supported OS matrix, full accessibility/lifecycle/load measurements | Phase 7 is not implemented |
| Genuine phone-approved Windows authentication | Advanced module remains a design; no supported authentication integration has been implemented |

Phase 0's acceptance criterion explicitly permits unavailable-device checks to be reported as
unverified. The available paths are now evidenced and the implementation/library choices can be
reviewed before Phase 1. These open gates must be completed before advertising the corresponding
product capabilities. The [physical/system checklist](physical-device-checklist.md) identifies
the remaining operator/device scenarios.

## Evidence and reproduction

The local evidence folder is `artifacts/phase0-retry-20261003-034927/`, ignored by Git. It contains
the final TRX/JUnit/instrumentation results, manual UI summaries/trees/synthetic screenshots,
WPF capture/Stop/WTS metadata, SCM journal/status, APK/lint inspection and `cleanup-final.json`.
Earlier failures remain identifiable; final files and summaries supersede earlier partial runs.
Evidence timestamps are UTC where labelled, so late 2 October UTC corresponds to 3 October IST.

Build with the exact [README prerequisites](../README.md), then run:

```powershell
dotnet restore LapCont.sln --locked-mode
dotnet build LapCont.sln -c Release --no-restore
dotnet test windows/Agent.Tests/Agent.Tests.csproj -c Release --no-build
.\scripts\prepare-native.ps1
cd android
.\gradlew.bat :transport:test :app:assembleDebug :app:assembleDebugAndroidTest :app:lintDebug
```

For the full emulator flow, start the loopback channel host in a separate terminal with a new
private fixture folder as described in the README. Boot the chosen dedicated emulator and use
the real serial from adb. From the repository root:

```powershell
$taskAdb = "$env:ANDROID_HOME\platform-tools\adb.exe"
New-Item -ItemType Directory -Path artifacts\new-android-check
.\scripts\test-phase0-android.ps1 -Serial emulator-5580 -FixtureDirectory "$env:LOCALAPPDATA\LapCont\new-phase0-fixture" -ResultsDirectory artifacts\new-android-check -Adb $taskAdb
```

The script is explicitly limited to emulator serials and the disposable Phase 0 app. It clears
that app's data, installs the current debug/test APKs, runs all eight instrumented tests, drives
permissions/Stop/Home/Surface checks from observed UI nodes, and clears test credentials/reverse
mapping in `finally`. The API 29 branch expects the tested AOSP image's missing BLE adapter;
the API 31+ branch requires an operational virtual Bluetooth adapter. Inspect the resulting JSON
and require `status: passed`, both cleanup flags true and zero app fatal signatures. Review the
synthetic screenshot separately. Stop the host, verify fixture cleanup and shut down the emulator.

Physical WTS/SCM and audible-speaker checks require a present owner; follow the physical checklist.
Do not automate Windows sign-in or substitute an acknowledgment for an actual unlock event.
