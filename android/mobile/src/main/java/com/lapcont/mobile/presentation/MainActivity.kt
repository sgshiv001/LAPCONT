// LapCont — Android — Simple cards, animated navigation and explicit live-action cleanup
// License: MIT
package com.lapcont.mobile.presentation

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.*
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.*
import androidx.compose.animation.core.tween
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.lapcont.mobile.notifications.MonitorService
import dagger.hilt.android.AndroidEntryPoint
import org.json.JSONObject

@AndroidEntryPoint
class MainActivity : ComponentActivity() {
    private lateinit var model: DashboardViewModel
    private var pendingFeatureAction: (() -> Unit)? = null
    private var requestedPc by mutableStateOf<String?>(null)
    private var openRevision by mutableIntStateOf(0)
    private var permissionRevision by mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState); enableEdgeToEdge()
        model = ViewModelProvider(this)[DashboardViewModel::class.java]
        requestedPc = intent.getStringExtra("pc_id")
        setContent { LapContTheme { App(model, requestedPc, openRevision) } }
    }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); requestedPc = intent.getStringExtra("pc_id"); openRevision++ }
    override fun onResume() { super.onResume(); permissionRevision++ }
    override fun onPause() { model.background(); super.onPause() }
    override fun onStop() { pendingFeatureAction = null; super.onStop() }

    @Composable private fun App(vm: DashboardViewModel, openPc: String?, revision: Int) {
        val pcs by vm.pcs.collectAsStateWithLifecycle()
        val states by vm.states.collectAsStateWithLifecycle()
        val message by vm.message.collectAsStateWithLifecycle()
        val error by vm.error.collectAsStateWithLifecycle()
        val pairing by vm.pairing.collectAsStateWithLifecycle()
        val live by vm.live.collectAsStateWithLifecycle()
        val fingerprint by vm.fingerprint.collectAsStateWithLifecycle()
        val preferences = remember { getSharedPreferences("settings", MODE_PRIVATE) }
        var screen by rememberSaveable { mutableStateOf(if (openPc != null) AppScreen.DETAILS else if (preferences.getBoolean("onboarded", false)) AppScreen.PCS else AppScreen.WELCOME) }
        var selected by rememberSaveable { mutableStateOf(openPc) }
        var scan by rememberSaveable { mutableStateOf(false) }
        var manualQr by remember { mutableStateOf("") }
        var video by rememberSaveable { mutableStateOf(true) }
        var audio by rememberSaveable { mutableStateOf(true) }
        var high by rememberSaveable { mutableStateOf(preferences.getBoolean("high_quality", true)) }
        var surfaceReady by remember { mutableStateOf(false) }
        var direction by remember { mutableIntStateOf(1) }
        var removePc by remember { mutableStateOf(false) }
        val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { grants ->
            permissionRevision++
            val action = pendingFeatureAction; pendingFeatureAction = null
            if (grants.values.all { it } && lifecycle.currentState.isAtLeast(androidx.lifecycle.Lifecycle.State.STARTED)) action?.invoke()
            else vm.error.value = "Permission wasn't allowed. You can still use the other controls."
        }
        fun permissions(names: Array<String>, action: () -> Unit) {
            val missing = names.filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
            if (missing.isEmpty()) action() else { pendingFeatureAction = action; permission.launch(missing.toTypedArray()) }
        }
        fun navigate(next: AppScreen) { direction = 1; screen = next; vm.error.value = null }
        fun back() {
            pendingFeatureAction = null; vm.stopTalk(); vm.stopMedia(); vm.cancelPair()
            val next = backDestination(screen, scan)
            scan = false; manualQr = ""; direction = -1; screen = next; vm.error.value = null
            if (next == AppScreen.PCS) selected = null
        }
        // Register once: changing enabled after a tap can leave a brief gap where Android exits.
        BackHandler { if (screen == AppScreen.PCS || screen == AppScreen.WELCOME) finish() else back() }
        LaunchedEffect(openPc, revision) { if (openPc != null) { selected = openPc; navigate(AppScreen.DETAILS) } }
        val pc = pcs.firstOrNull { it.id == selected }
        val state = selected?.let { states[it] }

        Column(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background).windowInsetsPadding(WindowInsets.safeDrawing)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                if (screen != AppScreen.PCS && screen != AppScreen.WELCOME) IconButton(onClick = { back() }, modifier = Modifier.testTag("nav-back").semantics { contentDescription = "Back" }) { Text("‹", fontSize = 34.sp) }
                else Box(Modifier.size(44.dp).clip(RoundedCornerShape(15.dp)).background(MaterialTheme.colorScheme.primary), contentAlignment = Alignment.Center) { Text("L", color = MaterialTheme.colorScheme.onPrimary, fontSize = 24.sp, fontWeight = FontWeight.Bold) }
                Column(Modifier.weight(1f).padding(start = 12.dp)) {
                    Text(if (screen == AppScreen.PCS || screen == AppScreen.WELCOME) "LapCont" else screen.title, style = MaterialTheme.typography.titleLarge, modifier = Modifier.semantics { heading() })
                    Text(if (screen == AppScreen.PCS) "A little closer to your PC" else pc?.name ?: "Your phone. Your PC.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            AnimatedVisibility(error != null) {
                Surface(color = MaterialTheme.colorScheme.errorContainer, shape = RoundedCornerShape(16.dp), modifier = Modifier.padding(horizontal = 20.dp, vertical = 4.dp)) {
                    Row(Modifier.padding(start = 16.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(error ?: "", modifier = Modifier.weight(1f).semantics { liveRegion = LiveRegionMode.Polite }, color = MaterialTheme.colorScheme.onErrorContainer)
                        IconButton(onClick = { vm.error.value = null }, modifier = Modifier.semantics { contentDescription = "Dismiss error" }) { Text("×", fontSize = 22.sp) }
                    }
                }
            }
            AnimatedContent(targetState = screen, modifier = Modifier.weight(1f), transitionSpec = {
                (slideInHorizontally(tween(240)) { it / 8 * direction } + fadeIn(tween(180))) togetherWith
                    (slideOutHorizontally(tween(180)) { -it / 12 * direction } + fadeOut(tween(120)))
            }, label = "screen transition") { page ->
                Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(start = 20.dp, end = 20.dp, top = 8.dp, bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    when (page) {
                        AppScreen.WELCOME -> {
                            Hero("Meet your PC's\nnew companion.", "Simple controls, live view and nearby protection. All in one place.")
                            SectionCard("Three small steps") {
                                Step("1", "Open LapCont on Windows", "Install the companion and keep it running.")
                                Step("2", "Pair with a QR code", "Scan on your phone and approve it on the PC.")
                                Step("3", "Choose what to allow", "Camera, microphone and Bluetooth are always your choice.")
                            }
                            PrimaryAction("Get started") { preferences.edit().putBoolean("onboarded", true).apply(); navigate(AppScreen.PCS) }
                            Hint("Unlocking still requires normal Windows sign-in. Camera and audio are live only.")
                        }
                        AppScreen.PCS -> {
                            Hero("Your PCs,\nwithin reach.", if (pcs.isEmpty()) "Add your first PC to get started." else "${states.values.count { it.online }} of ${pcs.size} connected")
                            PrimaryAction("Add a PC") { navigate(AppScreen.PAIRING) }
                            if (pcs.isEmpty()) SectionCard("Nothing paired yet") { Text("Open LapCont on your Windows PC, choose Pair a phone, then scan its code here."); Hint("Keep both devices on the same Wi-Fi for setup.") }
                            else {
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) { Text("Your PCs", style = MaterialTheme.typography.titleMedium); TextButton(onClick = { vm.repository.connectAll() }) { Text("Connect saved PCs") } }
                                pcs.forEach { item ->
                                    Card(onClick = { selected = item.id; navigate(AppScreen.DETAILS) }, shape = RoundedCornerShape(24.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant), modifier = Modifier.fillMaxWidth()) {
                                        Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically) {
                                            DeviceMark(); Column(Modifier.weight(1f).padding(start = 14.dp)) { Text(item.name, style = MaterialTheme.typography.titleMedium); StatusPill(if(states[item.id]?.online == true) "Connected" else "Offline", states[item.id]?.online == true); Text("Open PC controls", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }; Text("›", fontSize = 28.sp)
                                        }
                                    }
                                }
                                SectionCard("Stay in the loop") {
                                    Text("Receive PC events and keep enabled Bluetooth protection visible.")
                                    PrimaryAction("Start visible monitoring") { permissions(if(Build.VERSION.SDK_INT >= 33) arrayOf(Manifest.permission.POST_NOTIFICATIONS) else emptyArray()) { startForegroundService(Intent(this@MainActivity, MonitorService::class.java)) } }
                                    TextButton(onClick = { startService(Intent(this@MainActivity, MonitorService::class.java).setAction("STOP")) }) { Text("Stop monitoring") }
                                    Hint("Android can pause background connections. Reconnect to get the latest verified events.")
                                }
                            }
                            Hint("Your PC still needs normal Windows sign-in to unlock.")
                        }
                        AppScreen.PAIRING -> {
                            SectionCard("Connect your PC") {
                                Text("On Windows, open LapCont and choose Pair a phone. Scan its code, compare the identity, then approve your phone on the PC.")
                                PrimaryAction("Scan QR code", enabled = !pairing) { permissions(arrayOf(Manifest.permission.CAMERA)) { scan = true } }
                                if (scan && !pairing) { QrScanner(Modifier.fillMaxWidth().height(280.dp).clip(RoundedCornerShape(20.dp))) { raw -> scan = false; manualQr = raw; vm.pair(raw) }; TextButton(onClick = { scan = false }) { Text("Close scanner") } }
                                if (pairing) { LinearProgressIndicator(Modifier.fillMaxWidth()); Text("Waiting for approval on your PC…"); TextButton(onClick = { vm.cancelPair() }) { Text("Cancel pairing") } }
                            }
                            ExpandableSection("Enter a code instead") {
                                OutlinedTextField(manualQr, { manualQr = it }, label = { Text("Pairing QR text") }, modifier = Modifier.fillMaxWidth(), enabled = !pairing, shape = RoundedCornerShape(14.dp))
                                PrimaryAction("Verify and pair", enabled = manualQr.isNotBlank() && !pairing) { vm.pair(manualQr) }
                            }
                            ExpandableSection("Phone identity") { Text(fingerprint, style = MaterialTheme.typography.bodySmall); Hint("Compare this identity when approving your phone. Pairing codes expire after 60 seconds.") }
                        }
                        AppScreen.DETAILS -> if (pc != null) {
                            val session = state?.verified?.optJSONArray("sessions")?.optJSONObject(0)
                            val grants = state?.verified?.optInt("grants", pc.grants) ?: pc.grants
                            val available = state?.online == true && session?.optBoolean("agent_available") == true
                            SectionCard(pc.name) {
                                StatusPill(if(state?.online == true) "Connected" else "Offline · reconnect to update", state?.online == true)
                                DetailRow("Windows", (session?.optString("lock_state") ?: "unknown").replaceFirstChar { it.uppercase() } + if(state?.online != true) " · last known" else "")
                                DetailRow("Connection", state?.route?.ifBlank { "Not connected" } ?: "Not connected")
                                PrimaryAction(if(state?.online == true) "Refresh status" else "Connect / retry") { vm.repository.connect(pc) }
                            }
                            SectionCard("Quick controls") {
                                PrimaryAction("Lock PC", enabled = available && grants and 1 != 0) { vm.sessionAction(pc.id, "lock") }
                                OutlinedButton(onClick = { vm.sessionAction(pc.id, "unlock_request") }, enabled = available && grants and 1 != 0, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = RoundedCornerShape(16.dp)) { Text("Request unlock") }
                                Hint("Sign in normally at your PC. Your phone never replaces Windows authentication.")
                                if (!available) Hint("Open the PC companion and connect your phone to enable the controls.")
                            }
                            SectionCard("Live view") {
                                ToggleLine("PC camera", video) { video = it }; ToggleLine("PC microphone", audio) { audio = it }
                                ToggleLine("Higher quality · 720p", high) { high = it; preferences.edit().putBoolean("high_quality", it).apply() }
                                val selectedTracksAllowed = (!video || grants and 4 != 0) && (!audio || grants and 8 != 0)
                                PrimaryAction("Open Live View", enabled = available && (video || audio) && selectedTracksAllowed) { navigate(AppScreen.LIVE) }
                                if (!selectedTracksAllowed) Hint("Allow the selected camera and microphone tracks in your PC companion first.")
                                Hint("Live only. No recording or media export.")
                            }
                            FeatureLink("Nearby protection", "Bluetooth calibration and auto-lock status") { navigate(AppScreen.PROXIMITY) }
                            FeatureLink("Connection settings", "Wi-Fi address, notifications and optional relay") { navigate(AppScreen.SETTINGS) }
                            ExpandableSection("Permissions and recent activity") {
                                Text(listOf("Lock & request unlock", "Bluetooth", "Camera", "Microphone", "Talk-back").filterIndexed { index, _ -> grants and (1 shl index) != 0 }.joinToString(" · ").ifBlank { "No permissions yet. Approve them on your PC." })
                                session?.let { DetailRow("Live media", it.optString("media_state", "unknown")); DetailRow("Talk-back", it.optString("talk_state", "unknown")) }
                                state?.verified?.optJSONArray("events")?.let { events -> if (events.length() > 0) Text("Last event: ${events.getJSONObject(events.length() - 1).optString("event")}", style = MaterialTheme.typography.bodySmall) }
                            }
                        } else LoadingPc()
                        AppScreen.LIVE -> if (pc != null) {
                            DisposableEffect(pc.id) { onDispose { vm.stopTalk(); vm.stopMedia(); vm.surface(null); surfaceReady = false } }
                            SectionCard("A live look at your PC") {
                                StatusPill(if(live != null) "Live media active" else "Ready when you are", live != null)
                                if (video) VideoSurface(vm) { surfaceReady = it }
                                val permitted = state?.verified?.optInt("grants") ?: 0
                                PrimaryAction("Start live view", enabled = live == null && state?.online == true && state.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available") == true && (!video || surfaceReady) && (!video || permitted and 4 != 0) && (!audio || permitted and 8 != 0)) { vm.startMedia(pc.id, video, audio, high) }
                                OutlinedButton(onClick = { vm.stopTalk(); vm.stopMedia() }, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = RoundedCornerShape(16.dp)) { Text("Stop live view") }
                                Hint("Going back or leaving the app stops live media.")
                            }
                            SectionCard("Talk to your PC") {
                                val micPermission = remember(permissionRevision) { checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED }
                                if (!micPermission) PrimaryAction("Allow microphone for push-to-talk") { permissions(arrayOf(Manifest.permission.RECORD_AUDIO)) { } }
                                val talkGranted = (state?.verified?.optInt("grants") ?: 0) and 16 != 0
                                val canTalk = talkGranted && micPermission && state?.online == true
                                var pressing by remember { mutableStateOf(false) }
                                Surface(color = if(pressing) MaterialTheme.colorScheme.primary else if(canTalk) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant, contentColor = if(pressing) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onPrimaryContainer, shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth().heightIn(min = 80.dp).semantics { role = Role.Button; if (!canTalk) disabled() }.pointerInput(pc.id, micPermission, talkGranted, state?.online) {
                                    detectTapGestures(onPress = { if(canTalk) { pressing = true; vm.startTalk(pc.id); try { tryAwaitRelease() } finally { pressing = false; vm.stopTalk() } } })
                                }) { Box(Modifier.padding(20.dp), contentAlignment = Alignment.Center) { Text("Hold to talk · release to stop", fontWeight = FontWeight.SemiBold) } }
                                Hint(if(!talkGranted) "Allow Phone talk-back in your PC companion first." else "Your voice plays through the PC speaker. PC microphone playback pauses while you talk.")
                            }
                        } else LoadingPc()
                        AppScreen.PROXIMITY -> if (pc != null) {
                            val p = state?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optJSONObject("proximity")
                            val fresh = state?.online == true
                            val healthy = p?.optBoolean("observer_healthy") == true
                            val paused = p?.optBoolean("paused") != false
                            val samples = (p?.optInt("calibration_samples") ?: 0).coerceIn(0, 8)
                            val calibrated = p?.optString("calibration_status") == "calibrated"
                            val near = p?.optString("state") == "near" && healthy && fresh
                            val median = if(p == null || p.isNull("median_rssi")) "Waiting for signal" else "${p.optInt("median_rssi")} dBm"
                            SectionCard("Nearby protection") {
                                StatusPill(when { !fresh -> "Offline · status is stale"; paused -> "Auto-lock paused at PC"; !healthy -> "PC Bluetooth unavailable"; near -> "Your phone is near"; else -> "Waiting for Bluetooth signal" }, near && !paused)
                                Text("Move away to lock. Come back for a sign-in reminder."); Hint("Returning never unlocks Windows.")
                                PrimaryAction("Enable Bluetooth monitoring") { permissions(if(Build.VERSION.SDK_INT >= 31) arrayOf(Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_ADVERTISE) else emptyArray()) { vm.run { vm.repository.settings(pc, pc.addresses, pc.relayUrl, null, true); startForegroundService(Intent(this@MainActivity, MonitorService::class.java)) } } }
                                TextButton(onClick = { vm.run { vm.repository.settings(pc, pc.addresses, pc.relayUrl, null, false); vm.repository.proximityStart() } }) { Text("Pause this phone's advertising") }
                            }
                            SectionCard("Calibrate beside your PC") {
                                Text(when { calibrated && samples == 8 -> "Calibration ready · 8/8 samples"; calibrated -> "Calibration saved · waiting for a fresh signal"; p?.optString("calibration_status") == "insufficient_data" -> "Not enough signal. Keep your phone closer and retry."; else -> "$samples/8 samples collected" })
                                LinearProgressIndicator(progress = { samples / 8f }, modifier = Modifier.fillMaxWidth())
                                DetailRow("Signal", median)
                                Hint("Keep your phone beside the laptop. Start calibration, then refresh to check the samples.")
                                if (paused) Hint("Unpause automatic locking in the Windows companion to start the Bluetooth observer.")
                                if (!fresh) Hint("Connect to your PC before using calibration controls.")
                                for ((operation, label) in listOf("start" to "Start calibration", "query" to "Query calibration", "cancel" to "Cancel calibration")) {
                                    val action = { vm.run { if(operation == "query") vm.repository.action(pc.id, "status") else vm.repository.action(pc.id, "calibrate", JSONObject().put("windows_session_id", vm.repository.session(pc.id)).put("operation", operation)); Unit } }
                                    if (operation == "start") PrimaryAction(label, enabled = fresh && healthy && !paused, onClick = action)
                                    else TextButton(onClick = action, enabled = fresh && (operation == "query" || healthy)) { Text(label) }
                                }
                            }
                            ExpandableSection("Bluetooth details") {
                                DetailRow("PC observer", if(healthy) "Healthy" else "Unavailable")
                                DetailRow("Away threshold", if(p == null || p.isNull("threshold")) "Unavailable" else "${p.optInt("threshold")} dBm")
                                DetailRow("Weak-signal delay", "${p?.optInt("lock_delay_seconds") ?: 10}s")
                                DetailRow("Missing-signal grace", "${p?.optInt("missing_grace_seconds") ?: 30}s")
                                Text("Recent signal: ${p?.optJSONArray("recent_rssi") ?: "No samples"}", style = MaterialTheme.typography.bodySmall)
                                Hint("Bluetooth signal varies. It isn't a distance measurement.")
                            }
                        } else LoadingPc()
                        AppScreen.SETTINGS -> if (pc != null) {
                            var addresses by rememberSaveable(pc.id) { mutableStateOf(pc.addresses) }
                            var relay by rememberSaveable(pc.id) { mutableStateOf(pc.relayUrl ?: "") }
                            var credential by remember(pc.id) { mutableStateOf("") }
                            SectionCard("Wi-Fi connection") {
                                Text("If your PC's Wi-Fi address changes, update it here. Your pairing stays saved.")
                                OutlinedTextField(addresses, { addresses = it }, label = { Text("PC Wi-Fi address") }, supportingText = { Text("For example 192.168.1.10. Separate multiple addresses with commas.") }, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(14.dp))
                                PrimaryAction("Save and reconnect") { vm.run { vm.repository.settings(pc, addresses.trim(), relay.trim().ifBlank { null }, credential.ifBlank { null }, pc.proximityEnabled); credential = ""; navigate(AppScreen.DETAILS) } }
                            }
                            ExpandableSection("Internet relay · optional") {
                                Hint("Use a relay supplied by your operator to connect beyond your local Wi-Fi.")
                                OutlinedTextField(relay, { relay = it }, label = { Text("Relay URL (wss://)") }, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(14.dp))
                                OutlinedTextField(credential, { credential = it }, label = { Text("Phone relay credential") }, visualTransformation = androidx.compose.ui.text.input.PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(14.dp))
                                Hint("Choose Save and reconnect above to apply these settings.")
                            }
                            SectionCard("Notifications") {
                                Text("Manage LapCont's event notifications on this phone.")
                                OutlinedButton(onClick = { startActivity(Intent(android.provider.Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(android.provider.Settings.EXTRA_APP_PACKAGE, packageName)) }, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp)) { Text("Notification settings") }
                                if(com.lapcont.mobile.BuildConfig.OPTIONAL_PUSH) TextButton(onClick = { startActivity(Intent().setClassName(packageName, "com.lapcont.push.PushSettingsActivity")) }) { Text("Configure optional push") }
                            }
                            ExpandableSection("Help and diagnostics") {
                                Hint("Camera, microphone and locked-session permissions are managed on your PC.")
                                TextButton(onClick = { val report = "LapCont ${com.lapcont.mobile.BuildConfig.VERSION_NAME}\nAndroid API ${Build.VERSION.SDK_INT}\nPaired PCs: ${pcs.size}\nConnected PCs: ${states.values.count { it.online }}\nMedia active: ${live != null}\nNo keys, credentials, personal event details, or media included."; startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, report), "Share diagnostic summary")) }) { Text("Share diagnostic summary") }
                            }
                            TextButton(onClick = { removePc = true }, colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error)) { Text("Remove this PC") }
                        } else LoadingPc()
                    }
                    if (message != "Ready") Hint(message)
                }
            }
        }
        if (removePc && pc != null) AlertDialog(onDismissRequest = { removePc = false }, title = { Text("Remove ${pc.name}?") }, text = { Text(if(state?.online == true) "This revokes your phone's pairing on the PC. You'll need to pair again." else "This removes the PC from your phone only. Its PC-side authorization remains until you revoke it there.") }, confirmButton = { TextButton(onClick = { removePc = false; vm.run { vm.repository.remove(pc.id, state?.online != true); selected = null; navigate(AppScreen.PCS) } }) { Text("Remove") } }, dismissButton = { TextButton(onClick = { removePc = false }) { Text("Keep PC") } })
    }
}

