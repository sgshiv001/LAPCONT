// LapCont — Android — Clearly labelled foreground feasibility controls
// License: MIT
package com.lapcont.app
import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.view.Surface
import androidx.compose.foundation.AndroidExternalSurface
import androidx.compose.foundation.AndroidEmbeddedExternalSurface
import androidx.compose.foundation.AndroidExternalSurfaceScope
import androidx.activity.ComponentActivity
import androidx.activity.enableEdgeToEdge
import androidx.activity.compose.setContent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.lapcont.app.media.*
import com.lapcont.app.proximity.AdvertiserProbe
import kotlinx.coroutines.*

/** Foreground prototype Activity. Lifecycle cancellation releases BLE/codecs; it is not a paired-PC controller. */
class MainActivity : ComponentActivity() {
    private var stopForUserLeave: (() -> Unit)? = null

    override fun onUserLeaveHint() {
        super.onUserLeaveHint()
        // Home can precede ON_STOP while a permission callback is still queued.
        stopForUserLeave?.invoke()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val colors = if (androidx.compose.foundation.isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()
            MaterialTheme(colorScheme = colors) {
                var result by remember { mutableStateOf("Choose a probe. Physical-device compatibility is unverified.") }
                var job by remember { mutableStateOf<Job?>(null) }
                var pendingPermission by remember { mutableStateOf<String?>(null) }
                var surface by remember { mutableStateOf<Surface?>(null) }
                val scope = rememberCoroutineScope()
                val advertiser = remember { AdvertiserProbe(this) }
                fun stop() {
                    pendingPermission = null
                    job?.cancel(); advertiser.close(); result = "Probe stopped"
                }
                fun run(block: suspend () -> String) {
                    if (!lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) { stop(); return }
                    job?.cancel(); result = "Probe running…"
                    job = scope.launch {
                        result = try { withContext(Dispatchers.IO) { block() } }
                        catch (e: CancellationException) { "Probe stopped" }
                        catch (e: SecurityException) { "Permission denied: ${e.javaClass.simpleName}" }
                        catch (e: Exception) { "Probe failed: ${e.javaClass.simpleName}: ${e.message}" }
                        catch (e: UnsatisfiedLinkError) { "Native Opus unavailable; run prepare-native.ps1 before building" }
                        finally { job = null }
                    }
                }
                val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { granted ->
                    val requested = pendingPermission == "bluetooth"
                    pendingPermission = null
                    if (requested) {
                        if (granted.values.all { it }) run { advertiser.run() } else result = "Bluetooth permission denied. Other probes remain available."
                    }
                }
                val microphonePermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
                    val requested = pendingPermission == "microphone"
                    pendingPermission = null
                    if (requested) {
                        if (granted) run { MicrophoneProbe.run() } else result = "Microphone permission denied. Other probes remain available."
                    }
                }
                DisposableEffect(Unit) {
                    // A permission dialog can also issue a leave hint. It owns no active capture.
                    stopForUserLeave = { if (pendingPermission == null) stop() }
                    val listener = LifecycleEventObserver { _, event ->
                        if (event == Lifecycle.Event.ON_STOP || (event == Lifecycle.Event.ON_PAUSE && job != null)) stop()
                    }
                    lifecycle.addObserver(listener)
                    onDispose { stopForUserLeave = null; lifecycle.removeObserver(listener); job?.cancel(); advertiser.close() }
                }
                Surface(Modifier.fillMaxSize()) {
                    Column(Modifier.windowInsetsPadding(WindowInsets.safeDrawing).padding(24.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        Text("LapCont / Phase 0", style = MaterialTheme.typography.headlineMedium)
                        Text("Feasibility probes", style = MaterialTheme.typography.titleLarge)
                        Text("No pairing or remote control yet. Media shown here is a generated codec test pattern. No camera/audio recording.")
                        Button(enabled = job == null && pendingPermission == null, onClick = {
                            if (Build.VERSION.SDK_INT >= 31) {
                                val required = arrayOf(Manifest.permission.BLUETOOTH_ADVERTISE, Manifest.permission.BLUETOOTH_CONNECT)
                                if (required.all { checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }) run { advertiser.run() }
                                else {
                                    pendingPermission = "bluetooth"
                                    permissions.launch(required)
                                }
                            }
                            else run { advertiser.run() }
                        }) { Text("Advertise BLE test beacon (8 seconds)") }
                        Button(enabled = job == null && pendingPermission == null && surface != null, onClick = { run { VideoProbe.run(checkNotNull(surface)) } }) { Text("Probe H.264 encode → decode") }
                        Button(enabled = job == null && pendingPermission == null, onClick = { run { OpusProbe.run() } }) { Text("Probe native Opus with synthetic silence") }
                        Button(enabled = job == null && pendingPermission == null, onClick = {
                            if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) run { MicrophoneProbe.run() }
                            else {
                                pendingPermission = "microphone"
                                microphonePermission.launch(Manifest.permission.RECORD_AUDIO)
                            }
                        }) { Text("Probe microphone / Opus (1.5 seconds)") }
                        OutlinedButton(onClick = { stop() }) { Text("Stop all probes") }
                        Text(result)
                        val initializeSurface: AndroidExternalSurfaceScope.() -> Unit = {
                            onSurface { created, _, _ ->
                                surface = created
                                created.onDestroyed { job?.cancel(); surface = null }
                            }
                        }
                        // API 29 SurfaceView interop hid the Compose controls in actual screenshot QA.
                        // Embedded output still supplies a real MediaCodec Surface, with extra GPU composition.
                        if (Build.VERSION.SDK_INT == 29) {
                            AndroidEmbeddedExternalSurface(Modifier.fillMaxWidth().height(200.dp), onInit = initializeSurface)
                        } else {
                            AndroidExternalSurface(Modifier.fillMaxWidth().height(200.dp), onInit = initializeSurface)
                        }
                    }
                }
            }
        }
    }
}
