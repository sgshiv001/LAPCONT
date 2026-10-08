// LapCont — Android — Baseline product, separate from retained Phase 0 probes
// License: MIT
plugins { id("com.android.application"); id("org.jetbrains.kotlin.plugin.compose"); id("com.google.devtools.ksp"); id("com.google.dagger.hilt.android") }
val optionalPush = providers.gradleProperty("enableFcm").map(String::toBoolean).getOrElse(false)
dependencyLocking { lockFile.set(layout.projectDirectory.file(if (optionalPush) "gradle-fcm.lockfile" else "gradle.lockfile")) }
android {
    namespace = "com.lapcont.mobile"; compileSdk = 37
    defaultConfig {
        applicationId = "com.lapcont.app"; minSdk = 29; targetSdk = 37; versionCode = 1; versionName = "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField("boolean", "OPTIONAL_PUSH", optionalPush.toString())
        ndk { abiFilters += setOf("arm64-v8a", "x86_64") }
    }
    buildFeatures { compose = true; buildConfig = true }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    buildTypes { release { isMinifyEnabled = true; proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro") } }
    sourceSets["main"].jniLibs.srcDir("../app/src/main/jniLibs")
    sourceSets["main"].assets.srcDir("../app/src/main/assets")
    packaging { jniLibs.useLegacyPackaging = false }
}
ksp { arg("room.schemaLocation", "$projectDir/schemas") }
dependencies {
    if (optionalPush) implementation(project(":push"))
    implementation(project(":transport")) { exclude(group = "org.json", module = "json") }
    implementation(platform("androidx.compose:compose-bom:2026.02.01"))
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("com.google.dagger:hilt-android:2.60.1"); ksp("com.google.dagger:hilt-compiler:2.60.1")
    implementation("androidx.room:room-runtime:2.8.4"); implementation("androidx.room:room-ktx:2.8.4"); ksp("androidx.room:room-compiler:2.8.4")
    implementation("androidx.camera:camera-camera2:1.5.2"); implementation("androidx.camera:camera-lifecycle:1.5.2"); implementation("androidx.camera:camera-view:1.5.2")
    implementation("com.google.zxing:core:3.5.3")
    implementation("net.java.dev.jna:jna:5.17.0@aar")
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test.ext:junit:1.3.0"); androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test:rules:1.7.0")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.7.0")
    androidTestImplementation(platform("androidx.compose:compose-bom:2026.02.01")); androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
