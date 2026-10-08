// LapCont — Android — Maintained libopus through generic JNA JNI
// License: MIT
package com.lapcont.app.media
import com.sun.jna.*
import com.sun.jna.ptr.IntByReference

/** Native libopus ABI binding. Each codec state belongs to one coroutine; no concurrent encode/decode calls. */
interface LibOpus : Library {
    fun opus_get_version_string(): String
    fun opus_encoder_create(rate: Int, channels: Int, application: Int, error: IntByReference): Pointer?
    fun opus_encoder_destroy(encoder: Pointer)
    fun opus_encoder_ctl(encoder: Pointer, request: Int, value: Int): Int
    fun opus_encode(encoder: Pointer, pcm: ShortArray, frameSize: Int, packet: ByteArray, max: Int): Int
    fun opus_decoder_create(rate: Int, channels: Int, error: IntByReference): Pointer?
    fun opus_decoder_destroy(decoder: Pointer)
    fun opus_decode(decoder: Pointer, packet: ByteArray, length: Int, pcm: ShortArray, frameSize: Int, fec: Int): Int
}

/** Synthetic silence packet test; does not open a microphone or speaker and never records media. */
object OpusProbe {
    fun run(): String {
        val opus = Native.load("opus", LibOpus::class.java)
        val error = IntByReference()
        val encoder = opus.opus_encoder_create(48000, 1, 2048, error) ?: error("Opus encoder failed ${error.value}")
        var decoder: Pointer? = null
        try {
            check(opus.opus_encoder_ctl(encoder, 4002, 64000) == 0)
            decoder = opus.opus_decoder_create(48000, 1, error) ?: error("Opus decoder failed ${error.value}")
            val packet = ByteArray(1275)
            val length = opus.opus_encode(encoder, ShortArray(960), 960, packet, packet.size)
            check(length in 1..1275) { "Opus encode failed $length" }
            val samples = opus.opus_decode(decoder, packet, length, ShortArray(960), 960, 0)
            check(samples == 960) { "Opus decode failed $samples" }
            return "${opus.opus_get_version_string()}: 960 synthetic samples → $length bytes → $samples samples"
        } finally { decoder?.let(opus::opus_decoder_destroy); opus.opus_encoder_destroy(encoder) }
    }
}
