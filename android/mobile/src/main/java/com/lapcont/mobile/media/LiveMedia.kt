// LapCont — Android — Bounded real MediaCodec/AudioTrack receiver and push-to-talk capture
// License: MIT
package com.lapcont.mobile.media

import android.annotation.SuppressLint
import android.content.Context
import android.media.*
import android.os.SystemClock
import android.view.Surface
import com.lapcont.transport.*
import com.sun.jna.*
import com.sun.jna.ptr.IntByReference
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import org.json.JSONObject
import java.nio.ByteBuffer

/** Native libopus ABI. Each encoder/decoder belongs to one worker and is always destroyed. */
interface LibOpus : Library {
    fun opus_encoder_create(rate: Int, channels: Int, application: Int, error: IntByReference): Pointer?
    fun opus_encoder_destroy(state: Pointer)
    fun opus_encoder_ctl(state: Pointer, request: Int, value: Int): Int
    fun opus_encode(state: Pointer, pcm: ShortArray, samples: Int, packet: ByteArray, maximum: Int): Int
    fun opus_decoder_create(rate: Int, channels: Int, error: IntByReference): Pointer?
    fun opus_decoder_destroy(state: Pointer)
    fun opus_decode(state: Pointer, packet: ByteArray, length: Int, pcm: ShortArray, samples: Int, fec: Int): Int
}

