# Advanced module: genuine phone-approved Windows unlock

Status: preserved design objective, **not implemented or advertised as available**.
The Phase 0 WPF app can request a local lock and observe WTS events. It cannot authenticate
Windows from a phone. The baseline command is `unlock_request`; its future response is
`SIGN_IN_REQUIRED` with “Sign in at your PC,” and verified state stays locked until Windows
reports an actual unlock.

A custom credential provider collects/serializes credentials; authentication packages enforce
their validity. Adding a provider does not turn an arbitrary phone signature into a supported
Windows credential. Keep at least one system sign-in provider available for recovery.
[Microsoft credential-provider guidance](https://learn.microsoft.com/en-us/windows/win32/secauthn/credential-providers-in-windows).

Before implementing this extension, publish a concrete account-specific authentication design:
local, Microsoft, Active Directory and Entra accounts must be treated separately. Identify the
supported credential/authentication integration and any native C++/COM provider dependency,
enrollment authority, protected key/credential storage, offline policy, installer/uninstaller
and owner recovery. A tray app's ordinary desktop UI is not trusted secure-desktop UI.

The future flow must issue a fresh single-use Windows challenge bound to PC identity,
authorized Windows user/session, intended unlock and a short expiration. Android requires fresh
biometric/device authentication before signing that challenge with its protected enrollment
credential. A notification tap alone must not authorize it. The provider must pass valid
credentials through the supported Windows authentication flow and keep the outcome pending
until the matching real Windows unlock event.

Test expired/reused/wrong-PC/wrong-user challenges, revoked phones, missing device authentication,
network loss, service/provider failure, account migration and recovery with the system provider.
Do not use password/keyboard injection, Winlogon bypass or provider disabling. Until that
supported mechanism is established, return `UNLOCK_CAPABILITY_UNAVAILABLE` for an advanced
capability query and keep the baseline request/sign-in behavior. No claim is made that genuine
unlock is impossible in every architecture, or that the baseline has delivered it.
