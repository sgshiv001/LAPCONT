# Third-party components in LapCont 0.1.0

LapCont-authored source is MIT; that license does not replace dependency licenses.
The copied upstream texts in [licenses](licenses/) retain their own copyright and license.
Do not add LapCont MIT headers to these files or the generated Gradle wrapper.

| Component | Pinned version | License / provenance |
|---|---|---|
| Vortice.MediaFoundation / DirectX | 3.8.3 | MIT; [upstream](https://github.com/amerkoleci/Vortice.Windows), [text](licenses/Vortice-MIT.txt) |
| SharpGen.Runtime | 2.4.2-beta, transitive | MIT; [upstream](https://github.com/SharpGenTools/SharpGenTools); review this pre-release dependency before production |
| QRCoder | 1.6.0 | MIT; [upstream](https://github.com/codebude/QRCoder), [text](licenses/QRCoder-MIT.txt) |
| ZXing core | 3.5.3 | Apache-2.0; [upstream](https://github.com/zxing/zxing), [notice](licenses/ZXing-Apache-2.0.txt) |
| Dagger / Hilt | 2.60.1 | Apache-2.0; [upstream](https://github.com/google/dagger) |
| Room / CameraX | 2.8.4 / 1.5.2 | Apache-2.0; AndroidX notices and lockfiles |
| Kotlin Symbol Processing | 2.3.9 | Apache-2.0; [upstream](https://github.com/google/ksp); build-time |
| coder/websocket | 1.8.14 | ISC; [upstream](https://github.com/coder/websocket), [text](licenses/coder-websocket-ISC.txt) |
| Go runtime / standard library | toolchain 1.27.1 | BSD-style; [text](licenses/Go-BSD.txt); embedded in relay binaries |
| Firebase Messaging | 25.1.3, optional build only | Apache-2.0 and bundled dependency notices; [upstream](https://github.com/firebase/firebase-android-sdk). Google services require separate operator setup. |
| NAudio and its modules | 2.2.1 | MIT; [upstream](https://github.com/naudio/NAudio), [text](licenses/NAudio-MIT.txt) |
| OpusSharp managed binding | 1.6.8 | MIT; [upstream](https://github.com/AvionBlock/OpusSharp), [text](licenses/OpusSharp-MIT.txt) |
| OpusSharp.Natives package | 1.6.1.4 | Packages libopus 1.6.1; BSD-style Opus terms, [COPYING](licenses/Opus-COPYING.txt) |
| libopus codec | 1.6.1, observed at runtime | [Xiph upstream](https://opus-codec.org/); preserve COPYING, including patent-license references; no claim that LapCont MIT covers native codec code |
| JNA Android JNI bridge | 5.17.0 | Apache-2.0 OR LGPL-2.1-or-later; this distribution elects Apache-2.0, [upstream choice](licenses/JNA-LICENSE.txt), [Apache text](licenses/Apache-2.0.txt) |
| libffi embedded in JNA | Version supplied by JNA 5.17.0 | MIT-style; [upstream JNA subtree text](licenses/JNA-libffi-LICENSE.txt) |
| OkHttp / Okio | 4.12.0 / transitive locked version | Apache-2.0; [upstream](https://github.com/square/okhttp) |
| Kotlin / kotlinx.coroutines | 2.2.10 plugins / 1.10.2 | Apache-2.0; [Kotlin](https://github.com/JetBrains/kotlin), [coroutines](https://github.com/Kotlin/kotlinx.coroutines) |
| AndroidX / Jetpack Compose | See Gradle lockfiles and Compose BOM 2026.02.01 | Apache-2.0; [AndroidX source](https://android.googlesource.com/platform/frameworks/support/) |
| Gradle wrapper | 9.6.0 | Apache-2.0; generated third-party scripts/JAR retain upstream notices |
| Android Gradle plugin | 9.4.1 | Apache-2.0; build-time dependency |
| .NET runtime / Microsoft packages | SDK 8.0.425, runtime 8.0.31; package lockfiles | MIT and package-specific notices; [runtime](https://github.com/dotnet/runtime); Windows system APIs are supplied by the OS |
| xUnit / Microsoft.NET.Test.Sdk / coverlet | 2.9.2 / 17.11.1 / 6.0.2 | Test-only; Apache-2.0 / MIT / MIT, respectively; retain package notices |
| JUnit | 4.13.2 | EPL-1.0; test-only, [upstream](https://github.com/junit-team/junit4) |
| org.json JVM fixture parser | 20240303 | JSON license; [upstream](https://github.com/stleary/JSON-java); excluded from the Android app, which uses the OS implementation |

NuGet `packages.lock.json` and Gradle `gradle.lockfile` files record resolved transitive
versions. They are dependency inventories, not substitutes for upstream notices. Keep
package-embedded notices in any redistributed binaries; do not strip them to reduce size.

`prepare-native.ps1` verifies SHA-256 of the exact OpusSharp.Natives package before copying
its arm64/x86_64 Android libraries, and copies this notice plus upstream license texts into
both Android apps' shared `assets/notices`. Include this document and `licenses/` in Windows
and relay distributions. The default product APK excludes Firebase; enabling `-PenableFcm=true`
adds Firebase and its locked transitive dependencies. No sender private credentials are bundled.
No FFmpeg or ExoPlayer is used. License files copied from upstream retain their original text;
version-specific package notices and transitive inventories still accompany redistributed binaries.
