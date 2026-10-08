// LapCont — Android — Verified event notifications and visible monitoring lifecycle
// License: MIT
package com.lapcont.mobile.notifications
import android.Manifest
import android.app.*
import android.content.*
import android.content.pm.PackageManager
import android.os.Build
import android.os.IBinder
import com.lapcont.mobile.data.*
import com.lapcont.mobile.presentation.MainActivity
import com.lapcont.mobile.R
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.first
import org.json.JSONObject
import javax.inject.Inject

/** Notifications derive exclusively from authenticated PC events; taps open the app and never attempt Windows authentication. */
class Notifications(private val context: Context) {
    private val manager = context.getSystemService(NotificationManager::class.java)
    init { manager.createNotificationChannel(NotificationChannel("pc-events", "PC security events", NotificationManager.IMPORTANCE_HIGH)); manager.createNotificationChannel(NotificationChannel("monitoring", "Active monitoring", NotificationManager.IMPORTANCE_LOW)) }
    fun open(pcId: String? = null): PendingIntent = PendingIntent.getActivity(context, pcId?.hashCode() ?: 0,
        Intent(context, MainActivity::class.java).putExtra("pc_id", pcId).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    fun event(pc: PairedPc, event: JSONObject) {
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return
        val reason = event.getString("event")
        val detail = when (reason) { "SessionLock" -> "Your PC is locked"; "SessionUnlock" -> "Your PC was unlocked by ${event.optString("verified_user").takeIf { it.isNotBlank() && it != "null" } ?: "a Windows user"}"; "SessionLogoff" -> "The Windows session signed out"; "PhoneReturned" -> "You're near your PC. Sign in at your PC."; else -> return }
        manager.notify(event.getString("event_id").hashCode(), Notification.Builder(context, "pc-events").setSmallIcon(R.drawable.ic_lapcont).setContentTitle(pc.name).setContentText(detail).setContentIntent(open(pc.id)).setAutoCancel(true).build())
    }
    fun monitoring(): Notification = Notification.Builder(context, "monitoring").setSmallIcon(R.drawable.ic_lapcont).setContentTitle("LapCont monitoring active")
        .setContentText("Authenticated PC events and enabled proximity. Capture stops when the app leaves the foreground.").setContentIntent(open()).setOngoing(true)
        .addAction(Notification.Action.Builder(null, "Stop monitoring", PendingIntent.getService(context, 1, Intent(context, MonitorService::class.java).setAction("STOP"), PendingIntent.FLAG_IMMUTABLE)).build()).build()
}

/** Explicitly started foreground service; sticky resurrection never restarts media or proximity. Stop releases all connections. */
@AndroidEntryPoint
class MonitorService : Service() {
    @Inject lateinit var repository: PcRepository
    private var startup: kotlinx.coroutines.Job? = null
    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == "STOP") { repository.scope.launch { repository.monitoringStop() }; stopForeground(STOP_FOREGROUND_REMOVE); stopSelf(); return START_NOT_STICKY }
        startForeground(7, Notifications(this).monitoring()); startup?.cancel(); startup=repository.scope.launch { repository.pcs.first { it.isNotEmpty() }; repository.connectAll(); repository.proximityStart() }; return START_NOT_STICKY
    }
    override fun onDestroy() { startup?.cancel(); repository.scope.launch { repository.monitoringStop() }; super.onDestroy() }
}
