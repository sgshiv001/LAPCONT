# LapCont physical testing — 6 October 2026

**Later UI update:** matching Windows/phone screens, navigation animations and Back fixes are
packaged in the new UI builds. Their emulator/component results are in the
[UI update report](ui-update-report-2026-10-06.md). These builds have not been installed on the
physical phone; the real-device results below retain their original APK/binary scope.

The Android companion-disconnect crash is fixed, installed and verified on the owner's POCO.
The saved campus LAN endpoint was repaired after the PC's DHCP address changed; the current
Refresh/monitoring regression passed. The Windows talk-back fixes are now **installed and
physically verified** for burst congestion, Stop races and normal press/release. Actual service
restart/crash recovery passed. The baseline implementation covers Phases 1–7; physical release acceptance remains
incomplete. This report combines checks performed overnight on 4–5 October with the 6 October
follow-up. Earlier results keep their original scope and build provenance.

The final 30-second campus-LAN voice check also passed: 1,530 phone talk packets, clean Stop,
and the owner confirmed hearing their spoken voice from the laptop speakers. No audio was recorded.

The later normal wireless proximity attempt did **not** establish an auto-lock pass. The owner
initially selected the 8/8 calibration answer, but subsequently supplied a screen showing
`not_calibrated`, 0/8 samples, no median and no recent readings, despite a healthy observer.
The finite 180.038-second WTS observation stayed unlocked. A later lock was manually initiated
by the owner, who signed in normally; read-only WTS checks observed locked and then unlocked.
These observations do not verify automatic proximity locking or the return reminder.
The PC's Pause setting was saved On after the interruption; normal delay/grace remain 10/30.

Local investigation found and repaired a separate concrete defect in normal Android advertising:
it selected PCs using grants saved at pairing and a single snapshot of Room preferences. A PC
permission granted afterward, or a preference update arriving after monitoring startup, could
therefore leave advertising stopped. Monitoring now observes both phone preferences and latest
authenticated PC grants, handles later revocation, and serializes replacement advertising slots.
An empty eligible set clears the stale active banner. Explicit monitoring retry can restart a
failed advertiser. This repair is built and locally tested, **not installed on the unplugged phone**.
The supplied screen alone does not prove this was the only cause of missing readings.
[Simple report](simple-test-report-2026-10-06.md).

## Current environment and retained state

- Windows 11 x64, installed `LapCont` LocalSystem service and ordinary-user companion. The
  6 October follow-up uses unlocked Windows session 7. After updates/restarts the service PID is
  17000. The companion was reopened for wireless proximity preparation (PID 10584); the
  machine-readable state records the latest observation.
  Earlier session 4 results do not complete a session 7 lock/sign-in test.
- Physical POCO 23122PCD1I / X6 5G, Android API36, ARM64 and 4 KiB pages, authorized USB debugging.
  Android microphone, Bluetooth Connect and Advertise grants are present. Pairing, Keystore and
  certificate pin were preserved through updates. The owner subsequently removed the saved PC
  or cleared phone enrollment, confirmed doing so, and manually paired again with a fresh QR.
  The new pairing initially had zero grants; authorized Camera, Microphone and Talk-back were
  selected through the ordinary PC permission controls. The subsequent wireless preparation
  observed Lock and Proximity enabled as well (all five grants selected). Removing the PC was
  not inferred to be an app data-loss bug.
- Campus Wi-Fi remains Public. The permanent rule is Private/LocalSubnet; the temporary owner
  approved Public rule permits only the installed service, TCP4433 and this phone's exact IP.
  The PC address changed from the previous saved endpoint to [private LAN address]; the phone remains
  [private LAN address]. The app's normal connection-settings API updated only the saved address and
  verified the unchanged enrollment against the authenticated PC. There is no adb reverse route.
- The owner unplugged USB for the wireless proximity test; adb now lists no devices. Phone
  connection/advertising was confirmed by the owner through the normal phone app. With temporary
  delay/grace values of 300/300 seconds, the owner initially reported calibrated 8/8 samples,
  a healthy observer and Near. The later supplied screen did not confirm that readiness; the
  fresh normal-app calibration is unverified. Phone participation remains On, Pause is saved On,
  and the normal 10/30-second timers are restored. No auto-lock occurred in the finite observation;
  the later lock was manual. [Metadata](testing/physical-wireless-proximity-20261006.json),
  [WTS observation](testing/physical-wireless-lock-observation-20261006.json).
  Locked-session media remains Off. The configured talk-back route is `Speakers (Realtek(R) Audio)`.
