# LapCont baseline implementation and testing report

Run dates: 3–4 October 2026, Asia/Kolkata. Scope: Phases 1–7 of the enhanced prompt, following the
completed Phase 0 feasibility work. **The baseline implementations are present and the listed
automated integrations passed. Physical deployment acceptance remains incomplete.** Genuine
phone-approved Windows authentication is outside this baseline; Request unlock tells the owner
to sign in at the PC and never changes verified lock state.

For later installed-product and physical-phone results, see the
[6 October follow-up](physical-test-report-2026-10-06.md). This report remains the historical
emulator/development checkpoint.

## Delivered implementation

| Phase | Delivered | Acceptance limits |
|---|---|---|
| 1 — Foundation/pairing | .NET coordinator, WPF companion, authenticated separate IPC, protected identities, Android Keystore/Hilt/Room, QR/local consent, scoped status | Actual PC-to-emulator pairing on both Android versions passed; physical LAN and installed LocalSystem IPC remain gates |
| 2 — Lock/events | Authorized interactive Lock dispatch, matching SCM completion/timeout, Request unlock, WTS startup state, five-minute event journal/notifications | Full product unattended phone-Lock/SCM and logoff not exercised; actual WTS/temporary SCM feasibility checks are historical separate evidence |
| 3 — Proximity | Both rotating BLE advertiser and Windows observer, calibration, median/hysteresis/timers, all-enabled-keys policy, return reminder, pause/resume | Deterministic policy passes; physical beacons/walk-away/OEM background remain unverified; one active advertising PC per phone |
| 4 — Live media | Real MF capture, hardware preferred H.264 with software fallback, SPS/PPS/IDR recovery, Android Surface rendering, WASAPI-to-Opus playback, independent grants/leases | Real hardware video/audio across protected channels passed at 480p, including explicit locked-session development opt-in; product 720p, installed SCM lock transitions and physical phone latency remain gates |
| 5 — Talk-back | Foreground AudioRecord/Opus to real WASAPI player, press/release/cancel and short leases, selected speaker and phone playback muting | Actual packet flow/cleanup passed; physical audible talk/feedback/routing still needs owner/device verification |
| 6 — WAN/push | Bounded authenticated opaque Go relay, LAN fallback, persistent revocation, optional Firebase hint addon/operator HTTP v1 sender | Relay race/nested-TLS tests and optional addon builds passed; real mobile-data WAN, Docker deployment and FCM delivery remain unverified |
| 7 — Hardening | Installer/uninstaller, permission/lifecycle UX, diagnostics, notices, locked dependencies, optimized APK and Windows/relay packages, reports/checklists | Builds and listed smoke checks pass; elevated install, physical OS matrix, sustained power/resource and latency acceptance remain incomplete |

The former Phase 0 screens are retained in `android/app` and Windows probe modes. The product
is `android/mobile`, `LapCont.Service --product-service`, and the default WPF companion.

## Environment and methods

Windows 11 Home Single Language build 26300 x64; actual ASUS webcam/microphone and Windows
capture/playback APIs; .NET SDK8.0.425/runtime8.0.31. Android10/API29 x86_64 has 4 KiB pages;
API37 x86_64 has 16 KiB pages. Both use Android Studio SDK emulators. Build toolchain and
dependency pins are documented in [compatibility](compatibility.md).

The actual Windows coordinator and companion ran in explicit current-user development mode,
with private QA identities under LocalAppData outside the OneDrive repository. Emulator TCP
reverse carried the real WSS and inner mutual TLS bytes to that coordinator. Pairing used
fresh expiring QR data, actual non-exportable Android Keystore client identity and the real WPF
local approval controls. All five grants applied only to disposable QA enrollments. The host
reported an actually locked Windows session in the final runs. Its default policy rejected new
media with zero frames. The locked-media option was enabled through the actual local WPF setting
for this disposable QA profile; turning it off stopped active capture, and denial was rechecked
on both phones. The option is restored off; it does not change Windows sign-in. No fixture
private key was imported into the product phone. TCP reverse is not physical LAN evidence.

No raw camera/audio recording, media export, secure-desktop bypass, system sign-in change or
public relay deployment was used. The current shell lacked an elevated Administrator token;
the full product SCM installer was therefore not executed. Temporary Phase 0 service and
audible speaker evidence remain separately documented in
[the historical report](phase0-full-test-report-2026-10-03.md).

## Executed checks

