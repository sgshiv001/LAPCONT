// LapCont — Android — Real MediaCodec encode/decode probe with synthetic frames
// License: MIT
package com.lapcont.app.media
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.view.Surface
import kotlinx.coroutines.ensureActive
import kotlin.coroutines.coroutineContext

/** Runs on Dispatchers.IO. Owns both codecs; optional output Surface is owned by the caller. No PC feed is implied. */
object VideoProbe {
    suspend fun run(surface: Surface? = null): String {
        val encoder = MediaCodec.createEncoderByType("video/avc")
        var decoder: MediaCodec? = null
        var encoderStarted = false
        var decoderStarted = false
        try {
            val format = MediaFormat.createVideoFormat("video/avc", 64, 64).apply {
                setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible)
                setInteger(MediaFormat.KEY_BIT_RATE, 100000); setInteger(MediaFormat.KEY_FRAME_RATE, 30); setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
            }
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE); encoder.start(); encoderStarted = true
            val packets = ArrayList<Pair<ByteArray, Long>>()
            var outputFormat: MediaFormat? = null
            val info = MediaCodec.BufferInfo()
            var submitted = 0; var ended = false
            val deadline = android.os.SystemClock.elapsedRealtime() + 8000
            while (!ended && android.os.SystemClock.elapsedRealtime() < deadline) {
                coroutineContext.ensureActive()
                if (submitted <= 30) {
                    val index = encoder.dequeueInputBuffer(10000)
                    if (index >= 0) {
                        val size = if (submitted == 30) 0 else 64 * 64 * 3 / 2
                        encoder.getInputBuffer(index)!!.apply { clear(); if (size > 0) put(ByteArray(size) { if (it < 64 * 64) 96 else 128.toByte() }) }
                        encoder.queueInputBuffer(index, 0, size, submitted * 33333L, if (submitted == 30) MediaCodec.BUFFER_FLAG_END_OF_STREAM else 0)
                        submitted++
                    }
                }
                val index = encoder.dequeueOutputBuffer(info, 10000)
                if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) outputFormat = encoder.outputFormat
                else if (index >= 0) {
                    if (info.size > 0 && info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG == 0) {
                        val bytes = ByteArray(info.size); encoder.getOutputBuffer(index)!!.apply { position(info.offset); limit(info.offset + info.size); get(bytes) }
                        require(packets.size < 60 && bytes.size <= 1048576); packets.add(bytes to info.presentationTimeUs)
                    }
                    ended = info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
                    encoder.releaseOutputBuffer(index, false)
                }
            }
            check(ended && packets.isNotEmpty()) { "Encoder timed out" }
            decoder = MediaCodec.createDecoderByType("video/avc")
            decoder.configure(checkNotNull(outputFormat), surface, null, 0); decoder.start(); decoderStarted = true
            var sent = 0; var decoded = 0; ended = false
            val decodeDeadline = android.os.SystemClock.elapsedRealtime() + 8000
            while (!ended && android.os.SystemClock.elapsedRealtime() < decodeDeadline) {
                coroutineContext.ensureActive()
                if (sent <= packets.size) {
                    val index = decoder.dequeueInputBuffer(10000)
                    if (index >= 0) {
                        val packet = packets.getOrNull(sent)
                        decoder.getInputBuffer(index)!!.apply { clear(); if (packet != null) put(packet.first) }
                        decoder.queueInputBuffer(index, 0, packet?.first?.size ?: 0, packet?.second ?: 1_000_000, if (packet == null) MediaCodec.BUFFER_FLAG_END_OF_STREAM else 0); sent++
                    }
                }
                val index = decoder.dequeueOutputBuffer(info, 10000)
                if (index >= 0) {
                    if (info.size > 0 || (surface != null && info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM == 0)) decoded++
                    ended = info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
                    decoder.releaseOutputBuffer(index, surface != null)
                }
            }
            check(ended && decoded > 0) { "Decoder did not produce frames" }
            return "${encoder.name} → ${decoder.name}: ${packets.size} encoded, $decoded decoded synthetic frames"
        } finally {
            if (decoderStarted) decoder?.stop(); decoder?.release()
            if (encoderStarted) encoder.stop(); encoder.release()
        }
    }
}