- The normal debug build was restored after optimized testing. Its installed SHA256 matches
  `artifacts/LapCont-debug.apk`: `ce8d55c2982357acccd2f23d37a89cb748ba0ccd9212470979e7eb2cd4ea5c34`.
  One earlier UI repeat was blocked while the phone was asleep/locked. Once the owner unlocked
  it, the current normal UI and talk press/release checks passed. Phone sign-in restrictions were
  not bypassed.

## Bugs reproduced and changes made

**Android process crash when the PC companion disappears.** The actual installed companion was
stopped during an active phone audio lease. A renewal coroutine threw an uncaught
`IllegalStateException: SESSION_AGENT_UNAVAILABLE`, terminating the app. The repository now stops
local media when its authenticated session reports an unavailable agent, handles renewal failures,
and preserves normal coroutine cancellation. A repeat against the actual installed companion
passed in 14.492 seconds: unavailable state observed, local media stopped, control retained,
companion returned, no media replay, and a fresh capture followed by Stop succeeded.
[Metadata](testing/physical-current-companion-recovery.json). The earlier 4 October exit-history
review found no crash in the history available then; the later reproduction supersedes that
observation as a statement about current product behavior.

**Talk-back congestion tears down desktop IPC.** Several real campus-LAN talk runs failed after
approximately 1–26 seconds. Service/companion diagnostics recorded media and control pipe EOF.
The eight-packet playback queue cancelled the whole session connection when bursts filled it.
The fix drops the oldest queued speech packet, retains bounded playback, clears an overflowing
PCM buffer before adding current audio, and ignores already queued packets for an ended talk
lease. A first installed update still failed because the service rejected late speech after Stop
and closed the peer connection. The service now discards only valid speech for this authenticated
phone's known ended stream; unknown streams, invalid direction and oversize packets remain rejected.
Ended leases never play. The final installed regression passed in 4.962 seconds: four bursts of
120 synthetic-silence Opus packets, Stop racing queued speech, control still connected, companion
available and talk idle. [Metadata](testing/physical-talk-burst-20261006.json).

The first attempt to install the service Stop-race correction hit a loaded assembly after SCM
reported Stopped. The updater now waits for the actual old service process to exit before copying.
A retry verified all 70 service/companion package files and unchanged protected PC identity,
then reached Running LocalSystem. Earlier failed-copy diagnostics remain historical evidence.

**Stale DHCP endpoint.** The 6 October test repaired the saved LAN address through the same
repository API used by Connection and privacy settings, without re-pairing or changing identity.
The finite test verified retained enrollment/pin and authenticated companion availability in
2.136 seconds. [Metadata](testing/physical-dhcp-repair-20261006.json). Changing campus addresses
can still require this normal settings update; automatic LAN discovery is not implemented.

The previously installed connection-ownership fix reuses the current client during Refresh and
monitor startup and ignores callbacks from replaced clients. The previous Windows BLE fix copies
the PC ID before a configuration document is disposed. Both remain in the current source.

## Physical results and their limits