| Check | Observed result | Boundary exercised |
|---|---|---|
| Windows Release solution | Passed, zero warnings/errors | Nine projects including product core compile |
| Locked NuGet restore | Passed | Checked package content/version lock state |
| C# xUnit | 35 passed, zero failed/skipped | Framing/strict JSON, real TLS rejection/tamper/replay, command expiry/identity/schema, pairing atomic use/expiry, bounded idempotency, BLE timing/filter/health/calibration/return |
| Kotlin JVM | 6 passed | Shared binary vector and strict JSON/length handling |
| Android API29 product matrix | 4 passed | Keystore signing/secret lifecycle, QR rejection, actual video/audio/talk/recovery/Stop/leases, critical Compose pairing/dashboard labels |
| Android API37 product matrix | 4 passed | Same boundaries on the newer OS/16 KiB emulator |
| Actual enrollment integration | Passed on both OS versions | Local consent, pinned mutual TLS, protected scoped status, separate media attachment, wrong-session and unknown-parameter rejection, sign-in semantics, repeated Stop |
| Real media integration | Passed on both OS versions | Actual laptop hardware encoder to Android decoded images, microphone Opus decode, phone AudioRecord packets to PC, fresh IDR recovery, 5s talk and 30s capture expiry |
| Locked-media privacy | Passed | Default denial and zero media frames on both OS versions; actual local setting disabled during an active stream stopped capture and prevented renewal; default restored |
| Connection loss/revocation integration | Passed | Capture stopped on disconnect; reconnect stayed idle; ended stream renewal rejected; revocation stopped active audio and rejected old phone identity. Explicit Stop-before-Start cancellation regression was added and exercised in the final API37 run |
| Go relay race suite | 10 top-level tests plus 4 subcases passed | Role/route/phone auth, offline/duplicate arbitration, exact opaque bytes, text/oversize rejection, disconnect cleanup, bounds, persistent revocation, real nested mutual TLS round-trip/wrong-phone rejection |
| Android default debug/release/R8 | Passed | Firebase-free product builds; release is optimized and unsigned for production signing |
| Optional push debug/release | Passed | Explicit Firebase addon compiles; separate mode lockfile prevents contaminating the default dependency graph. Only legacy Kotlin common metadata uses a strict version constraint instead of a lock, as explained in compatibility |
| Android lint | Zero errors, 18 warnings in default build | Mostly newer-version notices plus resource-shrinking/KTX suggestions; compiler/DSL deprecation warnings also remain |
| Optimized release UI smoke | Passed on API37 emulator | Test-signed R8 build launches, retains enrollment/Keystore, connects, starts audio-only tracks and releases PC capture on Home/background |
| PowerShell scripts | Syntax checks passed | Installer/uninstaller/development/pairing/diagnostic/relay helpers parse; elevated installation itself remains unverified |
| Packaging | Windows, optimized APKs, stripped relay binaries generated | Sizes, native alignment, signatures and notices are checked separately in packaging evidence |

Tests are meaningful boundary checks, not a coverage percentage or an assurance that untested
platforms work. The source contains opt-in destructive-enrollment tests; run them only against
disposable QA enrollments. APK debug signing is a testing convenience, never production signing.
The final rebuilt R8 APK was installed again: it launched, connected with the saved Keystore,
displayed the verified locked state, and showed MEDIA_LOCK_POLICY when Live View was requested.
The earlier audio-only/background smoke remains separate evidence; the final check started no capture.

## Measured media and command timing

Metadata-only measurements are retained in [testing](testing/). The frame counts below come
from a short camera interval followed by talk/expiry checks; they are not a sustained FPS test.
Requested preset: 640×480,30 fps,1 Mbps; mono48 kHz Opus,960 samples/20 ms,64 kbps. Both targets
reported the actual **Media Foundation hardware** encoder. Three or more keyframes cover initial,
periodic and explicitly requested recovery. Packet/image totals and exact timings are in the
per-API measurement JSON because repeat runs can change totals.

Each final run measured 20 authenticated status round trips over emulator TCP reverse.
The JSON contains sorted median (index10) and p95 (index18). These are command timings; they do **not** establish
200 ms LAN or 500 ms WAN capture-to-render latency. Live View honestly displays latency unavailable.

| Final target | Matrix duration | Rendered images | Decoded audio packets | Talk packets | Status median / p95 |
|---|---:|---:|---:|---:|---:|
| API29 | 45.261 s | 62 | 1,592 | 12 | 3.466 / 48.203 ms |
| API37 | 45.428 s | 63 | 1,627 | 11 | 3.825 / 7.773 ms |

Physical network, capture/render synchronization, achieved FPS/bitrate, audio delay and A/V drift
remain unmeasured acceptance gates.

One R8 audio-only UI snapshot measured Android PSS30,386 KiB/RSS161,224 KiB and Windows working
sets about49 MiB coordinator/73 MiB companion, with private allocations about129/177 MiB. These
are process snapshots affected by paging/GC and instrumentation. Cumulative CPU seconds are
recorded, not CPU utilization. They do not establish sustained load, power, thermal or battery goals.

## Package measurements

