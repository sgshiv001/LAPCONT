# LapCont 0.1.0 setup

The baseline application is implemented. This build is a test release: emulator and local
Windows hardware results are in [the product report](product-test-report-2026-10-03.md).
Complete [physical acceptance](physical-device-checklist.md) before relying on automatic
locking or distributing a production release. Request unlock asks you to sign in normally
at the PC; it never authenticates or unlocks Windows.

## Windows installation

Download `LapCont-Windows-UI-v0.1.0.zip` from the
[test release](https://github.com/sgshiv001/LAPCONT/releases/tag/v0.1.0-test.1), or generate
the standard Windows package with `scripts/package-release.ps1`. Extract into a new folder. Install
the **x64 .NET 8 Desktop Runtime and ASP.NET Core Runtime** first. This package is framework
dependent; the separate runtimes are excluded from the package size. See Microsoft's
[runtime downloads](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

From an Administrator PowerShell window in the extracted folder:

```powershell
.\install-windows.ps1 -CompanionAtSignIn
```

The installer creates the `LapCont` LocalSystem service and allows TCP 4433 only from
the local subnet on Private Windows network profiles. Open `companion/LapCont.Session.exe`
as your ordinary Windows user. Each Windows user opens their own companion and pairs their
own phone. Automatic companion startup applies to the installing user only. Do not expose
the PC listener on the Internet. Installer execution requires an elevated session; it has
been validated on this Windows 11 host in the [physical test run](physical-test-report-2026-10-04.md).
Full uninstall, multi-user and other Windows editions remain acceptance checks.

If an installation stopped before creating the service, inspect the incomplete target first.
For the exact same package, `-ResumeIncompleteInstall` verifies every existing file against
the package before resuming. It rejects redirected roots, unexpected files and changed binaries.
It is not an upgrade option for an existing service.

Administrator configuration is `service/appsettings.json`, under `LapCont`, using the
published snake_case fields. Changing the listener port requires adjusting the firewall.
Per-user privacy/proximity settings are changed locally in the companion and stored separately
for each Windows SID. Machine identities, enrollment, events and relay credentials are DPAPI
protected with restricted ACLs under `%ProgramData%/LapCont/Service`.

## Android and pairing

Install `LapCont-UI.apk` from the [test release](https://github.com/sgshiv001/LAPCONT/releases/tag/v0.1.0-test.1).
This optimized test APK uses the public QA/debug identity. Install over the matching test app
to preserve pairing. Debug and optimized unsigned variants are also available through the build
instructions; sign production builds with your own release key before distribution.
The Android application ID is `com.lapcont.app`; the retained feasibility
probe uses `com.lapcont.app.phase0`.

1. Put the phone and PC on the same private network. Open the PC companion and choose Pair.
2. Scan its QR in Android, or use the accessible QR text input. Verify the PC fingerprint.
3. Compare the phone fingerprint on the PC. Select only the grants you want and approve locally.
4. On Android, connect saved PCs and inspect the independent connection, Windows lock and media state.

The QR expires after 60 seconds and has one enrollment reservation. Cancel and retry with
a fresh QR after expiry/failure. A changed PC identity requires revoking the old enrollment
and pairing again. Phone TLS keys stay non-exportable in Android Keystore; secrets use Keystore
AES-GCM storage outside backup. Neither QR nor diagnostic exports contain private keys.

On shared campus/Public Wi-Fi, the default firewall rule deliberately does not allow inbound
connections. Keep shared networks Public. A temporary owner-approved rule scoped to the exact
phone address was used for physical testing; it is not installed by default. Campus client
isolation may also prevent direct connections. Generate a new QR after the PC address changes.
On POCO, approve USB installation when needed and grant microphone permission through the
normal LapCont prompt; automatic adb grants/input may be restricted by the phone.

Camera, microphone and talk-back grants are independent. Media defaults to stopping on PC lock;
the PC owner can explicitly allow locked-session media, subject to physical testing. The tray is
not visible on the Windows lock screen. Use the camera privacy light/OS indications where provided.
Capture always stops on logoff, connection loss, local Stop, revocation or lease expiry. New
connections stay idle until a new live request. Select a real speaker endpoint for talk-back
if the Windows default points to an inaudible virtual device.

## Proximity and monitoring

Enable the Proximity grant and key locally at the PC, unpause the PC policy, and enable phone
advertising after granting Bluetooth permission. Start visible Android monitoring. At the desk,
run calibration: eight distinct valid one-second samples are required. A confirmed near state
arms the policy. All enabled, calibrated proximity phones must be away before locking. Unknown
or unhealthy observation does not count as measured-away. Returning sends a sign-in reminder.
Changing the PC's away-threshold setting overrides that owner's stored calibrated thresholds;
it does not skip calibration for new phones. Saving unrelated settings preserves calibration.

This phone implementation supports **one advertising PC at a time**. Additional paired PCs can
use manual control; trying to enable multiple advertising PCs reports the unsupported combination.
BLE hardware support, background restrictions and walk-away performance need a physical phone.
Force-stop/reboot do not silently restart monitoring. Android notifications and background delivery
depend on permissions, network availability and OS policy.

## Optional self-hosted WAN relay

Deploy `relay/compose.yaml` or the relay binary on an operator-managed host. The relay needs a
normal trusted-CA TLS certificate, a hash-only credential file and writable persistent revocation
state. Docker tags are pinned; Docker deployment was not run on this machine. The container runs
as UID 10001, with read-only filesystem, no capabilities and a dedicated `/state` volume.

Generate independent role credentials in a **new private directory outside this repository**:

```powershell
.\relay\new-credentials.ps1 -PcId <enrolled-pc-id> -PhoneId <enrolled-phone-id> -PrivateOutputDirectory <private-folder>
```

Copy only `routes.json` to the relay's protected secrets mount. Provision `agent.token` locally
through the PC companion's relay settings, and `phone.token` through Android connection settings.
Use a `wss://your-domain:8443/ws` URL. Never put tokens in URLs, source control or shared logs.
The PC connects outbound; the phone tries pinned LAN and then relay. The outer relay connection
uses normal CA validation; the inner standard mutual TLS still pins the enrolled PC and phone.
The relay routes encrypted bytes and never stores commands for later execution. Revocation retries
remain pending locally if the relay is unreachable, and revocations persist across relay restarts.

## Optional Google/Firebase hints

The default APK has no Firebase dependency. Build with `-PenableFcm=true` for the explicit addon.
Configure the operator's public Firebase project ID, Android application ID, sender ID and API key
in Android's optional push screen, then opt in and provision the resulting registration token
privately to your trusted sender. No service-account private key belongs in the APK.

`relay/send-fcm-hint.ps1` sends HTTP v1 data-only hints using a short-lived OAuth access token from
your trusted server/ADC. The hint contains a PC ID; Android fetches the authoritative recent events
over the pinned end-to-end channel and deduplicates event IDs. Wire your event source to this
operator adapter. Account provisioning and real FCM delivery remain unverified in this build.

## Development, diagnostics and removal

```powershell
dotnet build LapCont.sln -c Release
.\scripts\start-development.ps1
```

Development mode runs both processes as the current user and creates no SCM service/firewall rule.
It verifies same-user IPC, but cannot prove LocalSystem or SCM session callbacks. Close both owned
development processes before rebuilding their loaded Windows assemblies. Android emulators require
`adb reverse tcp:4433 tcp:4433` and a locally approved QA QR; this does not prove physical LAN.

Diagnostic exports are metadata allowlists excluding keys, tokens, personal event details and media.
Use the Android share summary or `scripts/export-diagnostics.ps1 -OutputFile <path>` for the
development data directory; specify the installed data directory from an elevated session when needed.

Run `uninstall-windows.ps1` as Administrator from the extracted package. Identities/pairings are
preserved by default. `-PurgePairings` explicitly removes the checked product data directory. Remove
companion startup separately in other Windows accounts. Unpair/revoke phones before decommissioning.