| Check | Actual result | Limits / evidence |
|---|---|---|
| Manual fresh QR and local PC approval on campus LAN | Owner-confirmed pass | Historical [4 October report](physical-test-report-2026-10-04.md); no enrollment purge |
| ARM64 Keystore secret lifecycle, strict QR rejection, saved scoped commands | Passed | Real phone, independent grants and sign-in-required semantics; adversarial disposable tests retain their earlier scope |
| Current Refresh/monitoring ownership | Passed again on 6 October in 13.184 s | Four Refresh/connect-all repetitions, two monitoring starts, zero offline observations, active audio lease retained and Stop verified; [metadata](testing/physical-refresh-20261006.json) |
| Installed companion failure/recovery | Passed on fixed debug APK | No process crash or media replay; [metadata](testing/physical-current-companion-recovery.json) |
| Normal Compose Live View, actual Surface rendering and PC audio | Passed overnight in 7.795 s | 24 rendered frames, 50 decoded audio packets, Stop/background cleanup and idle resume; talk press/release not checked; [metadata](testing/physical-normal-ui-current-lan.json) |
| Current 6 October normal UI and talk press/release | Passed in 9.843 s | 21 Surface frames, 114 decoded PC audio packets, 24 phone talk packets; release/Stop/background cleanup and idle resume; [metadata](testing/physical-normal-ui-talk-20261006.json). An earlier locked-phone attempt did not reach UI actions |
| Optimized R8 APK on physical ARM64 | Finite UI smoke passed | Saved LAN enrollment, three Refreshes and normal Start/Stop controls; not debuggable. Frame counts, audibility and latency not measured; [metadata](testing/physical-optimized-current.json) |
| Physical BLE desk calibration and short beacon rotation | Passed in 47.103 s | Eight samples, five successful advertising starts, healthy observer, 30-second epoch crossed, final near. Median −46 dBm, threshold −56 dBm; [metadata](testing/physical-ble-current-lan.json) |
| BLE walk-away/return and OEM endurance | Unverified | The desk test kept Windows unlocked. It temporarily extended delay/grace to 300 s, then restored 10/30 s, Pause On and participation Off. One offline callback includes initial connection; this is not continuous endurance evidence |
| Later normal-app wireless proximity attempt | No auto-lock pass | 180.038 s WTS observation remained unlocked; supplied phone screen shows 0/8 samples and no RSSI. Later lock was manual. New Android permission/preference repair awaits installation and physical retest |
| Physical 480p/720p native media, talk packets, Stop and lease expiry | Passed on earlier build via explicit USB QA route | 61/64 images, 1,462/1,624 audio packets, 11/10 talk packets, three keyframes each. Command RTT is not LAN capture/render latency; [earlier report](physical-test-report-2026-10-04.md) |
| Real campus-LAN talk/backpressure and Stop race | Final installed build passed | 480 synthetic-silence packets across four bursts; control/companion retained and talk idle after Stop. Earlier failures remain separate evidence |
| Real phone voice to selected laptop speaker | Passed; owner heard voice | 30-second target, 33.619 s total test, 1,530 talk packets, idle after Stop; [metadata](testing/physical-talk-30s-20261006.json). Feedback, headset/focus changes and long endurance remain separate gates |
| Product phone Lock and matching SCM event | Lock-side assertions passed | Capture stopped, locked media denied and Request unlock kept Windows locked. Same-session sign-in wait timed out; [partial result](testing/physical-lock-partial.json) |
| Actual service restart/crash recovery | Both passed on first Windows talk update | Authenticated reconnect, unchanged grants, reconstructed unlocked session 7, no media replay and fresh capture/Stop. Restart test 6.534 s; crash test 58.615 s. SCM process recovery 2.351/6.176 s. [Restart](testing/physical-current-service-restart.json), [crash](testing/physical-current-service-crash.json). Final Stop-race correction was installed afterward; these timings retain that revision boundary |

The first optimized smoke attempt used the debug AndroidX test runner against an obfuscated APK
and failed with a missing Kotlin test class. The ordinary R8 app launched successfully. A Java-only
helper targeting the product package then passed. A later helper self-instrumentation experiment
could not access the own-app window and was reverted. The retained helper targets only LapCont,
requires explicit opt-in and intentionally stops the target process at instrumentation boundaries;
the normal app must be reopened afterward. These helper failures are not ordinary app startup passes
or failures. No media samples or unrelated UI trees are saved by these checks.

## Builds and reviewable packages

- Windows Release build/publish succeeded with zero build warnings/errors. The 6 October rerun
  passed all 35 xUnit tests, with zero failures/skips; result file is
  `windows/Agent.Tests/TestResults/physical-followup-20261006.trx`.
- Default debug/optimized release and optional Firebase debug/optimized release builds completed.
  Android lint has zero errors and 18 warnings. Six transport unit tests passed. Optional push
  compilation does not establish account provisioning or real notification delivery.
- Current optimized QA APK SHA256 is
  `982fa266f3155999654271772a29b38f277640079e94ba4974a4386d4d386183`.
  APK v3 verification passed, all ten ARM64/x86_64 native libraries meet 16 KiB load alignment,
  native ZIP entries are stored/aligned at 16 KiB, 13 license notice assets are present, and no
  private-key/QR/credential fixture entries were found. [Packaging metadata](testing/physical-packaging.json).
  This APK uses the public QA debug signing key. Production APKs remain unsigned signing inputs.
- After the wireless investigation, the 35 Windows tests and five new Android proximity
  regressions passed, as did all ten top-level relay tests with the race detector. The six
  unchanged transport results were reused by Gradle. Default debug/optimized release and
  optional push debug/optimized release builds of the repair completed; lint remains zero
  errors and 18 warnings. New files use the `LapCont-proximity-fix-*` prefix. They are QA or
  unsigned signing inputs, with no physical pass inherited from the older APKs.
  [Local repair checks and package hashes](testing/proximity-fix-local-qa-20261006.json).
