// LapCont — Optional push — Operator public project configuration and explicit user consent
// License: MIT
package com.lapcont.push
import android.app.Activity
import android.os.Bundle
import android.widget.*
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.messaging.FirebaseMessaging

/** Native opt-in screen present only in the optional APK. No service-account private credential is accepted. */
class PushSettingsActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val layout=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(24,24,24,24) }
        setContentView(ScrollView(this).apply { addView(layout) })
        fun label(text:String) { layout.addView(TextView(this).apply { this.text=text }) }
        label("Optional Google/Firebase push. Hints trigger a secure PC event refresh; delivery is not guaranteed. Self-hosted monitoring still works.")
        val prefs=getSharedPreferences("push-mode",MODE_PRIVATE)
        val keys=listOf("project_id","application_id","sender_id","api_key")
        val fields=keys.map { key -> EditText(this).apply { hint=key.replace('_',' '); setText(prefs.getString(key,"")); layout.addView(this) } }
        val status=TextView(this).apply { setTextIsSelectable(true) }; layout.addView(status)
        layout.addView(Button(this).apply { text="Enable optional push"; setOnClickListener {
            try {
                val values=fields.map { it.text.toString().trim() }; require(values.all { it.length in 1..200 })
                val options=FirebaseOptions.Builder().setProjectId(values[0]).setApplicationId(values[1]).setGcmSenderId(values[2]).setApiKey(values[3]).build()
                if (FirebaseApp.getApps(this@PushSettingsActivity).isEmpty()) FirebaseApp.initializeApp(this@PushSettingsActivity,options)
                val editor=prefs.edit().putBoolean("enabled",true); keys.zip(values).forEach { (k,v) -> editor.putString(k,v) }; editor.apply()
                FirebaseMessaging.getInstance().isAutoInitEnabled=true
                FirebaseMessaging.getInstance().token.addOnCompleteListener { result -> status.text=if(result.isSuccessful) "Give this registration token privately to your operator:\n${result.result}" else "Registration unavailable. Check project setup and Google Play services." }
            } catch (_:Exception) { status.text="Invalid public project setup. Use the operator instructions; do not enter a service-account private key." }
        } })
        layout.addView(Button(this).apply { text="Disable optional push"; setOnClickListener { prefs.edit().putBoolean("enabled",false).apply(); if(FirebaseApp.getApps(this@PushSettingsActivity).isNotEmpty()) { FirebaseMessaging.getInstance().isAutoInitEnabled=false; FirebaseMessaging.getInstance().deleteToken() }; status.text="Optional push disabled" } })
    }
}
