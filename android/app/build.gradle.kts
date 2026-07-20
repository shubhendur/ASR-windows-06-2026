plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.asr.keyboard"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.asr.keyboard"
        minSdk = 26           // Android 8.0+ (NoiseSuppressor/AGC widely available)
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"

        // On-device inference is heavy: 64-bit only keeps the APK small
        ndk { abiFilters += listOf("arm64-v8a") }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}

dependencies {
    // sherpa-onnx Android AAR — run scripts/get-sherpa-aar.ps1 once to fetch
    // it from the sherpa-onnx GitHub releases into app/libs/.
    implementation(files("libs/sherpa-onnx.aar"))

    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    implementation("com.google.android.material:material:1.12.0")

    // tar.bz2 extraction for the Parakeet model archive
    implementation("org.apache.commons:commons-compress:1.26.2")
}
