// LapCont — Android — StateFlow single source of truth and explicit live-action ownership
// License: MIT
package com.lapcont.mobile.data
import android.content.Context
import android.view.Surface
import com.lapcont.mobile.media.LiveMedia
import com.lapcont.mobile.proximity.PhoneAdvertiser
import com.lapcont.mobile.proximity.proximityCandidates
import com.lapcont.mobile.notifications.Notifications
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import org.json.JSONObject
import javax.inject.Inject
import javax.inject.Singleton

/** Independent connection, verified lock, media and proximity state. Repository mutations execute on its supervisor scope. */
data class PcStatus(val online: Boolean = false, val route: String = "", val detail: String = "Offline · status unknown", val verified: JSONObject? = null)
@Singleton
class PcRepository @Inject constructor(private val dao: PcDao, val identity: IdentityStore, @ApplicationContext val context: Context) {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    val pcs = dao.observe().stateIn(scope, SharingStarted.Eagerly, emptyList())
    private val clients = java.util.concurrent.ConcurrentHashMap<String, PcClient>()
    private val connectionGate = Any()
    private val mutable = MutableStateFlow<Map<String, PcStatus>>(emptyMap()); val states = mutable.asStateFlow()
    private val text = MutableStateFlow("Ready"); val message = text.asStateFlow()
    private val live = MutableStateFlow<String?>(null); val livePc = live.asStateFlow()
    private var streamId: String? = null; private var talkId: String? = null; private var liveRenewal: Job? = null; private var talkRenewal: Job? = null
    private var talkPc: String? = null; private var talkRequested = false
    private val media = LiveMedia(context, scope) { reason -> text.value = reason; scope.launch { stopMedia() } }
    private val advertiser = PhoneAdvertiser(context, identity)
    private val proximityGate = Any()
    private var proximityJob: Job? = null
    private val events = java.util.LinkedHashSet<String>()
    private val hintAt = java.util.concurrent.ConcurrentHashMap<String,Long>()
    private fun verifiedEvent(pc:PairedPc,e:JSONObject) { synchronized(events) { val id=e.getString("event_id"); if(events.add(id)) { while(events.size>256) events.remove(events.first()); Notifications(context).event(pc,e) } } }
    private fun update(id: String, f: (PcStatus) -> PcStatus) { mutable.update { old -> old + (id to f(old[id] ?: PcStatus())) } }
    fun connect(pc: PairedPc) = synchronized(connectionGate) {
        clients[pc.id]?.let { current ->
            if(states.value[pc.id]?.online==true) scope.launch { try { current.command("status") } catch(_:java.io.IOException) { current.retryNow() } }
            else current.retryNow()
            return@synchronized
        }
        update(pc.id) { it.copy(online=false,detail="Connecting to PC · last verified status is stale") }
        lateinit var c: PcClient
        c = PcClient(pc, identity,
            { state, route ->
                update(pc.id) { old -> if(clients[pc.id]===c) PcStatus(true, route, "Connected", state) else old }
                if(clients[pc.id]===c) {
                    state.optJSONArray("events")?.let { a -> for(i in 0 until a.length()) verifiedEvent(pc,a.getJSONObject(i)) }
                    if(state.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")!=true && (live.value==pc.id || talkPc==pc.id))
                        scope.launch { if(clients[pc.id]===c && states.value[pc.id]?.verified?.optJSONArray("sessions")?.optJSONObject(0)?.optBoolean("agent_available")!=true) stopMedia() }
                }
            },
            { e -> if(clients[pc.id]===c) verifiedEvent(pc,e) },
            { frame -> if(clients[pc.id]===c) media.frame(frame) },
            { reason -> update(pc.id) { old -> if(clients[pc.id]===c) old.copy(online = false, detail = reason) else old }; if(clients[pc.id]===c && (live.value == pc.id || talkPc == pc.id)) scope.launch { if(clients[pc.id]===c) stopMedia() } })
        clients[pc.id] = c; c.start(scope)
    }
    fun connectAll() { pcs.value.forEach(::connect) }
    /** Push contains no authoritative status. A bounded fresh TLS fetch is the only source of displayed events. */
    suspend fun verifyPushHint(id:String) {
        if(!id.matches(Regex("[a-f0-9]{32}"))) return
        val pc=dao.get(id) ?: return
        val now=android.os.SystemClock.elapsedRealtime(); val previous=hintAt.put(id,now)
        if(previous!=null && now-previous<10000) return
        if(clients.containsKey(id)) { try { action(id,"status") } catch(_:Exception) { }; return }
        val done=CompletableDeferred<Unit>()
        val client=PcClient(pc,identity,{ state,_ -> state.optJSONArray("events")?.let { a -> for(i in 0 until a.length()) verifiedEvent(pc,a.getJSONObject(i)) }; done.complete(Unit) },{ e -> verifiedEvent(pc,e) },{}, {})
        try { client.start(scope); withTimeout(15000) { done.await() } } catch(_:java.io.IOException) { } catch(_:TimeoutCancellationException) { } finally { client.close() }
    }
    suspend fun enroll(raw: String) {
        val connecting = "Connecting to PC · approval appears on the PC after identity verification"
        val waiting = "PC identity verified · approve this phone on the PC"
        text.value = connecting
        try {
            val q = PairingQr.parse(raw); require(dao.get(q.getString("pc_id")) == null) { "This PC is already paired. Revoke the old pairing before replacing its identity." }
            val pc = pair(q, identity) { text.value = waiting }
            dao.save(pc); text.value = "Paired · permissions are managed at the PC"; connect(pc)
        } finally {
            if (text.value == connecting || text.value == waiting) text.value = "Pairing ended · generate a fresh PC QR to retry"
        }
    }
    suspend fun action(id: String, name: String, params: JSONObject = JSONObject()): JSONObject {
        val result = (clients[id] ?: error("Connect to this PC first")).command(name, params)
        text.value = result.getString("detail"); if (result.getString("disposition") == "failed") error("${result.getString("code")}: ${result.getString("detail")}")
        return result
    }
    fun session(id: String): Int = states.value[id]?.verified?.getJSONArray("sessions")?.let { a -> require(a.length() > 0) { "No authorized Windows session is available" }; a.getJSONObject(0).getInt("windows_session_id") } ?: error("Refresh PC status first")
    fun surface(value: Surface?) { media.surface = value; if (value == null && live.value != null) scope.launch { stopMedia() } }
    suspend fun startMedia(id: String, video: Boolean, audio: Boolean, high: Boolean) {
        stopMedia(); media.awaitStopped(); val stream = java.util.UUID.randomUUID().toString().replace("-", ""); streamId = stream; live.value = id; media.start(stream,video)
        val params = JSONObject().put("windows_session_id", session(id)).put("stream_id", stream).put("video", video).put("audio", audio)
            .put("width", if (high) 1280 else 640).put("height", if (high) 720 else 480).put("fps", 30).put("bitrate", if (high) 2000000 else 1000000).put("recovery", false)
        try { media.awaitReady(); action(id, "stream_start", params); currentCoroutineContext().ensureActive(); if (streamId != stream) error("Stream was stopped while starting"); liveRenewal = scope.launch {
            try { while (isActive) { delay(10000); action(id, "stream_start", params) } }
            catch(e:CancellationException) { throw e }
            catch(_:Exception) { if(streamId==stream) { stopMedia(); text.value="Live media stopped · reconnect to the PC and start a new stream" } }
        } }
        catch (e: Exception) {
            if(streamId==stream) { live.value = null; streamId = null; media.stop() }
            cancelRemoteStart(id,"stream_stop",stream); throw e
        }
    }
    suspend fun stopMedia() {
        liveRenewal?.cancel(); liveRenewal = null; stopTalk(); val pc = live.value; val id = streamId; live.value = null; streamId = null; media.stop()
        if (pc != null && id != null) try { action(pc, "stream_stop", JSONObject().put("stream_id", id)) } catch (e: Exception) { text.value = "Stopped locally · PC lease expires if disconnected" }
    }
    suspend fun startTalk(pc: String) {
        stopTalk(); talkRequested = true; talkPc = pc; val id = java.util.UUID.randomUUID().toString().replace("-", ""); talkId = id
        try {
            val parameters = JSONObject().put("windows_session_id", session(pc)).put("stream_id", id)
            action(pc, "talk_start", parameters)
            if (!talkRequested || talkId != id) { try { action(pc, "talk_stop", JSONObject().put("stream_id", id)) } catch (_: Exception) { }; return }
            media.startTalk(id) { frame -> clients[pc]?.sendMedia(frame) ?: error("PC disconnected") }
            talkRenewal = scope.launch {
                try { while (isActive) { delay(2000); action(pc, "talk_start", parameters) } }
                catch(e:CancellationException) { throw e }
                catch(_:Exception) { if(talkId==id) { stopTalk(); text.value="Talk-back stopped · reconnect to the PC and hold Talk again" } }
            }
        } catch (e: Exception) {
            if(talkId==id) { talkRequested=false; talkId=null; talkPc=null; media.stopTalk() }
            cancelRemoteStart(pc,"talk_stop",id); throw e
        }
    }
    private suspend fun cancelRemoteStart(pc:String,command:String,id:String) = withContext(NonCancellable) {
        withTimeoutOrNull(2000) { try { action(pc,command,JSONObject().put("stream_id",id)) } catch(e:CancellationException) { throw e } catch(_:Exception) { } }
    }
    suspend fun stopTalk() {
        talkRequested = false; talkRenewal?.cancel(); talkRenewal = null; media.stopTalk(); val pc = talkPc; val id = talkId; talkPc = null; talkId = null
        if (pc != null && id != null) try { action(pc, "talk_stop", JSONObject().put("stream_id", id)) } catch (_: Exception) { text.value = "Talk stopped locally · PC has a short expiry lease" }
    }
    suspend fun remove(id: String, localOnly: Boolean = false) {
        if (!localOnly) action(id, "unpair")
        if (live.value == id || talkPc == id) stopMedia(); synchronized(connectionGate){clients.remove(id)?.close()}; dao.remove(id); identity.remove(id); mutable.update { it - id }
        text.value = if (localOnly) "Removed from this phone only. Revoke its permission at the PC." else "Pairing revoked at the PC and removed from this phone"
    }
    suspend fun settings(pc: PairedPc, addresses: String, relay: String?, credential: String?, proximity: Boolean) {
        require(addresses.split(',').size <= 8 && addresses.split(',').all(PairingQr::ipv4))
        if (relay != null) require(java.net.URI(relay).let { it.scheme == "wss" && it.host != null && it.userInfo == null && it.query == null })
        val secret = com.lapcont.transport.StrictJson.parse(identity.secret(pc.id)); if (credential != null) { require(credential.length in 32..512); secret.put("relay_credential", credential); identity.saveSecret(pc.id, secret.toString().toByteArray()) }
        val updated = pc.copy(addresses = addresses, relayUrl = relay, proximityEnabled = proximity); dao.save(updated)
        if(updated.addresses!=pc.addresses || updated.relayUrl!=pc.relayUrl || credential!=null) {
            if(live.value==pc.id || talkPc==pc.id) stopMedia()
            synchronized(connectionGate){clients.remove(pc.id)?.close();connect(updated)}
        } else connect(updated)
    }
    fun proximityStart() = synchronized(proximityGate) {
        if (proximityJob?.isActive == true && advertiser.isActive) return@synchronized
        proximityJob?.cancel()
        proximityJob = scope.launch {
            val monitoringContext = currentCoroutineContext()
            proximityCandidates(pcs, states.map { current ->
                current.mapNotNull { (id, status) ->
                    status.verified?.takeIf { it.has("grants") }?.let { id to it.getInt("grants") }
                }.toMap()
            }).collect { eligible ->
                synchronized(proximityGate) {
                    if (monitoringContext.isActive) advertiser.start(scope, eligible) { text.value = it }
                }
            }
        }
    }
    fun mediaStopTalkImmediately() { talkRequested = false; media.stopTalk() }
    fun mediaStopImmediately() { media.stop() }
    suspend fun monitoringStop() {
        synchronized(proximityGate) { proximityJob?.cancel(); proximityJob = null; advertiser.stop() }
        stopMedia(); synchronized(connectionGate){val stopping=clients.values.toList();clients.clear();stopping.forEach { it.close() }}; mutable.update { it.mapValues { p -> p.value.copy(online = false, detail = "Monitoring stopped · last status is stale") } }
    }
}