@Composable private fun Hero(title: String, subtitle: String) {
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(28.dp)).background(Brush.linearGradient(listOf(Color(0xFF252D49), Color(0xFF44367A)))).padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) { Text(title, style = MaterialTheme.typography.headlineLarge, color = Color.White); Text(subtitle, color = Color(0xFFE0DEFA), style = MaterialTheme.typography.bodyLarge) }
}
@Composable private fun SectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Surface(modifier = Modifier.fillMaxWidth(), color = MaterialTheme.colorScheme.surface, shape = RoundedCornerShape(24.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)) { Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) { Text(title, style = MaterialTheme.typography.titleMedium, modifier = Modifier.semantics { heading() }); content() } }
}
@Composable private fun PrimaryAction(label: String, enabled: Boolean = true, onClick: () -> Unit) { Button(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), shape = RoundedCornerShape(16.dp), contentPadding = PaddingValues(horizontal = 18.dp, vertical = 14.dp)) { Text(label) } }
@Composable private fun Hint(text: String) { Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
@Composable private fun DetailRow(label: String, value: String) { Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) { Text(label, Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium); Text(value, Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium) } }
@Composable private fun ToggleLine(label: String, checked: Boolean, change: (Boolean) -> Unit) { Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).toggleable(value = checked, role = Role.Switch, onValueChange = change), verticalAlignment = Alignment.CenterVertically) { Text(label, Modifier.weight(1f)); Switch(checked, onCheckedChange = null) } }
@Composable private fun StatusPill(label: String, positive: Boolean) { Surface(color = if(positive) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant, shape = RoundedCornerShape(50)) { Text(label, Modifier.padding(horizontal = 12.dp, vertical = 7.dp), style = MaterialTheme.typography.labelMedium, color = if(positive) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurfaceVariant) } }
@Composable private fun DeviceMark() { Box(Modifier.size(48.dp).clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.primaryContainer), contentAlignment = Alignment.Center) { Text("PC", color = MaterialTheme.colorScheme.onPrimaryContainer, fontWeight = FontWeight.Bold) } }
@Composable private fun Step(number: String, title: String, detail: String) { Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) { Box(Modifier.size(32.dp).clip(RoundedCornerShape(10.dp)).background(MaterialTheme.colorScheme.primaryContainer), contentAlignment = Alignment.Center) { Text(number, color = MaterialTheme.colorScheme.onPrimaryContainer, fontWeight = FontWeight.Bold) }; Column(Modifier.weight(1f)) { Text(title, fontWeight = FontWeight.SemiBold); Hint(detail) } } }
@Composable private fun FeatureLink(title: String, detail: String, action: () -> Unit) { Card(onClick = action, shape = RoundedCornerShape(24.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant), modifier = Modifier.fillMaxWidth()) { Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text(title, style = MaterialTheme.typography.titleMedium); Hint(detail) }; Text("›", fontSize = 28.sp) } } }
@Composable private fun ExpandableSection(title: String, content: @Composable ColumnScope.() -> Unit) { var expanded by rememberSaveable { mutableStateOf(false) }; Surface(shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surface, border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant), modifier = Modifier.fillMaxWidth()) { Column { TextButton(onClick = { expanded = !expanded }, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) { Text(title, Modifier.weight(1f)); Text(if(expanded) "−" else "+", fontSize = 22.sp) }; AnimatedVisibility(expanded) { Column(Modifier.padding(start = 20.dp, end = 20.dp, bottom = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp), content = content) } } } }
@Composable private fun LoadingPc() { SectionCard("Loading your PC…") { CircularProgressIndicator(); Hint("Use Back to return to your saved PCs.") } }

@OptIn(ExperimentalFoundationApi::class)
@Composable private fun VideoSurface(vm: DashboardViewModel, ready: (Boolean) -> Unit) {
    val modifier = Modifier.fillMaxWidth().height(240.dp).clip(RoundedCornerShape(16.dp)).background(Color(0xFF11141D))
    if(Build.VERSION.SDK_INT == 29) AndroidEmbeddedExternalSurface(modifier) { onSurface { surface, _, _ -> vm.surface(surface); ready(true); surface.onDestroyed { ready(false); vm.surface(null) } } }
    else AndroidExternalSurface(modifier) { onSurface { surface, _, _ -> vm.surface(surface); ready(true); surface.onDestroyed { ready(false); vm.surface(null) } } }
}
