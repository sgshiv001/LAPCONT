# LapCont — Android — Native bindings and TLS certificate methods
# License: MIT
-keep class * extends com.sun.jna.Library { *; }
-keep interface * extends com.sun.jna.Library { *; }
-keep class com.sun.jna.** { *; }
-dontwarn java.awt.**
# JNA's desktop AWT helper is unreachable on Android. Keep the native Opus ABI method names.
-keep interface com.lapcont.mobile.media.LibOpus { *; }
-keep class com.lapcont.push.** { *; }
