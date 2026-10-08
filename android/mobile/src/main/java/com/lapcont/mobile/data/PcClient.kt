// LapCont — Android — Pinned LAN, authenticated relay fallback, request correlation and fresh reconnect context
// License: MIT
package com.lapcont.mobile.data

import com.lapcont.transport.*
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import org.json.JSONObject
import java.io.IOException
import java.time.Instant
import java.util.UUID
import javax.net.ssl.SSLContext

/** Worker-owned connection. Reconnection gets status only; business actions/media are never automatically replayed. */
class PcClient(private val pc: PairedPc, private val identity: IdentityStore,
    private val onState: (JSONObject, String) -> Unit, private val onEvent: (JSONObject) -> Unit,
    private val onMedia: suspend (MediaFrame) -> Unit, private val onOffline: (String) -> Unit,
    private val onResponse: (JSONObject) -> Unit = {}) {
    private var scope: CoroutineScope? = null
    @Volatile private var control: DuplexTunnel? = null
    private var media: DuplexTunnel? = null
    private var session = ""
    private var clockOffset = 0L
    private var sequence = 0L
    private var route = ""
    private val failedLan = hashMapOf<String,Long>()
    private var candidateAddress: String? = null
    private val requests = java.util.concurrent.ConcurrentHashMap<String, CompletableDeferred<JSONObject>>()
    private val commands = Channel<Pair<String, ByteArray>>(32)
    private var controlCarrier: WebSocketCarrier? = null
    private var mediaCarrier: WebSocketCarrier? = null
    private var connectJob: Job? = null
    private val retry = Channel<Unit>(Channel.CONFLATED)
    private val retryRequested = java.util.concurrent.atomic.AtomicBoolean()
    fun retryNow() { retryRequested.set(true);retry.trySend(Unit) }
    fun start(owner: CoroutineScope) { scope = owner; connectJob = owner.launch(Dispatchers.IO) { reconnect() } }
    suspend fun command(name: String, params: JSONObject = JSONObject()): JSONObject {
        require(name in listOf("status", "lock", "unlock_request", "stream_start", "stream_stop", "talk_start", "talk_stop", "calibrate", "unpair"))
        if (control == null) throw IOException("PC unavailable. Connect and try again.")
        val id = UUID.randomUUID().toString().replace("-", ""); val issued = Instant.ofEpochMilli(System.currentTimeMillis() + clockOffset)
        val envelope = synchronized(this) { JSONObject().put("protocol", "LPC1").put("kind", "command").put("request_id", id).put("pc_id", pc.id).put("phone_id", identity.phoneId)
            .put("connection_session_id", session).put("sequence", ++sequence).put("issued_at_utc", issued.toString()).put("expires_at_utc", issued.plusSeconds(10).toString()).put("command", name).put("params", params).toString().toByteArray() }
        require(envelope.size <= Frames.CONTROL_LIMIT)
        val result = CompletableDeferred<JSONObject>(); requests[id] = result
        try { if (!commands.trySend(id to envelope).isSuccess) throw IOException("Control queue full"); return withTimeout(12000) { result.await() } }
        finally { requests.remove(id) }
    }
    suspend fun sendMedia(frame: MediaFrame) { (media ?: throw IOException("Media connection unavailable")).writeFrame(Frames.encode(frame), Frames.MEDIA_LIMIT + 40) }
    fun close() { connectJob?.cancel(); controlCarrier?.close(); mediaCarrier?.close(); control = null; media = null }
    private fun tls(carrier: Carrier): DuplexTunnel {
        val context = SSLContext.getInstance("TLS").apply { init(arrayOf(identity.keyManager()), arrayOf(PinTrust(pc.pin)), null) }
        val engine = context.createSSLEngine("localhost",pc.port).apply { useClientMode = true; enabledProtocols = arrayOf("TLSv1.3", "TLSv1.2") }
        return DuplexTunnel(engine, carrier)
    }
    private suspend fun reconnect() {
        var attempt = 0
        while (currentCoroutineContext().isActive) {
            var healthy = false
            var stage = "route"
            try {
                coroutineScope {
                    if(retryRequested.getAndSet(false)) failedLan.clear()
                    val (url, pin, headers, chosenRoute) = selectRoute()
                    route = chosenRoute; controlCarrier = WebSocketCarrier(url, pin, headers); val tunnel = tls(controlCarrier!!)
                    stage = "control handshake"
                    val hello = withTimeout(10000) { tunnel.handshake(); StrictJson.parse(tunnel.readFrame(Frames.CONTROL_LIMIT)) }
                    stage = "control context"; validateContext(hello, "control"); session = hello.getString("connection_session_id"); require(session.matches(Regex("[a-fA-F0-9]{32}"))); sequence = 0
                    clockOffset = java.time.OffsetDateTime.parse(hello.getString("pc_time_utc")).toInstant().toEpochMilli() - System.currentTimeMillis()
                    val mediaHeaders = if (headers.isEmpty()) emptyMap() else headers + ("X-LapCont-Channel" to "media")
                    mediaCarrier = WebSocketCarrier(if (chosenRoute == "LAN") url.replace("/control", "/media") else url, pin, mediaHeaders)
                    val mediaTunnel = tls(mediaCarrier!!)
                    stage = "media handshake"
                    withTimeout(10000) {
                        mediaTunnel.handshake(); mediaTunnel.writeFrame(JSONObject().put("protocol", "LPC1").put("kind", "media_attach").put("pc_id", pc.id).put("phone_id", identity.phoneId)
                            .put("connection_session_id", session).put("media_attachment", hello.getString("media_attachment")).toString().toByteArray(), Frames.CONTROL_LIMIT)
                        val h = StrictJson.parse(mediaTunnel.readFrame(Frames.CONTROL_LIMIT)); validateContext(h, "media"); require(h.getString("connection_session_id") == session)
                    }
                    control = tunnel; media = mediaTunnel
                    stage = "connected"
                    launch {
                        for ((id, bytes) in commands) { if (requests.containsKey(id)) tunnel.writeFrame(bytes, Frames.CONTROL_LIMIT) }
                    }
                    launch {
                        val mediaSequences = hashMapOf<String, Long>()
                        while (isActive) {
                            val frame = Frames.decode(mediaTunnel.readFrame(Frames.MEDIA_LIMIT + 40)); require(frame.type != 3); val id = Frames.hex(frame.streamId)
                            val prior = mediaSequences[id]; require(prior == null || frame.sequence > prior); if (prior == null) require(mediaSequences.size < 32)
                            mediaSequences[id] = frame.sequence; onMedia(frame)
                        }
                    }
                    launch { delay(9 * 60 * 1000L); throw IOException("Renewing secure connection") }
                    launch { val response = command("status"); check(response.getString("disposition") != "failed"); healthy = true; attempt = 0 }
                    launch { while (isActive) { delay(5000); command("status") } }
                    while (isActive) {
                        val response = StrictJson.parse(tunnel.readFrame(Frames.CONTROL_LIMIT)); require(response.getString("protocol") == "LPC1")
                        when (response.getString("kind")) {
                            "response" -> { response.optJSONObject("state")?.let { require(it.getString("pc_id") == pc.id); onState(it, route) }; onResponse(response); requests.remove(response.getString("request_id"))?.complete(response) }
                            "event" -> { require(response.getString("pc_id") == pc.id); onEvent(response.getJSONObject("event")); launch { command("status") } }
                            else -> throw IOException("Unexpected protected control envelope")
                        }
                    }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                if(!healthy && route=="LAN") candidateAddress?.let { failedLan[it]=android.os.SystemClock.elapsedRealtime()+15000 }
                val boundary = when(e.message) { "Carrier limit exceeded" -> "receive_limit"; "Carrier backpressure/disconnect" -> "send_limit_or_disconnect"; else -> "transport" }
                android.util.Log.w("LapCont", "Connection stage=$stage error=${e.javaClass.simpleName} boundary=$boundary"); onOffline("PC unreachable (${e.javaClass.simpleName}); last status is stale")
            }
            finally {
                control = null; media = null; controlCarrier?.close(); mediaCarrier?.close()
                requests.values.forEach { it.completeExceptionally(IOException("Connection ended; submit a new action")) }; requests.clear(); while (commands.tryReceive().isSuccess) { }
                onOffline("Disconnected · media stopped · last verified status is stale")
            }
            if (healthy) attempt = 0 else attempt = minOf(attempt + 1, 9)
            withTimeoutOrNull((minOf(300000L, 1000L shl attempt) * kotlin.random.Random.nextDouble(.75, 1.0)).toLong()) { retry.receive() }
        }
    }
    private data class Route(val url: String, val pin: String?, val headers: Map<String, String>, val name: String)
    private suspend fun selectRoute(): Route {
        for (address in pc.addresses.split(',').take(8)) {
            if (address.isBlank()) continue
            if((failedLan[address] ?: 0)>android.os.SystemClock.elapsedRealtime()) continue
            try {
                withTimeout(1500) { withContext(Dispatchers.IO) { java.net.Socket().use { it.connect(java.net.InetSocketAddress(address, pc.port), 1200) } } }
                candidateAddress=address; return Route("wss://$address:${pc.port}/control", pc.pin, emptyMap(), "LAN")
            } catch (e: CancellationException) { if (!currentCoroutineContext().isActive) throw e }
            catch (_: IOException) { }
        }
        val relay = pc.relayUrl ?: throw IOException("LAN unavailable; no relay configured")
        candidateAddress=null
        val secrets = StrictJson.parse(identity.secret(pc.id))
        val credential = secrets.optString("relay_credential"); require(credential.length in 32..512) { "Provision a separate relay credential first" }
        return Route(relay, null, mapOf("Authorization" to "Bearer $credential", "X-LapCont-Route" to pc.id, "X-LapCont-Phone" to identity.phoneId, "X-LapCont-Role" to "phone", "X-LapCont-Channel" to "control"), "Relay")
    }
    private fun validateContext(h: JSONObject, channel: String) { require(h.getString("protocol") == "LPC1" && h.getString("kind") == "context" && h.getString("channel") == channel && h.getString("pc_id") == pc.id && h.getString("phone_id") == identity.phoneId) }
}
