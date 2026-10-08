# LapCont testing — 6 October 2026

**Available local testing is complete. Full release testing is still incomplete.**

The new Windows/phone interface and Back fix are now packaged. The latest phone update is
`artifacts/LapCont-UI.apk`; it includes the Bluetooth repair described below. Navigation,
dark mode and larger text passed emulator/component checks. See the
[UI update and morning instructions](ui-update-report-2026-10-06.md).

| Check | Result |
|---|---|
| Pairing and campus Wi-Fi connection | Previously passed on your phone |
| Live video, PC audio and phone talk-back | Passed; you confirmed hearing your voice from the laptop |
| Connection crash, service recovery and Stop fixes | Passed the documented physical checks |
| Automated checks | 35 Windows, 5 new Bluetooth, 6 transport and 10 relay tests passed; transport results were reused |
| Updated Android builds | Passed; no lint errors, 18 warnings |
| Bluetooth walk-away locking | Not passed: your screen showed 0/8 samples and no signal; the later PC lock was manual |

I fixed how normal Bluetooth monitoring follows permission changes made on the PC and
phone settings that update after monitoring starts. The new APK is ready, but it could not
be installed or physically tested after you unplugged and took the phone.

**When you return:** install `artifacts/LapCont-UI.apk` over the existing app, keep the phone beside
the PC, and verify fresh 8/8 calibration samples with a numeric signal and Near before the
walk-away/return test. Automatic locking is paused; the normal timers are restored to 10/30 seconds.

Still pending: physical testing of this update, walk-away/return, longer stability and battery
checks, multiple Windows users/uninstall, real internet relay/push setup, and production signing.
Staying on college Wi-Fi does not provide remote debugging access to your phone or complete
the separate mobile-data internet test.

The detailed results and build-specific limits are in
[the full report](physical-test-report-2026-10-06.md).