The optimized default unsigned APK is approximately8.3 MiB; optional push approximately9.0 MiB.
The debug APK is approximately41.6 MiB and is not the 30 MB release-size target. The test-signed
optimized APK uses the ordinary Android debug key and is labelled accordingly. Both ARM64 and
x86_64 native assets are included; ARM64 execution remains unverified.

The Windows framework-dependent ZIP is approximately16 MiB, below the50 MB archive target.
The extracted duplicate service/companion dependencies are approximately56 MiB; installed .NET
Desktop/ASP.NET runtimes are separate prerequisites. Do not confuse archive size with installed
footprint or a self-contained package.

Stripped single relay binaries are approximately7.4 MiB Windows and7.2 MiB Linux, below10 MB each;
container image size is separate. The final `artifacts/package-manifest.json` gives exact byte
counts and SHA-256 hashes for every delivered archive/APK/binary. No production signing key or
operator credential is included. Notices and upstream license texts accompany packages.

## Failures found and fixed

- Android10 TLS required Keystore EC keys permitting prehashed NONEwithECDSA signing in addition
  to SHA256withECDSA. Real non-exportable identities now authenticate on both OS targets.
- Android10 parsed .NET UTC offset timestamps differently; OffsetDateTime conversion fixed it.
- The hardware MF encoder required asynchronous unlock/event handling and output stream-change
  negotiation. The live pipeline now handles these and reports its actual encoder path; software
  fallback is limited to before the first emitted access unit.
- Slow cold-start native codec initialization and small intermediate queues could stop streams.
  Native audio loading and video decoder creation now precede capture. Bounded PC queues allow
  32 frames/4 MiB and the phone 64 frames/4 MiB; the encrypted carrier allows 64 chunks/1 MiB.
  Overload still stops safely. Temporary decoder input unavailability is retried while draining
  output for at most 500 ms, preserving complete dependent access units. Capture permits a
  cancellable 250 ms IPC enqueue wait; its deadline cancels the pending write so it cannot enqueue
  after Stop. Android's
  [MediaCodec buffer contract](https://developer.android.com/reference/android/media/MediaCodec#dequeueInputBuffer(long))
  treats a temporarily unavailable input buffer as a normal result, rather than a codec failure.
- API37 media tests needed a foreground activity for modern audio-focus rules; test infrastructure
  needed Espresso3.7's InputManager fix. The corrected suite tests the supported foreground path.
- Independent IPC request workers removed a coordinator/control deadlock. Media and control remain
  separate so Stop is not queued behind video.
- Stream IDs are retired on Stop/fault/disconnect, including Stop-before-Start. Canceled starts
  send bounded cleanup despite coroutine cancellation; they cannot silently renew ended capture.
- A second channel closing after control disposal caused a CancellationTokenSource exception;
  closing is now idempotent. Permission changes and disabling locked media stop affected sessions.
- BLE timing fixes reject stale/future IPC samples, clear weak timing across gaps/hysteresis,
  rebuild after health changes, and preserve stable return/cooldown state. Changing the owner's
  away threshold now updates that owner's calibrated thresholds without skipping new-phone
  calibration. Physical validation remains.
- Persistent relay revocation closes active routes even if writing revocation state fails; the
  response reports failure and local retries remain pending. The container state directory is owned
  by its non-root UID. Default and optional Android dependency locks are separate.

## Cleanup and remaining acceptance

The following describes this emulator/development run. Later installed service and physical
phone testing is recorded separately in [the 4 October follow-up](physical-test-report-2026-10-04.md).

Owned QA hosts and emulators are stopped; the phone product data/Keystore and test packages are
cleared, and owned TCP reverse routes removed. The locked-media option is restored off. Expiring
QR files were removed after pairing. **Five temporary DPAPI-protected PC QA files remain under
LocalAppData:** automatic approval review rejected both the combined cleanup and a narrower
credential-only deletion with the reason “blocked by policy.” A reviewable manual cleanup script
is provided at `artifacts/cleanup-private-qa.ps1`; it removes only those five files and was not run.
The retained directory contains QA metadata and protected stores, outside the repository/packages.
No product SCM installation/firewall rule was created in this run. SDKs/AVDs and user system settings
are preserved; build tools and artifacts remain ignored by Git. Cleanup verification and the
retained-file limitation are recorded in `testing/cleanup.json`.

The next release acceptance requires a physical ARM64 phone, actual LAN/mobile-data WAN, physical
BLE walk-away/return and OEM background checks, elevated full product SCM install/uninstall,
multi-user/RDP/logoff/suspend tests, installed-service lock transitions and real speaker/feedback checks.
Complete [the checklist](physical-device-checklist.md), measure physical latency/resource/power,
review dependency advisories and migrate .NET8 before its support deadline. These are not marked
passed because emulator/feasibility results cannot establish them.
