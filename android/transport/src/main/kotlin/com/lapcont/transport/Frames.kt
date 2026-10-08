// LapCont — Transport — Shared network-byte-order frame codec
// License: MIT
package com.lapcont.transport
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Immutable media envelope; callers must not mutate its arrays while writing. Matches C# Frames. */
data class MediaFrame(val type: Int, val flags: Int, val streamId: ByteArray, val sequence: Long,
    val ptsMicroseconds: Long, val payload: ByteArray)

/** Stateless bounded codec. No capture, buffering, or device access. */
object Frames {
    const val CONTROL_LIMIT = 4096
    const val MEDIA_LIMIT = 1048576
    fun encode(frame: MediaFrame): ByteArray {
        require(frame.type in 1..5 && frame.flags in 0..3 && frame.streamId.size == 16)
        require(frame.sequence >= 0 && frame.ptsMicroseconds >= 0 && frame.payload.size <= MEDIA_LIMIT)
        return ByteBuffer.allocate(40 + frame.payload.size).order(ByteOrder.BIG_ENDIAN).apply {
            put(1); put(frame.type.toByte()); putShort(frame.flags.toShort()); put(frame.streamId)
            putLong(frame.sequence); putLong(frame.ptsMicroseconds); putInt(frame.payload.size); put(frame.payload)
        }.array()
    }
    fun decode(bytes: ByteArray): MediaFrame {
        require(bytes.size >= 40)
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.BIG_ENDIAN)
        require(buffer.get().toInt() == 1)
        val type = buffer.get().toInt() and 255; val flags = buffer.short.toInt() and 65535
        val id = ByteArray(16).also(buffer::get); val seq = buffer.long; val pts = buffer.long; val length = buffer.int
        require(type in 1..5 && flags in 0..3 && seq >= 0 && pts >= 0)
        require(length in 0..MEDIA_LIMIT && length == buffer.remaining())
        return MediaFrame(type, flags, id, seq, pts, ByteArray(length).also(buffer::get))
    }
    fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02X".format(it.toInt() and 255) }
    fun unhex(hex: String): ByteArray {
        require(hex.length % 2 == 0 && hex.all { it in "0123456789abcdefABCDEF" })
        return hex.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    }
}
