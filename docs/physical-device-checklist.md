# Physical acceptance checklist — baseline 0.1.0

Current physical results and blocking steps are in the
[6 October follow-up](physical-test-report-2026-10-06.md). Automated/emulator results are in
[the product report](product-test-report-2026-10-03.md).
Unchecked items are unverified release gates. Use disposable accounts/enrollments and preserve
normal Windows sign-in/recovery. Do not record camera/audio to test latency; use a visible timing
reference observed directly or metadata-only synchronized capture/render instrumentation.

The [4 October physical report](physical-test-report-2026-10-04.md) records actual product
installation, manual campus-LAN pairing, physical ARM64 480p/720p native media over cable,
matching phone Lock/SCM completion and owner-confirmed stable LAN refresh after the connection
fix. Eight physical BLE calibration buckets were collected. Combined items below remain
unchecked when any part (such as uninstall, return, audibility or sustained use) is unverified.

The updated Android app also passed actual companion failure/recovery with no crash/replay,
and repeated Refresh/monitoring on current campus LAN. Normal Surface Live View and optimized
physical UI smoke passed overnight. Eight BLE samples and a short rotating-beacon observation
passed; walk-away/return remains unverified. Installed Windows talk-back burst/Stop-race and
normal press/release checks now pass, as do actual SCM restart/crash recovery checks. Combined
acceptance items below stay unchecked when only these narrower checkpoints passed.
The 30-second real voice test also passed with 1,530 talk packets and owner-confirmed laptop
speaker output; feedback, audio routes/focus and endurance still need their own checks.

The later normal wireless attempt stayed unlocked during a three-minute WTS observation.
The supplied phone screen had 0/8 calibration samples and no RSSI; a later Windows lock was
manual. The normal advertising permission/preference repair passes five local regressions
but is not installed on the unplugged phone. Install the `LapCont-proximity-fix-debug.apk`
before repeating ordinary monitoring, fresh calibration and walk-away/return. Historical
BLE helper results do not verify this repaired normal flow.

## Enrollment and Windows service

- [ ] Install/uninstall the full product from an elevated Windows session; inspect LocalSystem,
      service recovery, ProgramFiles/ProgramData ACLs, firewall scope and user companion startup.
- [ ] Real Android ARM64 phone on physical LAN: scan QR, compare fingerprints, approve minimal
      grants, verify scoped state. Reject expired/reused QR, altered identity and wrong phone.
- [ ] Two Windows users/fast switching/RDP: isolated grants and privacy settings; hostile other-user
      pipe access/server impersonation denied. Missing/logged-off companions report unavailable.
- [x] Phone Lock: accepted, then completed only after matching SCM Windows event. Request unlock
      leaves the session locked and tells the user to sign in at the PC.
- [ ] Complete the coordinated normal-sign-in check for that same Windows session. The physical
      run's four-minute SessionUnlock wait timed out; a later session does not complete that run.
- [x] Restart the actual installed service: authenticated reconnect, reconstructed or unknown
      session state, unchanged grants, no capture replay and a fresh capture/Stop.
- [x] Terminate the actual service process and verify configured SCM recovery, then authenticated
      reconnect with no capture replay and a fresh capture/Stop.
- [x] End/relaunch the ordinary-user installed companion during audio: local media stops without
      crashing the phone, control remains, and return does not replay capture.

Service restart/crash timings were recorded on the first Windows talk update; the final
ended-packet correction was installed afterward. Their exact revision boundary is in the report.
- [ ] Actual logoff, suspend/resume/hibernate, network loss and unreachable PC status/cleanup.

## BLE on actual phone and PC

- [ ] Android advertiser support/permissions/visible foreground monitoring; Windows central/radio health.
- [ ] Confirm actual 23-byte legacy payload receipt, rotating identity matching and one-second buckets.
- [ ] At least eight calibration samples; seven-sample median; inadequate data displays retry.
- [ ] Walk-away for ten seconds and missing-beacon grace of 30 seconds while armed/healthy.
- [ ] Bluetooth off/revoked permission/observer failure/resume produces unknown/degraded, not measured-away.
- [ ] All explicitly enabled phone keys away locks; a near enabled key prevents all-away policy.
- [ ] Return remains stable before one sign-in reminder; duplicate/cooldown behavior; never unlocks.
- [ ] One phone/one active advertising PC supported; multiple-PC advertising reports unsupported.
- [ ] Force-stop/reboot/OEM background/battery restrictions and Pause; no indefinite monitoring claim.

## Media and talk-back

- [ ] Physical ARM64 Keystore TLS, Opus/JNA native ABI and MediaCodec Surface lifecycle.
- [ ] 720p30/2 Mbps default and 480p30/1 Mbps negotiation on each supported camera; hardware and
      software fallback, busy/unsupported modes and unplugged/permission-denied devices.
- [ ] Camera and microphone independent grants, visible activity, local Stop and immediate revocation.
- [ ] Default stop-on-lock, owner-approved locked media, disabling that permission while locked,
      logoff, slow receiver and capture fault all release devices without hidden capture.
- [ ] Push-to-talk press/release/cancel/Home/rotation, speaker route and microphone permission recovery;
      real audible output and feedback behavior. PC mic playback muted during talk.
- [ ] Headset/Bluetooth route changes, audio focus interruption and Surface destruction stop safely.
- [ ] LAN median/p95 capture-to-render and audio delay, measured FPS/bitrate, sync and resource usage;
      publish sample count/method. Check 200 ms LAN goal with actual end-to-end timing.
- [ ] Memory/CPU/thermals/battery/background power over sustained use, not only short emulator checks.

## WAN and optional push

- [ ] Deploy pinned non-root Docker relay with valid trusted-CA certificate renewal and writable UID10001
      revocation volume. Independently provision role credentials, inspect minimal health and logs.
- [ ] Real phone on mobile data connects to PC behind router through outbound relay, without PC WAN
      port forwarding; pinned identity survives LAN/relay switching and IP changes.
- [ ] Wrong route/phone/role credential, relay downtime/duplicate connection/rate limits and restart.
- [ ] Revocation while offline persists/retries and invalidates routing after relay restart.
- [ ] WAN capture-to-render median/p95 under representative loss/jitter; assess 500 ms goal separately
      from command RTT. Slow/blocked media does not prevent Stop/Lock processing.
- [ ] Optional Firebase operator project/token provisioning, HTTP v1 sender and verified catch-up;
      delayed/missing/duplicate hints never invent an event or unlock Windows.

## Shipping

- [ ] User-owned production APK signing key; verify signature/16 KiB ZIP+ELF alignment on ARM64 device.
- [ ] Clean supported Windows editions/runtime installation; migrate .NET 8 before its support deadline.
- [ ] Review dependency advisories/license notices and immutable container digests at release freeze.
- [ ] Accessibility/large fonts/light+dark themes/permission denials on physical devices.
- [ ] Uninstall/default identity preservation/explicit purge/other-user startup cleanup and recovery.

The temporary Phase 0 SCM lock/unlock and user-confirmed speaker tests are useful historical
hardware evidence; they do not substitute for the complete product checks above.
