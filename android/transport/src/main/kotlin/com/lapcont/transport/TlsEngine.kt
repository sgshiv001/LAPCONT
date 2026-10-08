// LapCont — Transport — Standard platform TLS engine over an opaque carrier
// License: MIT
package com.lapcont.transport

import java.io.EOFException
import java.nio.ByteBuffer
import javax.net.ssl.SSLEngine
import javax.net.ssl.SSLEngineResult
import javax.net.ssl.SSLException

/** Bounded asynchronous byte transport implemented by the WebSocket adapter. */
interface Carrier { suspend fun receive(): ByteArray; suspend fun send(bytes: ByteArray) }

/** Single-coroutine TLS owner. Calls must be serialized; handshake/records are handled by the OS/JVM provider. */
class TlsEngine(private val engine: SSLEngine, private val carrier: Carrier) {
    private val network = ByteBuffer.allocate(65536)
    private val output = ByteBuffer.allocate(65536)
    private val application = ByteBuffer.allocate(65536)
    private val empty = ByteBuffer.allocate(0)
    private var plaintext = byteArrayOf()
    val protocol: String get() = engine.session.protocol

    suspend fun handshake() {
        engine.beginHandshake()
        while (true) {
            when (engine.handshakeStatus.name) {
                "NEED_TASK" -> tasks()
                "NEED_WRAP" -> wrap(empty)
                "NEED_UNWRAP", "NEED_UNWRAP_AGAIN" -> unwrap()
                "FINISHED", "NOT_HANDSHAKING" -> return
                else -> throw SSLException("Unsupported TLS handshake state")
            }
        }
    }
    private fun tasks() { while (true) { val task = engine.delegatedTask ?: return; task.run() } }
    private suspend fun wrap(input: ByteBuffer) {
        output.clear()
        val result = engine.wrap(input, output)
        if (result.status != SSLEngineResult.Status.OK) throw SSLException("TLS wrap ${result.status}")
        if (output.position() > 0) carrier.send(output.array().copyOf(output.position()))
        if (result.handshakeStatus == SSLEngineResult.HandshakeStatus.NEED_TASK) tasks()
    }
    private suspend fun unwrap() {
        while (true) {
            if (network.position() == 0) append()
            network.flip(); application.clear()
            val result = engine.unwrap(network, application); network.compact()
            when (result.status) {
                SSLEngineResult.Status.BUFFER_UNDERFLOW -> { append(); continue }
                SSLEngineResult.Status.BUFFER_OVERFLOW -> throw SSLException("TLS plaintext exceeds buffer")
                SSLEngineResult.Status.CLOSED -> throw EOFException("TLS peer closed")
                SSLEngineResult.Status.OK -> {
                    if (application.position() > 0) {
                        require(plaintext.size + application.position() <= 2 * Frames.MEDIA_LIMIT)
                        plaintext += application.array().copyOf(application.position())
                    }
                    if (result.handshakeStatus == SSLEngineResult.HandshakeStatus.NEED_TASK) tasks()
                    if (result.handshakeStatus == SSLEngineResult.HandshakeStatus.NEED_WRAP) wrap(empty)
                    return
                }
                else -> throw SSLException("TLS state missing")
            }
        }
    }
    private suspend fun append() {
        val bytes = carrier.receive()
        if (bytes.isEmpty()) throw EOFException("Carrier closed")
        if (bytes.size > network.remaining()) throw SSLException("TLS carrier input exceeds bound")
        network.put(bytes)
    }
    suspend fun writeFrame(bytes: ByteArray, limit: Int) {
        require(bytes.isNotEmpty() && bytes.size <= limit)
        val source = ByteBuffer.wrap(ByteBuffer.allocate(4).putInt(bytes.size).array() + bytes)
        while (source.hasRemaining()) wrap(source)
    }
    suspend fun readFrame(limit: Int): ByteArray {
        val header = readExactly(4); val length = ByteBuffer.wrap(header).int
        require(length in 1..limit) { "Invalid plaintext frame size" }
        return readExactly(length)
    }
    private suspend fun readExactly(count: Int): ByteArray {
        while (plaintext.size < count) unwrap()
        val result = plaintext.copyOfRange(0, count); plaintext = plaintext.copyOfRange(count, plaintext.size)
        return result
    }
}
