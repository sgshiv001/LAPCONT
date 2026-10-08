// LapCont — Android — Rotating enrolled BLE identifiers with visible opt-in monitoring
// License: MIT
package com.lapcont.mobile.proximity
import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.le.*
import android.content.Context
import android.os.ParcelUuid
import com.lapcont.mobile.data.*
import com.lapcont.transport.*
import kotlinx.coroutines.*
import java.nio.ByteBuffer
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/** One validated advertising slot; simultaneous PC proximity is explicitly unsupported until hardware timing is measured. */
class PhoneAdvertiser(private val context: Context, private val identity: IdentityStore) {
    private var job: Job? = null
    val isActive: Boolean get() = job?.isActive == true
    fun stop() { job?.cancel() }
    @SuppressLint("MissingPermission")
    fun start(scope: CoroutineScope, pcs: List<PairedPc>, status: (String) -> Unit) {
        val previous = job; previous?.cancel()
        job = scope.launch(Dispatchers.IO) {
            // Release the prior hardware slot before starting its replacement.
            previous?.join()
            ensureActive()
            if (pcs.isEmpty()) { status("Proximity advertising paused · no enabled PC with proximity permission"); return@launch }
            if(pcs.size>1) { status("Enable proximity for one PC at a time on this build. Multiple PC advertising needs hardware validation."); return@launch }
            var advertiser: BluetoothLeAdvertiser? = null; var callback: AdvertiseCallback? = null
            try {
                val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter ?: error("No BLE adapter")
                check(adapter.isEnabled && adapter.isMultipleAdvertisementSupported) { "BLE advertising unavailable" }
                advertiser = adapter.bluetoothLeAdvertiser ?: error("BLE advertising unavailable")
                var index = 0
                while (isActive) {
                    val pc = pcs[index++ % pcs.size]; val secret = Frames.unhex(StrictJson.parse(identity.secret(pc.id)).getString("proximity_key")); val epoch = System.currentTimeMillis() / 30000
                    val mac = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(secret, "HmacSHA256")) }.doFinal("LPC1.proximity:${pc.id}:${identity.phoneId}:$epoch".toByteArray())
                    val payload = ByteBuffer.allocate(16).putInt(epoch.toInt()).put(mac, 0, 12).array(); val started = CompletableDeferred<Unit>()
                    callback = object : AdvertiseCallback() { override fun onStartSuccess(settings: AdvertiseSettings) { started.complete(Unit) }; override fun onStartFailure(code: Int) { started.completeExceptionally(IllegalStateException("BLE advertising failed $code")) } }
                    val settings = AdvertiseSettings.Builder().setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY).setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_MEDIUM).setConnectable(false).build()
                    val data = AdvertiseData.Builder().setIncludeDeviceName(false).setIncludeTxPowerLevel(false).addServiceData(ParcelUuid.fromString("0000fff0-0000-1000-8000-00805f9b34fb"), payload).build()
                    advertiser.startAdvertising(settings, data, callback); withTimeout(3000) { started.await() }; status("Proximity advertising active · ${pcs.size} PC(s)")
                    delay(if (pcs.size == 1) 10000 else 1000); advertiser.stopAdvertising(callback); callback = null
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) { status("Proximity unavailable (${e.javaClass.simpleName}); manual control remains available") }
            finally { callback?.let { advertiser?.stopAdvertising(it) } }
        }
    }
}
