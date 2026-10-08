// LapCont — Android — Foreground Phase 0 probe app
// License: MIT
plugins { id("com.android.application"); id("org.jetbrains.kotlin.plugin.compose") }
android {
    namespace = "com.lapcont.app"
    compileSdk = 37
    defaultConfig {
        applicationId = "com.lapcont.app.phase0"; minSdk = 29; targetSdk = 37
        versionCode = 1; versionName = "0.0.1-probe"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        // Ship only ABIs with the selected libopus asset; x86_64 is supplementary emulator QA.
        ndk { abiFilters += setOf("arm64-v8a", "x86_64") }
    }
    buildFeatures { compose = true }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    buildTypes { release { isMinifyEnabled = false } }
    packaging { jniLibs.useLegacyPackaging = false }
}
dependencies {
    implementation(project(":transport")) { exclude(group = "org.json", module = "json") }
    implementation(platform("androidx.compose:compose-bom:2026.02.01"))
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("net.java.dev.jna:jna:5.17.0@aar")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.7.0")
    androidTestImplementation(platform("androidx.compose:compose-bom:2026.02.01"))
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
