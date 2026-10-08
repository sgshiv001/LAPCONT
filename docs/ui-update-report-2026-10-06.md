# LapCont UI update — 6 October 2026

The Windows companion and Android app now have a simpler matching design: clear cards, indigo buttons, readable text, fewer controls per screen, and short navigation animations.

## What changed

- **Windows:** Overview, Paired phones and Settings are separate pages. Phone permissions have their own page. QR pairing has a countdown. Back returns to the right page; Escape uses the same route. Stop all media stays visible. Advanced relay and Bluetooth settings are collapsed. Removing a phone asks for confirmation.
- **Phone:** Welcome, PCs, pairing, PC controls, Live view, nearby protection and connection settings use the same layout. Toolbar Back and Android Back return to the previous screen. Scanner Back closes the scanner first. Back from Live view stops local media and returns to PC controls. A quick Back press no longer falls through and closes the app.
- **Readability:** light and dark phone themes, larger touch targets, scrolling for larger text, clear offline/paused states, and “Waiting for signal” instead of “null dBm.” Removing a PC now asks for confirmation.
- The APK includes the earlier Bluetooth repair that reacts to updated PC permissions and can restart advertising after a failure.

## Checks completed

| Check | Result |
|---|---|
| Windows build | Passed, zero compiler warnings/errors |
| Windows regression tests | 35 passed |
| Windows navigation/layout component checks | 14 passed; six off-screen previews rendered |
| Android unit tests | 8 passed |
| Transport unit tests | 6 unchanged passing results reused by Gradle |
| Android UI navigation | 6 passed on each tested profile: API 29 with 130% text, API 37 light, and API 37 dark with 130% text |
| Physical microphone test | Skipped in emulator; requires the real phone and owner |
| Optimized APK | Built and signed with the existing QA key; launch and pairing/Back smoke checks on API 29 and API 37 with 16 KiB pages |
| Optional push variant | Debug and optimized unsigned builds succeeded |
| Package verification | Signatures, native alignment, notices and package hashes checked; no private keys or test identity files included |

The emulator PC is disposable example metadata, not a real pairing. Live-view navigation was tested without starting a real camera or audio stream. Earlier real-phone media results belong to earlier APKs; the new physical Back-stops-media test is prepared but has not run on this APK. Production signing, operator relay/push setup, Bluetooth walk-away/return and other previously outstanding physical checks remain open.

Windows desktop interaction stopped when the lock screen appeared. WPF component rendering and navigation checks completed without desktop input or service connections. The full on-screen Windows check and physical installation of this update await your return.

## Open it in the morning

1. **Phone:** install `artifacts/LapCont-UI.apk` over your existing LapCont app. Keep the app's data so its saved pairing remains. This is the smaller optimized QA APK (about 8.5 MiB). The debug alternative is `artifacts/LapCont-UI-debug.apk`.
2. **Windows:** extract `artifacts/LapCont-Windows-UI-v0.1.0.zip`. Choose **Quit** in the old LapCont tray menu, then open `Open LapCont.cmd` from the extracted folder. Your installed service and saved pairing can stay in place. The installer is for a fresh installation; it intentionally rejects an existing service.
3. Open your saved PC → Connect / retry. Check Live view and nearby protection. Use the top-left Back arrow or Android Back; it should return one screen, with the scanner closing before pairing is left.

Screenshots are in `artifacts/ui-preview-20261006`. Windows images are off-screen component previews. Phone images use the example “Demo PC,” with no real media captured.

Machine-readable evidence: `docs/testing/ui-update-20261006.json`.
