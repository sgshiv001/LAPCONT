# Contributing to LapCont

Thank you for helping improve LapCont. This repository contains the Android app, Windows service and companion, optional relay, tests, build scripts and documentation. Read the [README](README.md) for setup and recorded acceptance limits.

## Documentation changes

1. Open an issue for a substantial change, or submit a small correction directly in a pull request.
2. Keep instructions clear for someone setting up LapCont for the first time.
3. Check relative links, heading links, tables and code blocks in the GitHub preview.
4. Preserve the distinction between implemented features, recorded test results and pending acceptance checks.

App tests are not required for documentation-only changes. If you add a test claim, identify the app build, device/platform, date and observed result. Keep skipped tests separate from passes.

## Bug reports

Use the [bug report form](https://github.com/sgshiv001/LAPCONT/issues/new?template=bug_report.yml). Include:

- The app version and build/package name, if available.
- Windows and Android versions, and which part of LapCont is affected.
- Whether the connection uses a local network or a configured relay.
- Steps to reproduce, expected behavior and actual behavior.
- Relevant error text or a screenshot with private details removed.

Check [troubleshooting](README.md#troubleshooting) and [known remaining work](README.md#remaining-work) first. Never include pairing QR contents, private keys, signing stores, tokens or service-account files in an issue or pull request.

## Feature requests

Use the [feature request form](https://github.com/sgshiv001/LAPCONT/issues/new?template=feature_request.yml). Explain the task you want to complete, the proposed behavior and any alternatives you have tried.

## Source changes

1. Explain the problem and intended behavior in an issue or pull request.
2. Keep each change focused and preserve existing owner permissions and privacy controls.
3. Follow the pinned toolchains and [build instructions](README.md#build-and-test).
4. Run the checks relevant to your change: Windows regressions for Windows behavior, Android unit/emulator tests for phone behavior, or relay tests for routing changes.
5. Include the validation result and any physical checks that remain pending in your pull request.

Generated packages, tool caches, signing keys and deployment credentials do not belong in source commits. The Android Gradle wrapper and dependency lock files are included for reproducible builds.

Contributions use the repository's [MIT License](LICENSE).
