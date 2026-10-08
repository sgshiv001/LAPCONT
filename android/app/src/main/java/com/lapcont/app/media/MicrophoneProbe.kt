// LapCont — Android — Explicit finite AudioRecord/Opus and AudioTrack probe
// License: MIT
package com.lapcont.app.media
import android.annotation.SuppressLint
import android.media.*
import android.os.SystemClock
import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.ptr.IntByReference
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlin.coroutines.coroutineContext

/** Runs on Dispatchers.IO after microphone permission. Always releases audio devices/codecs; captures no persistent media. */
object MicrophoneProbe {
    @SuppressLint("MissingPermission")
    suspend fun run(): String {
        val minimum = AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        require(minimum > 0) { "48 kHz microphone mode unavailable" }
        val recorder = AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION, 48000,
            AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(minimum * 2, 3840))
        val opus = Native.load("opus", LibOpus::class.java)
        var encoder: Pointer? = null; var decoder: Pointer? = null
        try {
            check(recorder.state == AudioRecord.STATE_INITIALIZED)
            val error = IntByReference()
            encoder = opus.opus_encoder_create(48000, 1, 2048, error) ?: error("Encoder unavailable ${error.value}")
            decoder = opus.opus_decoder_create(48000, 1, error) ?: error("Decoder unavailable ${error.value}")
            check(opus.opus_encoder_ctl(encoder, 4002, 64000) == 0)
            val pcm = ShortArray(960); val decoded = ShortArray(960); val packet = ByteArray(1275)
            var offset = 0; var packets = 0
            recorder.startRecording()
            val deadline = SystemClock.elapsedRealtime() + 1500
            while (SystemClock.elapsedRealtime() < deadline) {
                coroutineContext.ensureActive()
                val count = recorder.read(pcm, offset, 960 - offset, AudioRecord.READ_NON_BLOCKING)
                check(count >= 0) { "AudioRecord failed $count" }
                offset += count
                if (offset == 960) {
                    val bytes = opus.opus_encode(encoder, pcm, 960, packet, packet.size)
                    check(bytes in 1..1275)
                    check(opus.opus_decode(decoder, packet, bytes, decoded, 960, 0) == 960)
                    offset = 0; packets++
                } else delay(2)
            }
            check(packets > 0) { "No microphone packets received" }
            // Check playback API with generated silence, never play the microphone back (feedback).
            val playbackMinimum = AudioTrack.getMinBufferSize(48000, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT)
            require(playbackMinimum > 0)
            val track = AudioTrack.Builder().setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build())
                .setAudioFormat(AudioFormat.Builder().setSampleRate(48000).setChannelMask(AudioFormat.CHANNEL_OUT_MONO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build())
                .setBufferSizeInBytes(maxOf(playbackMinimum * 2, 3840)).setTransferMode(AudioTrack.MODE_STREAM).build()
            try { check(track.state == AudioTrack.STATE_INITIALIZED); track.play(); check(track.write(ShortArray(960), 0, 960, AudioTrack.WRITE_NON_BLOCKING) >= 0) }
            finally { try { track.stop() } finally { track.release() } }
            return "AudioRecord 48 kHz mono → libopus: $packets packets. AudioTrack accepted synthetic silence; audible output unverified."
        } finally {
            try { if (recorder.recordingState == AudioRecord.RECORDSTATE_RECORDING) recorder.stop() }
            finally { recorder.release(); decoder?.let(opus::opus_decoder_destroy); encoder?.let(opus::opus_encoder_destroy) }
        }
    }
}
