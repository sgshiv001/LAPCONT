// LapCont — Tests — Real Keystore enrollment, scoped status and protected command rejection
// License: MIT
package com.lapcont.mobile
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lapcont.mobile.data.*
import com.lapcont.transport.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import javax.net.ssl.SSLContext

/** Finite integration test using an explicitly provided fresh PC QR and real PC local approval. No exportable phone fixture. */
@RunWith(AndroidJUnit4::class)
class ProductTests {
    private fun repository(activity:androidx.test.core.app.ActivityScenario<com.lapcont.mobile.presentation.MainActivity>):PcRepository {
        lateinit var result:PcRepository
        activity.onActivity {result=androidx.lifecycle.ViewModelProvider(it)[com.lapcont.mobile.presentation.DashboardViewModel::class.java].repository}
        return result
    }
    /** Real product repository: repeated refresh and monitoring startup must not disrupt a live lease. */
    @Test fun refreshAndMonitoringReuseConnection() = runBlocking<Unit> {
        Assume.assumeTrue("Explicit physical connection regression check",InstrumentationRegistry.getArguments().getString("connectionQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val repo=repository(activity);val pc=try {withTimeout(10000){repo.pcs.first {it.isNotEmpty()}}.first().let(::qaRoute)}catch(e:Exception){activity.close();throw e}
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO);val offline=java.util.concurrent.atomic.AtomicInteger();val recording=java.util.concurrent.atomic.AtomicBoolean()
        val watch=owner.launch {repo.states.collect {if(recording.get() && it[pc.id]?.online!=true)offline.incrementAndGet()}}
        try {
            repo.connect(pc);withTimeout(20000){repo.states.first {it[pc.id]?.online==true}}
            assertEquals("unlocked",repo.states.value[pc.id]!!.verified!!.getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            repo.startMedia(pc.id,false,true,false);recording.set(true)
            repeat(4){repo.connect(pc);repo.connectAll();delay(1000);assertEquals(pc.id,repo.livePc.value);assertEquals("active",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))}
            repeat(2){activity.onActivity {it.startForegroundService(android.content.Intent(it,com.lapcont.mobile.notifications.MonitorService::class.java))};delay(1000)}
            delay(5000);assertEquals(0,offline.get());assertEquals(pc.id,repo.livePc.value)
            val active=repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0)
            assertEquals("active",active.getString("media_state"));recording.set(false);repo.stopMedia()
            assertEquals("idle",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            File(context.cacheDir,"product-connection-regression.json").writeText(JSONObject().put("status","passed").put("android_api",android.os.Build.VERSION.SDK_INT).put("route",if(pc.addresses=="127.0.0.1")"USB QA" else "saved LAN").put("refresh_and_connect_all_repetitions",4).put("monitoring_starts",2).put("offline_observations",offline.get()).put("live_lease_preserved",true).put("stop_verified",true).put("enrollment_preserved",true).put("media_recorded",false).toString())
        } finally {recording.set(false);activity.onActivity {it.stopService(android.content.Intent(it,com.lapcont.mobile.notifications.MonitorService::class.java))};repo.monitoringStop();watch.cancel();owner.cancel();activity.close()}
    }
    /** Explicit cable-only QA route; the saved enrollment and pinned identity stay unchanged. */
    private fun qaRoute(pc: PairedPc): PairedPc = if(InstrumentationRegistry.getArguments().getString("qaUsbReverse")=="true") pc.copy(addresses="127.0.0.1",relayUrl=null) else pc
    /** Owner-authorized DHCP repair through the same settings API as the normal phone UI. */
    @Test fun updateSavedLanAddressPreservesEnrollment() = runBlocking<Unit> {
        val address=InstrumentationRegistry.getArguments().getString("qaNewLanAddress")
        Assume.assumeTrue("Explicit saved endpoint repair",address!=null)
        require(PairingQr.ipv4(address!!))
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val repo=repository(activity)
        try {
            val before=withTimeout(10000){repo.pcs.first {it.size==1}}.single()
            repo.settings(before,address,before.relayUrl,null,before.proximityEnabled)
            val after=withTimeout(10000){repo.pcs.first {it.singleOrNull()?.addresses==address}}.single()
            assertEquals(before.copy(addresses=address),after)
            val state=withTimeout(20000){repo.states.first {it[after.id]?.online==true && it[after.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}[after.id]!!.verified!!}
            assertEquals("unlocked",state.getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            File(context.cacheDir,"product-dhcp-repair-summary.json").writeText(JSONObject().put("status","passed").put("route","saved physical LAN").put("saved_endpoint_updated",true).put("enrollment_and_pin_preserved",true).put("authenticated_companion_available",true).put("media_recorded",false).toString())
        } finally {repo.monitoringStop();activity.close()}
    }
    /** Real authenticated LAN bursts and Stop across the separate control/media pipes. */
    @Test fun burstTalkPreservesControlAndStop() = runBlocking<Unit> {
        Assume.assumeTrue("Explicit physical speech-congestion regression",InstrumentationRegistry.getArguments().getString("talkBurstQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().firstOrNull()?.let(::qaRoute);db.close();Assume.assumeTrue(pc!=null)
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO);val ready=CompletableDeferred<JSONObject>();val failures=java.util.concurrent.ConcurrentLinkedQueue<String>()
        val client=PcClient(pc!!,IdentityStore(context),{s,_->ready.complete(s)},{},{},{failures.add(it)})
        val opus=com.sun.jna.Native.load("opus",com.lapcont.mobile.media.LibOpus::class.java)
        val encoder=opus.opus_encoder_create(48000,1,2048,com.sun.jna.ptr.IntByReference())!!
        var sequence=0L
        try {
            client.start(owner);val state=withTimeout(20000){ready.await()};val sid=state.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            assertEquals("unlocked",state.getJSONArray("sessions").getJSONObject(0).getString("lock_state"));failures.clear()
            val id=java.util.UUID.randomUUID().toString().replace("-","");val p=JSONObject().put("windows_session_id",sid).put("stream_id",id)
            val start=client.command("talk_start",p)
            File(context.cacheDir,"product-talk-burst-start.json").writeText(JSONObject().put("code",start.getString("code")).put("disposition",start.getString("disposition")).put("grants",state.getInt("grants")).put("agent_available",state.getJSONArray("sessions").getJSONObject(0).getBoolean("agent_available")).toString())
            File(context.cacheDir,"product-own-phone-id.txt").writeText(IdentityStore(context).phoneId)
            assertNotEquals(start.getString("code"),"failed",start.getString("disposition"))
            val packet=ByteArray(1275);val silence=ShortArray(960)
            suspend fun burst(){repeat(120){val n=opus.opus_encode(encoder,silence,960,packet,packet.size);check(n in 1..1275);client.sendMedia(MediaFrame(3,0,Frames.unhex(id),sequence,sequence++*20000,packet.copyOf(n)))}}
            repeat(3){burst();delay(500);assertEquals("active",client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("talk_state"));val renewal=client.command("talk_start",p);assertNotEquals(renewal.getString("code"),"failed",renewal.getString("disposition"))}
            // Send another burst immediately before Stop, so Stop races queued speech.
            burst();assertEquals("completed",client.command("talk_stop",JSONObject().put("stream_id",id)).getString("disposition"));delay(1000)
            val stopped=client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0)
            assertEquals("idle",stopped.getString("talk_state"));assertTrue(stopped.getBoolean("agent_available"));assertTrue(failures.joinToString(),failures.isEmpty())
            File(context.cacheDir,"product-talk-burst-summary.json").writeText(JSONObject().put("status","passed").put("route",if(pc.addresses=="127.0.0.1")"USB QA" else "saved LAN").put("synthetic_silence_packets",sequence).put("burst_packets",120).put("bursts",4).put("control_connected_after_stop",true).put("companion_available_after_stop",true).put("talk_idle_after_stop",true).put("media_recorded",false).toString())
        } finally {opus.opus_encoder_destroy(encoder);client.close();owner.cancel()}
    }
    /** Host coordinates only the actual installed LapCont SCM service, never Windows sign-in. */
    @Test fun installedServiceRecoveryNeverReplaysMedia() = runBlocking<Unit> {
        Assume.assumeTrue("Requires coordinated installed-service recovery",InstrumentationRegistry.getArguments().getString("serviceRecoveryQa")=="true")
        val kind=InstrumentationRegistry.getArguments().getString("recoveryKind")!!;require(kind in listOf("restart","crash"))
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val marker=File(context.cacheDir,"product-service-$kind-ready.txt");marker.delete()
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val repo=repository(activity);val pc=try {withTimeout(10000){repo.pcs.first {it.isNotEmpty()}}.first().let(::qaRoute)}catch(e:Exception){activity.close();throw e}
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO);val check=java.util.concurrent.atomic.AtomicBoolean();val offline=java.util.concurrent.atomic.AtomicBoolean()
        val watch=owner.launch {repo.states.collect {if(check.get() && it[pc.id]?.online==false)offline.set(true)}}
        try {
            repo.connect(pc);val initial=withTimeout(20000){repo.states.first {it[pc.id]?.online==true && it[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}[pc.id]!!.verified!!}
            val sid=initial.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            repo.startMedia(pc.id,false,true,false)
            assertEquals("active",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            check.set(true);marker.writeText("Live capture ready for owner-authorized installed-service $kind")
            val returned=withTimeout(65000){
                while(!offline.get())delay(100)
                repo.states.first {s->s[pc.id]?.online==true && s[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}[pc.id]!!.verified!!
            }
            val session=returned.getJSONArray("sessions").getJSONObject(0)
            assertEquals(sid,session.getInt("windows_session_id"));assertTrue(session.getString("lock_state") in listOf("unlocked","locked","unknown"))
            assertEquals("idle",session.getString("media_state"));assertEquals("idle",session.getString("talk_state"));assertEquals(initial.getInt("grants"),returned.getInt("grants"))
            withTimeout(5000){while(repo.livePc.value!=null)delay(100)}
            repo.startMedia(pc.id,false,true,false);repo.stopMedia()
            assertEquals("idle",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            File(context.cacheDir,"product-service-$kind-summary.json").writeText(JSONObject().put("status","passed").put("operation",kind).put("route",if(pc.addresses=="127.0.0.1")"USB QA" else "saved LAN").put("offline_observed",true).put("reconnected_authenticated",true).put("windows_session_id",sid).put("reconstructed_lock_state",session.getString("lock_state")).put("grants_unchanged",true).put("no_capture_replay",true).put("fresh_capture_then_stop_passed",true).put("media_recorded",false).toString())
        } finally {check.set(false);marker.delete();repo.monitoringStop();watch.cancel();owner.cancel();activity.close()}
    }
    @Test fun companionRecoveryStopsLocalMedia() = runBlocking<Unit> {
        Assume.assumeTrue("Requires coordinated ordinary-user companion recovery",InstrumentationRegistry.getArguments().getString("companionRecoveryQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext;val marker=File(context.cacheDir,"product-companion-recovery-ready.txt");marker.delete()
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val repo=repository(activity);val pc=try {withTimeout(10000){repo.pcs.first {it.isNotEmpty()}}.first().let(::qaRoute)}catch(e:Exception){activity.close();throw e}
        try {
            repo.connect(pc);val initial=withTimeout(20000){repo.states.first {it[pc.id]?.online==true && it[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}[pc.id]!!.verified!!}
            repo.startMedia(pc.id,false,true,false);assertEquals("active",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            marker.writeText("Live capture ready for companion restart")
            withTimeout(12000){repo.states.first {it[pc.id]?.online==true && it[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==false};while(repo.livePc.value!=null)delay(100)}
            File(context.cacheDir,"product-companion-recovery-stopped.txt").writeText("Local media stopped while companion unavailable")
            val returned=withTimeout(30000){repo.states.first {it[pc.id]?.online==true && it[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")==true}[pc.id]!!.verified!!}
            assertEquals(initial.getInt("grants"),returned.getInt("grants"));assertEquals("idle",returned.getJSONArray("sessions").getJSONObject(0).getString("media_state"));assertNull(repo.livePc.value)
            repo.startMedia(pc.id,false,true,false);repo.stopMedia()
            assertEquals("idle",repo.action(pc.id,"status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            File(context.cacheDir,"product-companion-recovery-summary.json").writeText(JSONObject().put("status","passed").put("route",if(pc.addresses=="127.0.0.1")"USB QA" else "saved LAN").put("unavailable_observed",true).put("local_media_stopped",true).put("control_connection_retained",true).put("companion_returned",true).put("no_media_replay",true).put("fresh_capture_and_stop_passed",true).put("media_recorded",false).toString())
        } finally {marker.delete();repo.monitoringStop();activity.close()}
    }
    /** Opens Android's ordinary Nearby devices dialog; it does not grant permissions itself. */
    @Test fun physicalBluetoothPermissionPrompt() = runBlocking<Unit> {
        Assume.assumeTrue("Requires coordinated normal Nearby devices approval",InstrumentationRegistry.getArguments().getString("physicalBluetoothQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val permissions=arrayOf(android.Manifest.permission.BLUETOOTH_CONNECT,android.Manifest.permission.BLUETOOTH_ADVERTISE)
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        try {
            if(permissions.any{context.checkSelfPermission(it)!=android.content.pm.PackageManager.PERMISSION_GRANTED}) activity.onActivity {it.requestPermissions(permissions,4097)}
            withTimeout(240000){while(permissions.any{context.checkSelfPermission(it)!=android.content.pm.PackageManager.PERMISSION_GRANTED})delay(200)}
        } finally {activity.close()}
    }
    /** Real desk BLE check. A coordinated host runner restores Pause and key participation in finally. */
    @Test fun physicalBluetoothCalibration() = runBlocking<Unit> {
        Assume.assumeTrue("Requires explicit real-phone BLE QA",InstrumentationRegistry.getArguments().getString("physicalBluetoothQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        for(permission in arrayOf(android.Manifest.permission.BLUETOOTH_CONNECT,android.Manifest.permission.BLUETOOTH_ADVERTISE)) assertEquals(android.content.pm.PackageManager.PERMISSION_GRANTED,context.checkSelfPermission(permission))
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val repo=repository(activity);val storedPcs=try {withTimeout(10000){repo.pcs.first {it.isNotEmpty()}}}catch(e:Exception){activity.close();throw e}
        val pc=storedPcs.first().let(::qaRoute)
        File(context.cacheDir,"product-ble-start.json").writeText(JSONObject().put("package",context.packageName).put("stored_pc_count",storedPcs.size).put("usb_qa",InstrumentationRegistry.getArguments().getString("qaUsbReverse")).put("started_at_utc",java.time.Instant.now().toString()).toString())
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO)
        val advertising=java.util.concurrent.atomic.AtomicReference("not started");val advertisementStarts=java.util.concurrent.atomic.AtomicInteger()
        val advertiser=com.lapcont.mobile.proximity.PhoneAdvertiser(context,IdentityStore(context))
        val offlineCallbacks=java.util.concurrent.atomic.AtomicInteger()
        val observation=owner.launch {repo.states.collect {if(it[pc!!.id]?.online==false)offlineCallbacks.incrementAndGet()}}
        try {
            repo.connect(pc!!);val state=withTimeout(20000){repo.states.first {it[pc.id]?.online==true}[pc.id]!!.verified!!};val session=state.getJSONArray("sessions").getJSONObject(0);val sid=session.getInt("windows_session_id")
            val initialLockState=session.getString("lock_state");assertTrue(initialLockState in listOf("locked","unlocked"))
            suspend fun observed(): JSONObject {
                val status=withTimeout(20000) {
                    var response: JSONObject?=null
                    while(response==null) {try {response=repo.action(pc.id,"status")}catch(_:java.io.IOException){delay(250)}}
                    response
                }
                val current=status.getJSONObject("state").getJSONArray("sessions").getJSONObject(0)
                check(current.getString("lock_state")==initialLockState){"Windows state changed; end desk BLE QA and restore Pause"}
                return current.getJSONObject("proximity")
            }
            val before=session.getJSONObject("proximity");assertFalse("Unpause the scanner for this finite check; restore Pause in the host runner",before.getBoolean("paused"))
            withTimeout(5000){while(!observed().getBoolean("observer_healthy"))delay(100)}
            val startingEpoch=System.currentTimeMillis()/30000
            advertiser.start(owner,listOf(pc.copy(proximityEnabled=true))){advertising.set(it);if(it.startsWith("Proximity advertising active"))advertisementStarts.incrementAndGet()}
            withTimeout(5000){while(!advertising.get().startsWith("Proximity advertising active")){check(!advertising.get().startsWith("Proximity unavailable")){advertising.get()};delay(50)}}
            assertEquals("completed",repo.action(pc.id,"calibrate",JSONObject().put("windows_session_id",sid).put("operation","start")).getString("disposition"))
            var result=observed()
            withTimeout(22000){while(result.getString("calibration_status")!="calibrated" || result.getInt("calibration_samples")!=8){delay(500);result=observed();check(result.getBoolean("observer_healthy"));check(result.getString("calibration_status")!="insufficient_data"){"Insufficient physical BLE samples"}}}
            assertEquals(8,result.getInt("calibration_samples"));assertFalse(result.getBoolean("paused"));assertTrue(result.getJSONArray("recent_rssi").length()>=7)
            File(context.cacheDir,"product-ble-calibration-checkpoint.json").writeText(JSONObject().put("status","calibration passed; rotation check in progress").put("calibration_samples",8).put("median_rssi",result.getInt("median_rssi")).put("threshold",result.getInt("threshold")).put("recent_rssi",result.getJSONArray("recent_rssi")).toString())
            // Cross another rotating-identifier epoch while checking authenticated native observations continue.
            repeat(32){delay(1000);assertTrue(observed().getBoolean("observer_healthy"))};val continued=observed()
            assertTrue(continued.getBoolean("observer_healthy"));assertFalse(continued.getBoolean("paused"));assertEquals("calibrated",continued.getString("calibration_status"));assertFalse(continued.isNull("median_rssi"))
            assertTrue(System.currentTimeMillis()/30000>startingEpoch);assertTrue(advertisementStarts.get()>=4)
            assertEquals("Fresh authenticated observations remain present after the rotation interval","near",continued.getString("state"))
            File(context.cacheDir,"product-ble-summary.json").writeText(JSONObject().put("status","passed").put("android_api",android.os.Build.VERSION.SDK_INT).put("physical_advertiser",true).put("advertisement_start_successes",advertisementStarts.get()).put("observer_healthy",true).put("calibration_samples",8).put("median_rssi",continued.getInt("median_rssi")).put("threshold",continued.getInt("threshold")).put("recent_rssi",continued.getJSONArray("recent_rssi")).put("final_proximity_state",continued.getString("state")).put("rotation_interval_crossed",true).put("control_offline_callbacks",offlineCallbacks.get()).put("initial_windows_lock_state",initialLockState).put("windows_lock_state_unchanged",true).put("auto_lock_paused_during_test",false).put("walk_away_tested",false).toString())
        } finally {advertiser.stop();repo.monitoringStop();observation.cancel();owner.cancel();activity.close()}
    }
    /** Opt-in live-only audible talk check. The operator speaks and confirms the laptop output. */
    @Test fun physicalAudibleTalkBack() = runBlocking<Unit> {
        Assume.assumeTrue("Requires coordinated live speech check",InstrumentationRegistry.getArguments().getString("audibleTalkQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        assertEquals(android.content.pm.PackageManager.PERMISSION_GRANTED,context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO))
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().firstOrNull()?.let(::qaRoute);db.close();Assume.assumeTrue(pc!=null)
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO);val ready=CompletableDeferred<JSONObject>();val failures=java.util.concurrent.ConcurrentLinkedQueue<String>()
        val client=PcClient(pc!!,IdentityStore(context),{s,_->ready.complete(s)},{},{},{})
        val media=com.lapcont.mobile.media.LiveMedia(context,owner){failures.add(it)}
        val id=java.util.UUID.randomUUID().toString().replace("-","")
        try {
            client.start(owner);val state=withTimeout(20000){ready.await()};val sid=state.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            val parameters=JSONObject().put("windows_session_id",sid).put("stream_id",id)
            assertNotEquals("failed",client.command("talk_start",parameters).getString("disposition"));media.startTalk(id){client.sendMedia(it)}
            File(context.cacheDir,"product-talk-qa-ready.txt").writeText("Live phone-to-PC speech test active; no recording")
            repeat(15){delay(2000);check(failures.isEmpty()){failures.joinToString()};assertNotEquals("failed",client.command("talk_start",parameters).getString("disposition"))}
            media.stopTalk();media.awaitStopped();assertEquals("completed",client.command("talk_stop",JSONObject().put("stream_id",id)).getString("disposition"))
            assertTrue(media.statistics().sentTalkPackets>100)
            File(context.cacheDir,"product-talk-summary.json").writeText(JSONObject().put("transport_status","passed").put("duration_target_seconds",30).put("talk_sent_packets",media.statistics().sentTalkPackets).put("pc_talk_state",client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("talk_state")).put("audibility","requires owner confirmation").put("media_recorded",false).toString())
        } finally {media.stopTalk();media.awaitStopped();client.close();owner.cancel();activity.close()}
    }
    @Test fun keystoreIdentityAndSecretLifecycle() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext; val store = IdentityStore(context)
        val manager = store.keyManager(); assertNull(manager.getPrivateKey("lapcont-tls").encoded)
        assertEquals("EC", manager.getPrivateKey("lapcont-tls").algorithm)
        val signed = java.security.Signature.getInstance("SHA256withECDSA").apply { initSign(manager.getPrivateKey("lapcont-tls")); update(byteArrayOf(1,2,3)) }.sign()
        assertTrue(java.security.Signature.getInstance("SHA256withECDSA").apply { initVerify(manager.getCertificateChain("lapcont-tls")[0]); update(byteArrayOf(1,2,3)) }.verify(signed))
        val digest = java.security.MessageDigest.getInstance("SHA-256").digest(byteArrayOf(1,2,3))
        val tlsSignature = java.security.Signature.getInstance("NONEwithECDSA").apply { initSign(manager.getPrivateKey("lapcont-tls")); update(digest) }.sign()
        assertTrue(java.security.Signature.getInstance("NONEwithECDSA").apply { initVerify(manager.getCertificateChain("lapcont-tls")[0]); update(digest) }.verify(tlsSignature))
        assertEquals(64, store.fingerprint().length); val id = "0123456789abcdef0123456789abcdef"; val bytes = byteArrayOf(0, 1, -1)
        store.saveSecret(id, bytes); assertArrayEquals(bytes, store.secret(id)); store.remove(id); assertFalse(File(context.noBackupFilesDir, "$id.secret").exists())
    }
    @Test fun qrRejectsUnknownFieldsAndIdentityMismatch() {
        val q = JSONObject().put("app", "LapCont").put("v", 1).put("pc_id", "a".repeat(32)).put("pc_name", "PC").put("addresses", org.json.JSONArray().put("127.0.0.1")).put("port", 4433).put("relay_url", JSONObject.NULL)
            .put("cert_sha256", "b".repeat(64)).put("identity_fingerprint", "b".repeat(64)).put("pairing_token", "c".repeat(64)).put("expires_in_seconds", 60)
        assertEquals("PC",PairingQr.parse(q.toString()).getString("pc_name")); q.put("shell", "bad"); assertThrows(IllegalArgumentException::class.java) { PairingQr.parse(q.toString()) }; q.remove("shell"); q.put("identity_fingerprint", "d".repeat(64)); assertThrows(IllegalArgumentException::class.java) { PairingQr.parse(q.toString()) }
    }
    @Test fun enrolledControlAndMediaIntegration() = runBlocking<Unit> {
        val instrumentation = InstrumentationRegistry.getInstrumentation(); val qrFile = File(instrumentation.targetContext.cacheDir, "product-qr.json")
        if (!qrFile.exists()) { Assume.assumeTrue("Requires the explicitly provisioned expiring QR and local PC approval",false); return@runBlocking }
        val q = PairingQr.parse(qrFile.readText()); qrFile.delete(); val identity = IdentityStore(instrumentation.targetContext)
        val pc = pair(q, identity); val finished = CompletableDeferred<JSONObject>(); val errors = mutableListOf<String>()
        val db = androidx.room.Room.databaseBuilder(instrumentation.targetContext,PcDatabase::class.java,"pcs.db").build()
        db.pcs().save(pc); db.close()
        val owner = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val client = PcClient(pc,identity,{ state,_ -> finished.complete(state) },{},{},{ errors += it })
        try {
            client.start(owner); val initial = withTimeout(20000) { finished.await() }; assertEquals(pc.id, initial.getString("pc_id")); assertFalse(initial.getBoolean("true_unlock_available")); val sessions = initial.getJSONArray("sessions"); assertTrue(sessions.length()>0)
            val session = sessions.getJSONObject(0).getInt("windows_session_id"); assertTrue(sessions.getJSONObject(0).getBoolean("agent_available"))
            val wrong = client.command("lock",JSONObject().put("windows_session_id", session + 10000)); assertEquals("SESSION_SCOPE_DENIED",wrong.getString("code"))
            val bad = client.command("lock",JSONObject().put("windows_session_id", session).put("reboot", true)); assertEquals("INVALID_PARAMS",bad.getString("code"))
            val signIn = client.command("unlock_request",JSONObject().put("windows_session_id",session)); assertEquals("SIGN_IN_REQUIRED",signIn.getString("code")); assertEquals(sessions.getJSONObject(0).getString("lock_state"),signIn.getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            val stop = client.command("stream_stop",JSONObject().put("stream_id","f".repeat(32))); assertEquals("completed",stop.getString("disposition"))
            android.util.Log.i("LapContQA","Product pairing/status/media attachment/scope/sign-in/Stop passed using non-exportable Android Keystore identity")
        } finally { client.close(); owner.cancel() }
    }
    @Test fun realCameraAudioTalkAndLeaseIntegration() = runBlocking<Unit> {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        assertEquals("Allow LapCont microphone access through the normal permission prompt before the talk-back test",android.content.pm.PackageManager.PERMISSION_GRANTED,context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO))
        val high = InstrumentationRegistry.getArguments().getString("mediaPreset") == "720p"
        val width = if(high) 1280 else 640; val height = if(high) 720 else 480; val bitrate = if(high) 2000000 else 1000000
        val db = androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pcs = db.pcs().observe().first(); db.close()
        Assume.assumeTrue("Requires an enrolled development PC with camera/microphone/talk grants", pcs.isNotEmpty())
        val activity=androidx.test.core.app.ActivityScenario.launch(com.lapcont.mobile.presentation.MainActivity::class.java)
        val pc = qaRoute(pcs.first()); val owner = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val ready = CompletableDeferred<JSONObject>(); val failures = java.util.concurrent.ConcurrentLinkedQueue<String>()
        val images = java.util.concurrent.atomic.AtomicInteger(); val thread = android.os.HandlerThread("LapCont-QA-render").apply { start() }
        val output = android.media.ImageReader.newInstance(width,height,android.graphics.ImageFormat.YUV_420_888,3)
        output.setOnImageAvailableListener({ reader -> reader.acquireLatestImage()?.use { images.incrementAndGet() } },android.os.Handler(thread.looper))
        val media = com.lapcont.mobile.media.LiveMedia(context,owner) { failures.add(it) }; media.surface = output.surface
        val keyframes=java.util.concurrent.atomic.AtomicInteger(); var encoder="unknown"
        val client = PcClient(pc, IdentityStore(context), { state,_ -> ready.complete(state) },{}, { if(it.type==1 && it.flags and 1!=0) keyframes.incrementAndGet(); if(it.type==5) StrictJson.parse(it.payload).let { format -> if(format.optBoolean("video")) encoder=format.optString("encoder") }; media.frame(it) },{ })
        fun id() = java.util.UUID.randomUUID().toString().replace("-", "")
        try {
            client.start(owner); val state = withTimeout(20000) { ready.await() }; val session = state.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            val lockState=state.getJSONArray("sessions").getJSONObject(0).getString("lock_state")
            assertEquals(if(InstrumentationRegistry.getArguments().getString("allowLockedQaMedia")=="true") "locked" else "unlocked",lockState)
            val stream = id(); media.start(stream,true); media.awaitReady()
            val settings = JSONObject().put("windows_session_id",session).put("stream_id",stream).put("video",true).put("audio",true).put("width",width).put("height",height).put("fps",30).put("bitrate",bitrate).put("recovery",false)
            assertNotEquals("failed",client.command("stream_start",settings).getString("disposition"))
            withTimeout(18000) { while (images.get() < 10 || media.statistics().decodedAudioPackets < 20 || keyframes.get()<2) { check(failures.isEmpty()) { failures.joinToString() }; delay(100) } }
            val beforeRecovery=keyframes.get(); settings.put("recovery",true)
            assertEquals("completed",client.command("stream_start",settings).getString("disposition"))
            withTimeout(3000) { while(keyframes.get()<=beforeRecovery) { check(failures.isEmpty()); delay(50) } }; settings.put("recovery",false)
            val stop = JSONObject().put("stream_id",stream); assertEquals("completed",client.command("stream_stop",stop).getString("disposition")); media.stop(); media.awaitStopped()
            assertEquals("completed",client.command("stream_stop",stop).getString("disposition"))
            val talk = id(); val talkParams = JSONObject().put("windows_session_id",session).put("stream_id",talk)
            assertNotEquals("failed",client.command("talk_start",talkParams).getString("disposition")); media.startTalk(talk) { client.sendMedia(it) }
            withTimeout(5000) { while (media.statistics().sentTalkPackets < 10) { check(failures.isEmpty()) { failures.joinToString() }; delay(50) } }
            media.stopTalk(); media.awaitStopped(); assertEquals("completed",client.command("talk_stop",JSONObject().put("stream_id",talk)).getString("disposition"))
            val leased = id(); assertNotEquals("failed",client.command("talk_start",JSONObject().put("windows_session_id",session).put("stream_id",leased)).getString("disposition")); delay(6500)
            assertEquals("idle",client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("talk_state"))
            val audioStream = id(); media.start(audioStream); media.awaitReady(); settings.put("stream_id",audioStream).put("video",false)
            assertNotEquals("failed",client.command("stream_start",settings).getString("disposition")); delay(32000)
            assertEquals("idle",client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            val timing=mutableListOf<Double>(); repeat(20) { val start=android.os.SystemClock.elapsedRealtimeNanos(); client.command("status"); timing+=(android.os.SystemClock.elapsedRealtimeNanos()-start)/1000000.0 }; timing.sort()
            val report=JSONObject().put("android_api",android.os.Build.VERSION.SDK_INT).put("network",InstrumentationRegistry.getArguments().getString("networkEvidence") ?: "route not recorded; LAN/WAN unverified").put("windows_lock_state",lockState).put("width",width).put("height",height).put("fps_requested",30).put("bitrate_target",bitrate).put("encoder",encoder).put("rendered_images",images.get()).put("keyframes",keyframes.get()).put("audio_decoded_packets",media.statistics().decodedAudioPackets).put("talk_sent_packets",media.statistics().sentTalkPackets).put("command_samples",timing.size).put("command_median_ms",timing[10]).put("command_p95_ms",timing[18]).put("capture_to_render_latency","unmeasured")
            File(context.cacheDir,"product-measurements.json").writeText(report.toString())
            android.util.Log.i("LapContQA","Real media/Stop/leases passed: $report")
        } finally {
            android.util.Log.i("LapContQA","Media metadata: images=${images.get()} keyframes=${keyframes.get()} counters=${media.statistics()} failures=${failures.size}")
            media.stop(); media.awaitStopped(); client.close(); owner.cancel(); output.close(); thread.quitSafely(); activity.close()
        }
    }
    @Test fun savedEnrollmentProtectedCommands() = runBlocking<Unit> {
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().firstOrNull()?.let(::qaRoute); db.close()
        Assume.assumeTrue("Requires an already paired QA PC",pc!=null)
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO); val ready=CompletableDeferred<JSONObject>()
        val client=PcClient(pc!!,IdentityStore(context),{s,_->ready.complete(s)},{},{},{})
        try {
            client.start(owner); val initial=withTimeout(20000){ready.await()}
            assertEquals(pc.id,initial.getString("pc_id")); assertFalse(initial.getBoolean("true_unlock_available"))
            val session=initial.getJSONArray("sessions").getJSONObject(0); val sid=session.getInt("windows_session_id")
            assertTrue(session.getBoolean("agent_available"))
            assertEquals("SESSION_SCOPE_DENIED",client.command("lock",JSONObject().put("windows_session_id",sid+10000)).getString("code"))
            assertEquals("INVALID_PARAMS",client.command("lock",JSONObject().put("windows_session_id",sid).put("reboot",true)).getString("code"))
            val request=client.command("unlock_request",JSONObject().put("windows_session_id",sid))
            assertEquals("SIGN_IN_REQUIRED",request.getString("code")); assertEquals(session.getString("lock_state"),request.getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            repeat(2){assertEquals("completed",client.command("stream_stop",JSONObject().put("stream_id","f".repeat(32))).getString("disposition"))}
            val report=JSONObject().put("android_api",android.os.Build.VERSION.SDK_INT).put("abi",android.os.Build.SUPPORTED_ABIS.first()).put("network",InstrumentationRegistry.getArguments().getString("networkEvidence") ?: "unrecorded").put("loopback_route",pc.addresses.split(',').any{it=="127.0.0.1"}).put("grants",initial.getInt("grants")).put("lock_state",session.getString("lock_state")).put("agent_available",true).put("true_unlock_available",false).put("scoped_commands","passed")
            File(context.cacheDir,"product-status-summary.json").writeText(report.toString())
        } finally {client.close();owner.cancel()}
    }
    /** Explicit opt-in: locks the real PC and waits for the owner to sign back in normally. */
    @Test fun installedServicePhoneLockAndNormalSignIn() = runBlocking<Unit> {
        Assume.assumeTrue("Requires coordinated real Windows sign-in",InstrumentationRegistry.getArguments().getString("windowsLockQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().firstOrNull()?.let(::qaRoute); db.close(); Assume.assumeTrue(pc!=null)
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO); val ready=CompletableDeferred<JSONObject>()
        val events=java.util.concurrent.ConcurrentLinkedQueue<String>(); val responses=java.util.concurrent.ConcurrentLinkedQueue<JSONObject>()
        val packets=java.util.concurrent.atomic.AtomicInteger()
        val client=PcClient(pc!!,IdentityStore(context),{s,_->ready.complete(s)},{events.add(it.getString("event"))},{if(it.type==2)packets.incrementAndGet()},{}, {responses.add(it)})
        try {
            client.start(owner); val initial=withTimeout(20000){ready.await()}; val sid=initial.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            assertEquals("unlocked",initial.getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            val stream=java.util.UUID.randomUUID().toString().replace("-","")
            val p=JSONObject().put("windows_session_id",sid).put("stream_id",stream).put("video",false).put("audio",true).put("width",640).put("height",480).put("fps",30).put("bitrate",1000000).put("recovery",false)
            assertNotEquals("failed",client.command("stream_start",p).getString("disposition")); withTimeout(8000){while(packets.get()<10)delay(50)}
            val lock=client.command("lock",JSONObject().put("windows_session_id",sid)); val id=lock.getString("request_id")
            withTimeout(12000){while(!events.contains("SessionLock") || responses.none{it.getString("request_id")==id && it.getString("disposition")=="completed"})delay(50)}
            val dispositions=responses.filter{it.getString("request_id")==id}.map{it.getString("disposition")}
            assertEquals(listOf("accepted","completed"),dispositions)
            val locked=client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0)
            assertEquals("locked",locked.getString("lock_state")); assertEquals("idle",locked.getString("media_state"))
            val request=client.command("unlock_request",JSONObject().put("windows_session_id",sid))
            assertEquals("SIGN_IN_REQUIRED",request.getString("code")); assertEquals("locked",request.getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("lock_state"))
            assertEquals("MEDIA_LOCK_POLICY",client.command("stream_start",p.put("stream_id",java.util.UUID.randomUUID().toString().replace("-",""))).getString("code"))
            delay(400); val stopped=packets.get(); delay(400); assertEquals(stopped,packets.get())
            File(context.cacheDir,"product-lock-qa-ready.txt").writeText("SCM lock confirmed; normal Windows sign-in required")
            withTimeout(240000){while(!events.contains("SessionUnlock"))delay(200)}
            val returned=client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0)
            assertEquals("unlocked",returned.getString("lock_state")); assertEquals("idle",returned.getString("media_state")); assertEquals("idle",returned.getString("talk_state"))
            File(context.cacheDir,"product-lock-summary.json").writeText(JSONObject().put("status","passed").put("lock_responses",org.json.JSONArray(dispositions)).put("scm_events",org.json.JSONArray(events.toList())).put("capture_stopped_on_lock",true).put("request_unlock_preserved_locked_state",true).put("locked_capture_denied",true).put("normal_sign_in_observed",true).put("capture_replayed_after_unlock",false).toString())
        } finally {client.close();owner.cancel()}
    }
    @Test fun lockedSessionDeniesMediaByDefault() = runBlocking<Unit> {
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pcs=db.pcs().observe().first(); db.close(); Assume.assumeTrue("Requires an enrolled QA PC",pcs.isNotEmpty())
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO); val ready=CompletableDeferred<JSONObject>()
        val frames=java.util.concurrent.atomic.AtomicInteger(); val client=PcClient(qaRoute(pcs.first()),IdentityStore(context),{s,_->ready.complete(s)},{},{frames.incrementAndGet()},{})
        try {
            client.start(owner); val state=withTimeout(20000){ready.await()}; val session=state.getJSONArray("sessions").getJSONObject(0)
            assertEquals("locked",session.getString("lock_state"))
            val p=JSONObject().put("windows_session_id",session.getInt("windows_session_id")).put("stream_id",java.util.UUID.randomUUID().toString().replace("-","")).put("video",false).put("audio",true).put("width",640).put("height",480).put("fps",30).put("bitrate",1000000).put("recovery",false)
            assertEquals("MEDIA_LOCK_POLICY",client.command("stream_start",p).getString("code")); delay(400); assertEquals(0,frames.get())
        } finally { client.close(); owner.cancel() }
    }
    /** Opt-in: the QA operator must switch the actual PC owner setting after the marker appears. */
    @Test fun disablingLockedMediaStopsActiveCapture() = runBlocking<Unit> {
        Assume.assumeTrue("Requires an explicitly coordinated local privacy-setting test",InstrumentationRegistry.getArguments().getString("privacySwitchQa")=="true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext; val marker=File(context.cacheDir,"privacy-qa-ready")
        marker.delete(); val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().first(); db.close()
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO); val ready=CompletableDeferred<JSONObject>(); val packets=java.util.concurrent.atomic.AtomicInteger()
        val client=PcClient(pc,IdentityStore(context),{s,_->ready.complete(s)},{},{if(it.type==2)packets.incrementAndGet()},{})
        try {
            client.start(owner); val session=withTimeout(20000){ready.await()}.getJSONArray("sessions").getJSONObject(0)
            assertEquals("locked",session.getString("lock_state"))
            val p=JSONObject().put("windows_session_id",session.getInt("windows_session_id")).put("stream_id",java.util.UUID.randomUUID().toString().replace("-","")).put("video",false).put("audio",true).put("width",640).put("height",480).put("fps",30).put("bitrate",1000000).put("recovery",false)
            assertNotEquals("failed",client.command("stream_start",p).getString("disposition")); withTimeout(5000){while(packets.get()<10)delay(50)}
            marker.writeText("ready")
            withTimeout(15000) { while(client.command("status").getJSONObject("state").getJSONArray("sessions").getJSONObject(0).getString("media_state")!="idle")delay(200) }
            delay(700); val stopped=packets.get(); delay(400); assertEquals(stopped,packets.get())
            assertEquals("MEDIA_LOCK_POLICY",client.command("stream_start",p).getString("code"))
        } finally { marker.delete(); client.close(); owner.cancel() }
    }
    /** Opt-in: revokes only the disposable enrollment explicitly selected by the QA operator. */
    @Test fun connectionLossAndRevocationStopCapture() = runBlocking<Unit> {
        Assume.assumeTrue("Requires explicit disposable-enrollment opt-in", InstrumentationRegistry.getArguments().getString("destructiveQaEnrollment") == "true")
        val context=InstrumentationRegistry.getInstrumentation().targetContext
        val db=androidx.room.Room.databaseBuilder(context,PcDatabase::class.java,"pcs.db").build()
        val pc=db.pcs().observe().first().first(); db.close()
        val owner=CoroutineScope(SupervisorJob()+Dispatchers.IO); val packets=java.util.concurrent.atomic.AtomicInteger()
        val ready=CompletableDeferred<JSONObject>(); var client=PcClient(pc,IdentityStore(context),{s,_->ready.complete(s)},{},{if(it.type==2)packets.incrementAndGet()},{})
        try {
            client.start(owner); val session=withTimeout(20000){ready.await()}.getJSONArray("sessions").getJSONObject(0).getInt("windows_session_id")
            fun settings(id:String)=JSONObject().put("windows_session_id",session).put("stream_id",id).put("video",false).put("audio",true).put("width",640).put("height",480).put("fps",30).put("bitrate",1000000).put("recovery",false)
            val canceled=java.util.UUID.randomUUID().toString().replace("-","")
            assertEquals("completed",client.command("stream_stop",JSONObject().put("stream_id",canceled)).getString("disposition"))
            assertEquals("STREAM_ENDED",client.command("stream_start",settings(canceled)).getString("code"))
            val id=java.util.UUID.randomUUID().toString().replace("-",""); val p=settings(id)
            assertNotEquals("failed",client.command("stream_start",p).getString("disposition"))
            withTimeout(5000){while(packets.get()<10)delay(50)}; client.close(); delay(1000)
            val reconnected=CompletableDeferred<JSONObject>(); client=PcClient(pc,IdentityStore(context),{s,_->reconnected.complete(s)},{},{if(it.type==2)packets.incrementAndGet()},{})
            client.start(owner); val state=withTimeout(20000){reconnected.await()}
            assertEquals("idle",state.getJSONArray("sessions").getJSONObject(0).getString("media_state"))
            assertEquals("STREAM_ENDED",client.command("stream_start",p).getString("code"))
            val afterReconnect=packets.get(); delay(300); assertEquals(afterReconnect,packets.get())
            val next=java.util.UUID.randomUUID().toString().replace("-","")
            assertNotEquals("failed",client.command("stream_start",settings(next)).getString("disposition"))
            withTimeout(5000){while(packets.get()<afterReconnect+10)delay(50)}
            assertEquals("completed",client.command("unpair").getString("disposition")); delay(1000)
            val stopped=packets.get(); delay(400); assertEquals(stopped,packets.get()); client.close()
            val rejected=CompletableDeferred<String>(); val authenticated=java.util.concurrent.atomic.AtomicBoolean()
            client=PcClient(pc,IdentityStore(context),{_,_->authenticated.set(true)},{},{},{rejected.complete(it)})
            client.start(owner); withTimeout(20000){rejected.await()}; assertFalse(authenticated.get())
            android.util.Log.i("LapContQA","Connection loss stopped capture; reconnect stayed idle; ended stream could not restart; revocation stopped capture and rejected the old identity")
        } finally { client.close(); owner.cancel() }
    }
}
