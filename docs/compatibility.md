# Compatibility and dependencies — baseline 0.1.0

The implementations exist; verified platform paths and acceptance limits are distinct.

The [6 October physical report](physical-test-report-2026-10-06.md) adds current LAN Refresh
regression, Android companion-loss crash recovery, normal Surface UI and optimized ARM64
smoke, plus eight BLE samples across a short beacon rotation. Installed Windows talk-back
congestion/Stop races, normal press/release, and service restart/crash recovery passed. The older rows below retain their
checkpoint limits where the complete combined acceptance item is still unverified.

| Area | Verified here | Still unverified |
|---|---|---|
| Host | Windows 11 Home Single Language build 26300, x64; .NET 8.0.31 | Windows 10/other editions and production runtime migration |
| Service | Actual Windows 11 elevated product installation, Running LocalSystem, automatic startup/recovery, ACLs, Private firewall and current-user startup; matching product phone Lock/SCM completion and stop-on-lock; actual service restart/crash without media replay; historical WTS probe | Uninstall/reinstall, complete same-session normal sign-in sequence, logoff/suspend, multi-user/RDP hostile-client isolation |
| IPC | Explicit ACLs, OS session/SID checks; development scope rejection; actual installed LocalSystem SCM server verification | Cross-user deployment |
| Phone | API29/37 emulators; physical POCO API36 ARM64/4 KiB Keystore, manual pairing/campus LAN, current Refresh/monitoring regression, actual companion-loss crash fix/recovery | OEM background behavior, sustained connection endurance and other ARM64 OS targets |
| Media | Actual PC hardware H.264 480p/720p native rendering and Opus microphone/talk transport on physical ARM64 over cable; current Surface and press/release controls/background cleanup; installed talk burst/Stop-race fix and 30-second owner-heard voice; optimized ARM64 finite UI smoke; keyframe recovery/Stop/leases; installed-service stop-on-lock and locked-media denial; historical software fallback | Audio route/focus/feedback, end-to-end timing and sustained performance |
| BLE | Deterministic calibration/filter/health/multi-key policy; eight real paired advertiser/observer calibration buckets and short 30-second beacon-rotation observation | Complete rotating-beacon endurance, walk-away/return, OEM background/advertiser slots |
| Relay | Go race tests, nested TLS through opaque forwarding, Windows/Linux x64 binaries | Docker runtime and real public CA/mobile-data WAN deployment |
| Push | Default Firebase-free; optional Firebase addon compiles and HTTP v1 sender provided | Operator account integration and real delivery |

## Reproducible pins

.NET SDK 8.0.425/runtime 8.0.31; Vortice 3.8.3, SharpGen.Runtime 2.4.2-beta, NAudio 2.2.1,
OpusSharp 1.6.8/native package 1.6.1.4 (libopus 1.6.1), QRCoder 1.7.0. Android uses Gradle 9.6.0,
AGP 9.4.1, compile/target37/min29, build tools36.0.0, JDK25.0.3 tested/JVM17, Kotlin Compose/JVM
plugins2.2.10 with AGP built-in Android Kotlin, coroutines1.10.2, Compose BOM2026.02.01,
Activity1.10.1/Lifecycle2.9.0, Room2.8.4, CameraX1.5.2, Hilt2.60.1/KSP2.3.9, OkHttp4.12.0,
ZXing3.5.3 and JNA5.17.0. Instrumentation uses Espresso3.7.0/runner1.7.0/JUnit1.3.0;
Espresso3.7 fixes API37 InputManager lookup used by the Compose test infrastructure.
[AndroidX Test releases](https://developer.android.com/jetpack/androidx/releases/test).

Go source declares1.25 compatibility and selects toolchain1.27.1; coder/websocket1.8.14.
Docker build/runtime tags are golang1.27.1-alpine3.23 and alpine3.23.3, verified to exist in
Docker Hub; image deployment is unverified. Optional Firebase Messaging25.1.3 is isolated behind
-PenableFcm=true. Package lockfiles record direct/transitive resolved dependencies, including
Android release configurations. The optional push library excludes only legacy Kotlin common
metadata from dependency locking because AGP lint resolves that variant differently; a strict
2.3.21 constraint pins it instead. Its actual JVM runtime and all other dependencies remain locked.
Both ordinary builds were repeated without refreshing locks. Native preparation independently
verifies the package SHA-256.
See [notices](../THIRD_PARTY_NOTICES.md) for upstream licenses.

These pins record the exercised build, not a claim that every dependency is the newest.
Review advisories and platform/toolchain compatibility at each shipping freeze. Do not change
pins merely to hide a native or lifecycle failure.

## Runtime and servicing limits

.NET8 support ends 10 November2026. Migrate all Windows processes/shared libraries together
to a supported runtime, then rerun IPC, SCM/WTS, TLS1.2/1.3, native media and installer checks.
[Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Windows10 cannot be assumed to provide Schannel TLS1.3; the implementation permits TLS1.2
only with ECDHE/GCM. Forced TLS1.2 provider tests passed on this Windows11 host, which does not
prove Windows10 deployment. Record edition/build and applicable OS servicing arrangement before
committing Windows10 support.
[Schannel matrix](https://learn.microsoft.com/en-us/windows/win32/secauthn/protocols-in-tls-ssl--schannel-ssp-).

## Measurements

Use [the product report](product-test-report-2026-10-03.md) for measured package sizes and
command timing. The Windows ZIP is framework dependent; extracted files and separately installed
runtimes have different sizes. Optimized Android release and debug APK sizes are reported separately.
Go binary targets refer to a single stripped binary, not the container image. Command RTT is not
capture-to-render latency. Physical latency, sustained resource usage and battery remain gates.