- Downloadable Windows and Windows/Linux relay archives include the updated reports. Artifact
  sizes and SHA256 values are recorded in `artifacts/package-manifest.json`. The Windows package
  is framework dependent and requires separate x64 .NET Desktop and ASP.NET Core runtimes.
- The Windows fix is in `artifacts/release-v0.1.0/{service,companion}` and installed assemblies
  now match the final stage. `artifacts/update-physical-product.ps1 -RecoveryMatrix` validates the exact SCM
  installation, backs up binaries, verifies copied files and preserved protected identity,
  and coordinates finite restart/crash checks. It does not clear pairing or change sign-in.
  Earlier prompts were cancelled, but the owner subsequently approved the actual update/recovery
  helpers. All 70 files and retained PC identity were verified on the successful final update.
  No automatic approval-review rejection occurred for this update.

## Acceptance by phase

| Phase | Verified checkpoint | Remaining physical acceptance |
|---|---|---|
| 0 — Feasibility | Historical hardware session events, owner-heard speaker tone, codec/channel prototypes and emulator matrices | Other supported hardware/OS paths remain as documented in [Phase 0 report](phase0-full-test-report-2026-10-03.md) |
| 1 — Foundation/pairing | Actual installed LocalSystem IPC, real phone campus-LAN pairing, pinned identity and scoped status | Complete disposable physical token misuse/expiry/reuse and other-user matrix |
| 2 — Lock/events | Phone Lock confirmed by matching SCM event; Request unlock preserves lock; actual service restart/crash reconnect without media replay | Same-session normal sign-in completion, logoff/suspend/RDP |
| 3 — Proximity | Real advertiser/observer, calibration and short rotation | Walk-away/return timing, Bluetooth/permission failure, multiple enabled keys, OEM background/reboot |
| 4 — Live media | Real physical native video/audio and normal Surface controls; Stop/leases and locked denial | Full updated build matrix, camera/audio device changes, sustained performance and end-to-end LAN/WAN latency |
| 5 — Talk-back | Installed burst/Stop-race regression, normal UI press/release/Stop/background cleanup, and 30-second owner-heard voice passed | Broader cancel/route/focus/feedback and sustained endurance checks |
| 6 — WAN/push | Historical relay race/nested-TLS tests, stripped relay binaries, optional addon builds | Operator public-CA relay/account provisioning, actual mobile-data/router WAN, Docker runtime and FCM delivery |
| 7 — Hardening | Actual product install/ACL/firewall, optimized physical UI smoke, notices/signature/alignment/packages | Full uninstall/reinstall/default preservation/explicit purge, accessibility/OEM/OS matrix, sustained resources and production signing |

## What completes the remaining work

1. Complete audio route/focus/feedback and longer media/talk endurance checks. Installed service
   restart/crash, congestion/Stop, normal Live View/press-release and owner-heard voice checkpoints
   are now verified. Short successful runs do not establish sustained resource or latency acceptance.
2. Coordinate real walk-away/return and same-session Windows sign-in tests. Complete disposable
   multi-user/lifecycle/uninstall tests and sustained timing/resource checks without relying on
   short frame totals or command RTT.
   First install `artifacts/LapCont-proximity-fix-debug.apk` on the returning phone, then verify
   fresh 8/8 samples, a numeric median and a healthy Near state using the ordinary monitoring
   flow. The earlier finite BLE helper started its advertiser directly and did not exercise
   the normal repository's permission/preference selection that has now been repaired.
3. Supply operator relay/public certificate/Firebase configuration and a user-owned production
   signing key for the deployment-dependent checks. Runtime migration remains a shipping gate
   as documented in [compatibility](compatibility.md).

The detailed [physical acceptance checklist](physical-device-checklist.md) remains the release
gate. Genuine phone-approved Windows authentication is a separate advanced module; baseline
Request unlock still requires normal sign-in at the PC.

Five older protected QA stores and the accidentally retained pre-install phone UI file still
require owner-controlled cleanup after earlier automatic-review blocks. Their exact manual
helpers are documented in the [historical report](physical-test-report-2026-10-04.md). Those files
are excluded from packages and published evidence; no alternate deletion was attempted.
