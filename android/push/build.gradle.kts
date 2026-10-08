// LapCont — Optional push — Excluded from the default self-hosted APK
// License: MIT
plugins { id("com.android.library") }
// AGP lint resolves the legacy common metadata variant differently from the JVM runtime graph.
// Pin that metadata strictly, while locking the actual runtime and all other dependencies.
dependencyLocking { ignoredDependencies.add("org.jetbrains.kotlin:kotlin-stdlib-common") }
android { namespace = "com.lapcont.push"; compileSdk = 37; defaultConfig { minSdk = 29 }; compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 } }
dependencies {
    implementation("com.google.firebase:firebase-messaging:25.1.3")
    implementation("org.jetbrains.kotlin:kotlin-stdlib:2.3.21")
    constraints { implementation("org.jetbrains.kotlin:kotlin-stdlib-common:2.3.21") { version { strictly("2.3.21") } } }
}
