// Standalone Java QA driver: no dependency on the product's obfuscated Kotlin classes.
plugins { id("com.android.application") }
android {
    namespace="com.lapcont.qa";compileSdk=37
    defaultConfig {applicationId="com.lapcont.qa";minSdk=29;targetSdk=37;versionCode=1;versionName="0.1.0"}
    compileOptions {sourceCompatibility=JavaVersion.VERSION_17;targetCompatibility=JavaVersion.VERSION_17}
}
