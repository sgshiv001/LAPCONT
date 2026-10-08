// LapCont — Optional push — Google delivery is a hint; PC TLS events remain authoritative
// License: MIT
package com.lapcont.push
import android.content.Intent
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage

/** No notification payload is trusted. The application fetches events using its paired end-to-end channel. */
class HintService : FirebaseMessagingService() {
    override fun onMessageReceived(message: RemoteMessage) {
        if (!getSharedPreferences("push-mode", MODE_PRIVATE).getBoolean("enabled",false)) return
        val pc = message.data["pc_id"] ?: return
        if (!pc.matches(Regex("[a-f0-9]{32}"))) return
        sendBroadcast(Intent("com.lapcont.VERIFY_EVENT_HINT").setPackage(packageName).putExtra("pc_id",pc))
    }
    override fun onNewToken(token: String) { /* Operator retrieves the current token from the local settings screen; never log it. */ }
}