/** Repository-owned media state. Queues fail closed when full; Surface ownership remains with Compose. */
class LiveMedia(private val context: Context, private val scope: CoroutineScope, private val failed: (String) -> Unit) {
    data class Statistics(val decodedAudioPackets: Long, val renderedVideoFrames: Long, val sentTalkPackets: Long)
    private val audioPackets = java.util.concurrent.atomic.AtomicLong()
    private val videoFrames = java.util.concurrent.atomic.AtomicLong()
    private val talkPackets = java.util.concurrent.atomic.AtomicLong()
    private data class Incoming(val frames:Channel<MediaFrame>,val bytes:java.util.concurrent.atomic.AtomicInteger=java.util.concurrent.atomic.AtomicInteger())
    fun statistics() = Statistics(audioPackets.get(), videoFrames.get(), talkPackets.get())
    private var stoppingReceiver: Job? = null
    private var stoppingRecorder: Job? = null
    @Volatile var surface: Surface? = null
    @Volatile var mutedForTalk = false
    private var stream: String? = null
    private var receiver: Job? = null
    @Volatile private var queue: Incoming? = null
    private var ready: CompletableDeferred<Unit>? = null
    private var recorder: Job? = null
    private val audioManager = context.getSystemService(AudioManager::class.java)
    private val focus = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT).setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build())
        .setOnAudioFocusChangeListener { if (it <= AudioManager.AUDIOFOCUS_LOSS_TRANSIENT) failed("Audio interrupted; start a new stream when ready") }.build()
    private val routes = object : AudioDeviceCallback() { override fun onAudioDevicesRemoved(devices: Array<out AudioDeviceInfo>) { if (receiver != null || recorder != null) failed("Audio route changed; start again on the selected device") } }
    fun start(id: String, video: Boolean = false) {
        stop(); stream = id; val incoming = Incoming(Channel<MediaFrame>(64)); queue = incoming
        val initialized = CompletableDeferred<Unit>(); ready = initialized
        val previous = stoppingReceiver
        receiver = scope.launch(Dispatchers.IO) {
            previous?.join(); audioManager.registerAudioDeviceCallback(routes, null)
            var focusOwned = false
            var codec: MediaCodec? = null; var started = false; var track: AudioTrack? = null; var decoder: Pointer? = null
            var metadata: JSONObject? = null; var sps: ByteArray? = null; var pps: ByteArray? = null; var gotIdr = false
            var opus: LibOpus? = null; val pcm = ShortArray(960)
            val jitter = java.util.ArrayDeque<Pair<ByteArray, Long>>()
            var originPts: Long? = null; var originTime = 0L
            val info = MediaCodec.BufferInfo()
            try {
                opus = Native.load("opus", LibOpus::class.java)
                if(video) { check(surface?.isValid==true) { "Video surface is unavailable" }; codec=MediaCodec.createDecoderByType("video/avc") }
                initialized.complete(Unit)
                for (frame in incoming.frames) {
                    incoming.bytes.addAndGet(-frame.payload.size)
                    ensureActive(); if (frame.flags and 2 != 0) { gotIdr = false; jitter.clear(); originPts = null; if (started) codec?.flush() }
                    when (frame.type) {
                        5 -> { metadata = StrictJson.parse(frame.payload); require(metadata.getInt("sample_rate") == 48000 && metadata.getInt("channels") == 1 && metadata.getInt("opus_frame_samples") == 960) }
                        4 -> { for (nal in nals(frame.payload)) when (nal[0].toInt() and 31) { 7 -> sps = byteArrayOf(0, 0, 0, 1) + nal; 8 -> pps = byteArrayOf(0, 0, 0, 1) + nal } }
                        1 -> {
                            val format = metadata ?: error("Video before negotiated metadata"); require(format.getBoolean("video")); val idr = nals(frame.payload).any { it[0].toInt() and 31 == 5 }
                            if (!gotIdr && !idr) continue
                            if (!started) {
                                val w = format.getInt("width"); val h = format.getInt("height"); require(w in listOf(640, 1280) && h in listOf(480, 720))
                                val output = surface?.takeIf { it.isValid } ?: error("Video surface is unavailable")
                                val f = MediaFormat.createVideoFormat("video/avc", w, h).apply { setByteBuffer("csd-0", ByteBuffer.wrap(sps ?: error("Missing SPS"))); setByteBuffer("csd-1", ByteBuffer.wrap(pps ?: error("Missing PPS"))); setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, Frames.MEDIA_LIMIT) }
                                codec = codec ?: MediaCodec.createDecoderByType("video/avc"); codec.configure(f, output, null, 0); codec.start(); started = true
                            }
                            gotIdr = true
                            val current=codec ?: error("Video decoder unavailable")
                            if (originPts == null) { originPts = frame.ptsMicroseconds; originTime = SystemClock.elapsedRealtimeNanos() / 1000 + 60000 }
                            // TRY_AGAIN_LATER is normal while the platform starts or recycles buffers.
                            // Drain output while waiting, keep dependent input intact, and bound stalls.
                            val deadline = SystemClock.elapsedRealtime() + 500
                            var input: Int
                            do {
                                ensureActive(); drain(current, info, originPts!!, originTime)
                                input = current.dequeueInputBuffer(10000)
                                if (input < 0) {
                                    check(SystemClock.elapsedRealtime() < deadline) { "Video decoder stalled" }
                                    delay(2)
                                }
                            } while (input < 0)
                            current.getInputBuffer(input)!!.apply { clear(); require(remaining() >= frame.payload.size); put(frame.payload) }
                            current.queueInputBuffer(input, 0, frame.payload.size, frame.ptsMicroseconds, 0); drain(current, info, originPts!!, originTime)
                        }
                        2 -> {
                            require(metadata?.getBoolean("audio") == true && frame.payload.size in 1..1275)
                            if (track == null) {
                                check(audioManager.requestAudioFocus(focus) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED) { "Audio focus unavailable" }; focusOwned = true
                                val minimum = AudioTrack.getMinBufferSize(48000, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT); require(minimum > 0)
                                track = AudioTrack.Builder().setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build())
                                    .setAudioFormat(AudioFormat.Builder().setSampleRate(48000).setChannelMask(AudioFormat.CHANNEL_OUT_MONO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build())
                                    .setBufferSizeInBytes(maxOf(minimum * 2, 7680)).setTransferMode(AudioTrack.MODE_STREAM).build()
                                check(track.state == AudioTrack.STATE_INITIALIZED); val error = IntByReference(); decoder = opus.opus_decoder_create(48000, 1, error) ?: error("Opus decoder unavailable")
                                track.play()
                            }
                            jitter.add(frame.payload to frame.ptsMicroseconds)
                            if (originPts == null) { originPts = frame.ptsMicroseconds; originTime = SystemClock.elapsedRealtimeNanos() / 1000 + 60000 }
                            if (jitter.size >= 3) {
                                val (bytes, pts) = jitter.removeFirst(); val wait = originTime + pts - originPts!! - SystemClock.elapsedRealtimeNanos() / 1000
                                if (wait > 0) delay(minOf(wait / 1000, 80))
                                check(opus.opus_decode(decoder!!, bytes, bytes.size, pcm, 960, 0) == 960)
                                audioPackets.incrementAndGet()
                                if (!mutedForTalk) { var offset = 0; while (offset < 960) { ensureActive(); val count = track.write(pcm, offset, 960 - offset, AudioTrack.WRITE_NON_BLOCKING); check(count >= 0); offset += count; if (count == 0) delay(2) } }
                                else { track.pause(); track.flush(); track.play() }
                                if (wait < -100000) { jitter.clear(); originPts = null }
                            }
                            require(jitter.size <= 4)
                        }
                        else -> error("Unauthorized media direction")
                    }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) { initialized.completeExceptionally(e); android.util.Log.w("LapCont","Media failure ${e.javaClass.simpleName}: ${e.message?.take(120)}"); failed("Media stopped (${e.javaClass.simpleName}); start again when ready") }
            catch (e: LinkageError) { initialized.completeExceptionally(e); failed("Native audio codec unavailable on this device") }
            finally {
                initialized.cancel()
                try { if (started) codec?.stop() } finally { codec?.release() }
                try { track?.stop() } finally { track?.release(); decoder?.let { opus?.opus_decoder_destroy(it) } }
                if (focusOwned) { audioManager.abandonAudioFocusRequest(focus); focusOwned = false }; audioManager.unregisterAudioDeviceCallback(routes)
            }
        }
    }
    suspend fun awaitReady() { withTimeout(5000) { (ready ?: error("Stream was stopped while starting")).await() } }
    suspend fun frame(frame: MediaFrame) {
        if (Frames.hex(frame.streamId).lowercase() != stream) return
        val target = queue ?: return
        val bytes = target.bytes.addAndGet(frame.payload.size)
        if (bytes > 4194304 || !target.frames.trySend(frame).isSuccess) { target.bytes.addAndGet(-frame.payload.size); failed("Live receiver is too slow; stream stopped to preserve decoder dependencies"); stop() }
    }
    fun stop() { receiver?.let { it.cancel(); stoppingReceiver = it }; receiver = null; ready?.cancel(); ready = null; queue?.frames?.cancel(); queue = null; stream = null; stopTalk() }
    suspend fun awaitStopped() { stoppingReceiver?.join(); stoppingRecorder?.join() }
    @SuppressLint("MissingPermission")
    fun startTalk(id: String, send: suspend (MediaFrame) -> Unit) {
        stopTalk(); mutedForTalk = true
        val previous = stoppingRecorder
        recorder = scope.launch(Dispatchers.IO) {
            previous?.join(); mutedForTalk = true
            var record: AudioRecord? = null; var opus: LibOpus? = null; var encoder: Pointer? = null
            try {
                val minimum = AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT); require(minimum > 0)
                record = AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION, 48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(minimum * 2, 3840))
                opus = Native.load("opus", LibOpus::class.java)
                check(record.state == AudioRecord.STATE_INITIALIZED); encoder = opus.opus_encoder_create(48000, 1, 2048, IntByReference()) ?: error("Encoder unavailable"); check(opus.opus_encoder_ctl(encoder, 4002, 64000) == 0)
                val pcm = ShortArray(960); val packet = ByteArray(1275); var offset = 0; var seq = 0L; val origin = SystemClock.elapsedRealtimeNanos()
                record.startRecording()
                while (isActive) {
                    val count = record.read(pcm, offset, 960 - offset, AudioRecord.READ_NON_BLOCKING); check(count >= 0); offset += count
                    if (offset == 960) { val length = opus.opus_encode(encoder, pcm, 960, packet, packet.size); require(length in 1..1275); send(MediaFrame(3, 0, Frames.unhex(id), seq++, (SystemClock.elapsedRealtimeNanos() - origin) / 1000, packet.copyOf(length))); talkPackets.incrementAndGet(); offset = 0 }
                    else delay(2)
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) { failed("Talk-back stopped (${e.javaClass.simpleName})") }
            catch (e: LinkageError) { failed("Native talk-back codec unavailable on this device") }
            finally { try { if (record?.recordingState == AudioRecord.RECORDSTATE_RECORDING) record.stop() } finally { record?.release(); encoder?.let { opus?.opus_encoder_destroy(it) }; mutedForTalk = false } }
        }
    }
    fun stopTalk() { recorder?.let { it.cancel(); stoppingRecorder = it }; recorder = null; mutedForTalk = false }
    private fun drain(codec: MediaCodec, info: MediaCodec.BufferInfo, originPts: Long, originTime: Long) {
        while (true) {
            val i = codec.dequeueOutputBuffer(info, 0)
            if (i == MediaCodec.INFO_TRY_AGAIN_LATER) return
            if (i < 0) continue // Format/buffer change notifications do not end output draining.
            if (surface?.isValid == true) {
                val now = SystemClock.elapsedRealtimeNanos(); val target = (originTime + info.presentationTimeUs - originPts) * 1000
                codec.releaseOutputBuffer(i, maxOf(now, minOf(target, now + 80000000))); videoFrames.incrementAndGet()
            } else codec.releaseOutputBuffer(i, false)
        }
    }
    private fun nals(bytes: ByteArray): List<ByteArray> {
        val starts = mutableListOf<Pair<Int, Int>>(); var i = 0
        while (i + 3 < bytes.size) { if (bytes[i] == 0.toByte() && bytes[i + 1] == 0.toByte()) { if (bytes[i + 2] == 1.toByte()) { starts += i to (i + 3); i += 3; continue }; if (bytes[i + 2] == 0.toByte() && bytes[i + 3] == 1.toByte()) { starts += i to (i + 4); i += 4; continue } }; i++ }
        return starts.mapIndexedNotNull { n, s -> val end = starts.getOrNull(n + 1)?.first ?: bytes.size; if (s.second < end) bytes.copyOfRange(s.second, end) else null }
    }
}
