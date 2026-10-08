// LapCont — Transport — Production duplex SSLEngine with bounded records and handshake budget
// License: MIT
package com.lapcont.transport

import java.io.EOFException
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicLong
import javax.net.ssl.SSLEngine
import javax.net.ssl.SSLEngineResult
import javax.net.ssl.SSLException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** One reader and independently serialized writers. Provider operations share a short lock; carrier reads never hold it. */
class DuplexTunnel(private val engine: SSLEngine, private val carrier: Carrier) {
    private val network = ByteBuffer.allocate(131072)
    private val writer = Mutex()
    private val budget = AtomicLong()
    @Volatile private var authenticated = false
    private var plain = byteArrayOf()
    private val gate = Any()
    private fun account(count: Int) { if (budget.addAndGet(count.toLong()) > if (authenticated) 1073741824L else 262144L) throw SSLException("Tunnel byte budget exhausted") }
    private fun tasks() { while (true) { val task = engine.delegatedTask ?: return; task.run() } }
    private suspend fun wrap(bytes: ByteBuffer) = writer.withLock {
        do {
            val output = ByteBuffer.allocate(65536)
            val result = synchronized(gate) { engine.wrap(bytes, output).also { tasks() } }
            if (result.status != SSLEngineResult.Status.OK) throw SSLException("TLS output closed or oversized")
            if (output.position() > 0) { account(output.position()); carrier.send(output.array().copyOf(output.position())) }
            if (result.bytesConsumed() == 0 && result.bytesProduced() == 0 && bytes.hasRemaining()) throw SSLException("TLS write stalled")
        } while (bytes.hasRemaining())
    }
    private suspend fun unwrap() {
        while (true) {
            if (network.position() == 0) append()
            val output = ByteBuffer.allocate(65536)
            network.flip()
            val result = synchronized(gate) { engine.unwrap(network, output).also { tasks() } }
            network.compact()
            when (result.status) {
                SSLEngineResult.Status.BUFFER_UNDERFLOW -> { append(); continue }
                SSLEngineResult.Status.BUFFER_OVERFLOW -> throw SSLException("TLS output exceeds bound")
                SSLEngineResult.Status.CLOSED -> throw EOFException("TLS peer closed")
                SSLEngineResult.Status.OK -> {
                    if (output.position() > 0) { require(plain.size + output.position() <= 2 * Frames.MEDIA_LIMIT); plain += output.array().copyOf(output.position()) }
                    if (result.handshakeStatus == SSLEngineResult.HandshakeStatus.NEED_WRAP) wrap(ByteBuffer.allocate(0))
                    return
                }
                else -> throw SSLException("Unknown TLS provider state")
            }
        }
    }
    private suspend fun append() { val bytes = carrier.receive(); account(bytes.size); if (bytes.isEmpty()) throw EOFException(); require(bytes.size <= network.remaining()); network.put(bytes) }
    suspend fun handshake() {
        engine.beginHandshake()
        while (true) when (engine.handshakeStatus) {
            SSLEngineResult.HandshakeStatus.NEED_TASK -> synchronized(gate) { tasks() }
            SSLEngineResult.HandshakeStatus.NEED_WRAP -> wrap(ByteBuffer.allocate(0))
            SSLEngineResult.HandshakeStatus.NEED_UNWRAP, SSLEngineResult.HandshakeStatus.NEED_UNWRAP_AGAIN -> unwrap()
            SSLEngineResult.HandshakeStatus.FINISHED, SSLEngineResult.HandshakeStatus.NOT_HANDSHAKING -> {
                if (engine.session.protocol == "TLSv1.2" && !(engine.session.cipherSuite.startsWith("TLS_ECDHE_") && engine.session.cipherSuite.contains("_GCM_"))) throw SSLException("TLS 1.2 requires ECDHE/GCM")
                authenticated = true; return
            }
            else -> throw SSLException("Unknown TLS handshake state")
        }
    }
    suspend fun writeFrame(bytes: ByteArray, limit: Int) { require(bytes.size in 1..limit); wrap(ByteBuffer.wrap(ByteBuffer.allocate(4).putInt(bytes.size).array() + bytes)) }
    suspend fun readFrame(limit: Int): ByteArray { val length = ByteBuffer.wrap(exactly(4)).int; require(length in 1..limit); return exactly(length) }
    private suspend fun exactly(count: Int): ByteArray { while (plain.size < count) unwrap(); val result = plain.copyOfRange(0, count); plain = plain.copyOfRange(count, plain.size); return result }
}
