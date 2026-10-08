// LapCont — Android — Strict QR bootstrap, proof of key possession and local approval
// License: MIT
package com.lapcont.mobile.data
import com.lapcont.transport.*
import kotlinx.coroutines.withTimeout
import org.json.JSONObject
import javax.net.ssl.SSLContext

/** Stateless scanner payload validation. Only numeric LAN endpoints are accepted for bootstrap enrollment. */
object PairingQr {
    fun parse(text: String): JSONObject {
        val q = StrictJson.parse(text.toByteArray()); StrictJson.exact(q, "app", "v", "pc_id", "pc_name", "addresses", "port", "relay_url", "cert_sha256", "identity_fingerprint", "pairing_token", "expires_in_seconds")
        require(q.getString("app") == "LapCont" && q.getInt("v") == 1 && q.getInt("expires_in_seconds") == 60)
        require(q.getString("pc_id").matches(Regex("[a-fA-F0-9]{32}")) && q.getString("pc_name").length in 1..64)
        for (key in listOf("cert_sha256", "identity_fingerprint", "pairing_token")) require(q.getString(key).matches(Regex("[a-fA-F0-9]{64}")))
        require(q.getString("identity_fingerprint") == q.getString("cert_sha256") && q.getInt("port") in 1024..65535)
        val a = q.getJSONArray("addresses"); require(a.length() in 1..8)
        for (i in 0 until a.length()) require(ipv4(a.getString(i)))
        if (!q.isNull("relay_url")) require(java.net.URI(q.getString("relay_url")).let { it.scheme == "wss" && it.host != null && it.rawUserInfo == null && it.rawQuery == null })
        return q
    }
    fun ipv4(address: String) = address.split('.').let { it.size == 4 && it.all { part -> part.isNotEmpty() && part.length <= 3 && part.all(Char::isDigit) && part.toInt() in 0..255 } }
}

/** Uses the phone's non-exportable key in both outer pin-verified WSS and inner standard mutual TLS. */
suspend fun pair(q: JSONObject, identity: IdentityStore, awaitingApproval: () -> Unit = {}): PairedPc = withTimeout(65000) {
    var last: Exception? = null
    val addresses = q.getJSONArray("addresses")
    for (index in 0 until addresses.length()) {
        try {
            val address = addresses.getString(index); val pin = q.getString("cert_sha256")
            WebSocketCarrier("wss://$address:${q.getInt("port")}/pair", pin).use { carrier ->
                val context = SSLContext.getInstance("TLS").apply { init(arrayOf(identity.keyManager()), arrayOf(PinTrust(pin)), null) }
                val engine = context.createSSLEngine("localhost",q.getInt("port")).apply { useClientMode = true; enabledProtocols = arrayOf("TLSv1.3", "TLSv1.2") }
                val tls = DuplexTunnel(engine, carrier)
                withTimeout(10000) { tls.handshake() }
                val hello = StrictJson.parse(tls.readFrame(Frames.CONTROL_LIMIT)); require(hello.getString("protocol") == "LPC1" && hello.getString("channel") == "pair" && hello.getString("pc_id") == q.getString("pc_id"))
                tls.writeFrame(JSONObject().put("protocol", "LPC1").put("kind", "command").put("command", "pair").put("pairing_token", q.getString("pairing_token"))
                    .put("phone_id", identity.phoneId).put("phone_name", android.os.Build.MODEL.take(64)).toString().toByteArray(), Frames.CONTROL_LIMIT)
                awaitingApproval()
                val result = StrictJson.parse(tls.readFrame(Frames.CONTROL_LIMIT)); check(result.getBoolean("ok")) { result.optString("detail", "Pairing rejected") }
                require(result.getString("pc_id") == q.getString("pc_id") && result.getString("phone_id") == identity.phoneId)
                identity.saveSecret(result.getString("pc_id"), JSONObject().put("proximity_key", result.getString("proximity_key")).toString().toByteArray())
                return@withTimeout PairedPc(result.getString("pc_id"), result.getString("pc_name"), (0 until addresses.length()).joinToString(",") { addresses.getString(it) }, q.getInt("port"), pin,
                    result.getString("windows_sid"), result.getInt("grants"), q.optString("relay_url").takeIf { it.isNotBlank() && it != "null" })
            }
        } catch (e: kotlinx.coroutines.CancellationException) { throw e }
        catch (e: Exception) { last = e; if (e !is java.io.IOException) throw e }
    }
    android.util.Log.w("LapCont", "Pairing transport ended type=${last?.javaClass?.simpleName} cause=${last?.cause?.javaClass?.simpleName}")
    val secureFailure = generateSequence(last as Throwable?) { it.cause }.take(8).any { it is javax.net.ssl.SSLException || it is java.security.cert.CertificateException }
    throw java.io.IOException(if(secureFailure) "Secure PC connection failed. Generate a fresh QR and verify the PC identity." else "Cannot reach the PC. Generate a fresh QR, use the same Wi-Fi, and check the PC firewall. Shared Wi-Fi may block connections between devices.",last)
}
