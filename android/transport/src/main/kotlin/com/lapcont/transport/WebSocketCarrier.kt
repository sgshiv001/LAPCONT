// LapCont — Transport — Bounded WebSocket carrier and exact-pin TLS trust
// License: MIT
package com.lapcont.transport
import java.io.IOException
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.*
import kotlinx.coroutines.channels.Channel
import okhttp3.*
import okio.ByteString
import okio.ByteString.Companion.toByteString

/** Per-connection exact-pin trust manager. Never installed globally; no client trust-all path. */
class PinTrust(private val fingerprint: String) : X509TrustManager {
    override fun getAcceptedIssuers() = emptyArray<X509Certificate>()
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) { throw CertificateException("Client validation belongs on PC") }
    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val leaf = chain.firstOrNull() ?: throw CertificateException("Missing certificate")
        leaf.checkValidity()
        if (!MessageDigest.isEqual(MessageDigest.getInstance("SHA-256").digest(leaf.encoded), Frames.unhex(fingerprint)))
            throw CertificateException("Wrong PC identity")
    }
}

/** One connection and bounded queues. Coroutine reads; OkHttp callback never blocks. Close cancels the carrier. */
class WebSocketCarrier(url: String, outerTestPin: String? = null, headers: Map<String, String> = emptyMap()) : Carrier, AutoCloseable {
    private val incoming = Channel<ByteArray>(64)
    private val incomingBytes = java.util.concurrent.atomic.AtomicInteger()
    private val client: OkHttpClient
    private val socket: WebSocket
    init {
        require(url.startsWith("wss://")) { "TLS-protected wss:// carrier required" }
        val tls = ConnectionSpec.Builder(ConnectionSpec.MODERN_TLS).tlsVersions(TlsVersion.TLS_1_3, TlsVersion.TLS_1_2).build()
        val builder = OkHttpClient.Builder().connectionSpecs(listOf(tls)).connectTimeout(5, TimeUnit.SECONDS).readTimeout(0, TimeUnit.MILLISECONDS)
        if (outerTestPin != null) {
            // Only the local test host uses an exact outer pin. Production relay uses default CA/hostname trust.
            val trust = PinTrust(outerTestPin)
            val context = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }
            builder.sslSocketFactory(context.socketFactory, trust)
            val expectedHost = java.net.URI(url).host
            builder.hostnameVerifier { host, session -> host == expectedHost && session.peerCertificates.firstOrNull()?.let {
                MessageDigest.isEqual(MessageDigest.getInstance("SHA-256").digest(it.encoded), Frames.unhex(outerTestPin))
            } == true }
        }
        client = builder.build()
        val request = Request.Builder().url(url).apply { headers.forEach { (key, value) -> require(value.length <= 512); header(key, value) } }.build()
        socket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                if (bytes.size !in 1..65536) {
                    incoming.close(IOException("Carrier limit exceeded")); webSocket.cancel()
                    return
                }
                if (incomingBytes.addAndGet(bytes.size) > 1048576 || !incoming.trySend(bytes.toByteArray()).isSuccess) {
                    incomingBytes.addAndGet(-bytes.size)
                    incoming.close(IOException("Carrier limit exceeded")); webSocket.cancel()
                }
            }
            override fun onMessage(webSocket: WebSocket, text: String) { incoming.close(IOException("Carrier must be binary")); webSocket.cancel() }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { incoming.close(IOException("Carrier failed", t)) }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { incoming.close() }
        })
    }
    override suspend fun receive() = incoming.receive().also { incomingBytes.addAndGet(-it.size) }
    override suspend fun send(bytes: ByteArray) {
        require(bytes.size in 1..65536)
        if (socket.queueSize() + bytes.size > 65536 || !socket.send(bytes.toByteString())) throw IOException("Carrier backpressure/disconnect")
    }
    override fun close() { socket.cancel(); incoming.cancel(); client.connectionPool.evictAll(); client.dispatcher.executorService.shutdown() }
}
