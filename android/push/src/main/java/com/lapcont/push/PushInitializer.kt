// LapCont — Optional push — Restore public project setup only after previous explicit opt-in
// License: MIT
package com.lapcont.push
import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.messaging.FirebaseMessaging
class PushInitializer : ContentProvider() {
    override fun onCreate():Boolean {
        val ctx=context ?: return false; val p=ctx.getSharedPreferences("push-mode",0)
        if(!p.getBoolean("enabled",false)) return true
        try {
            if(FirebaseApp.getApps(ctx).isEmpty()) FirebaseApp.initializeApp(ctx,FirebaseOptions.Builder().setProjectId(p.getString("project_id","")!!).setApplicationId(p.getString("application_id","")!!).setGcmSenderId(p.getString("sender_id","")!!).setApiKey(p.getString("api_key","")!!).build())
            FirebaseMessaging.getInstance().isAutoInitEnabled=true
        } catch(_:Exception) { android.util.Log.w("LapCont","Optional push configuration unavailable; self-hosted mode remains available") }
        return true
    }
    override fun query(uri:Uri,projection:Array<out String>?,selection:String?,selectionArgs:Array<out String>?,sortOrder:String?):Cursor?=null
    override fun getType(uri:Uri):String?=null
    override fun insert(uri:Uri,values:ContentValues?):Uri?=null
    override fun delete(uri:Uri,selection:String?,selectionArgs:Array<out String>?):Int=0
    override fun update(uri:Uri,values:ContentValues?,selection:String?,selectionArgs:Array<out String>?):Int=0
}
