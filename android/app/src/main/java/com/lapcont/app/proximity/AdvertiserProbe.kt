// LapCont — Android — Foreground BLE advertising feasibility
// License: MIT
package com.lapcont.app.proximity
import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.le.*
import android.content.Context
import android.os.ParcelUuid
import java.security.SecureRandom
import java.util.UUID
import kotlinx.coroutines.*

/** Foreground-only test advertiser. No paired identities or production policy; close stops the advertisement. */
class AdvertiserProbe(private val context: Context) : AutoCloseable {
    private var advertiser: BluetoothLeAdvertiser? = null
    private var callback: AdvertiseCallback? = null
    @SuppressLint("MissingPermission")
    suspend fun run(): String {
        val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter ?: return "BLE_UNAVAILABLE: no adapter"
        if (!adapter.isEnabled) return "BLE_UNAVAILABLE: Bluetooth is off"
        if (!adapter.isMultipleAdvertisementSupported) return "BLE_UNAVAILABLE: advertiser unsupported"
        val actual = adapter.bluetoothLeAdvertiser ?: return "BLE_UNAVAILABLE: advertiser unavailable"
        advertiser = actual
        val ready = CompletableDeferred<Int>()
        val listener = object : AdvertiseCallback() {
            override fun onStartSuccess(settingsInEffect: AdvertiseSettings) { ready.complete(0) }
            override fun onStartFailure(errorCode: Int) { ready.complete(errorCode) }
        }
        callback = listener
        val beacon = ByteArray(16).also(SecureRandom()::nextBytes)
        // Legacy advertisement: flags 3 bytes + service data (length+type+UUID16+16 bytes) 20 = 23 <= 31.
        val data = AdvertiseData.Builder().setIncludeDeviceName(false).setIncludeTxPowerLevel(false)
            .addServiceData(ParcelUuid(UUID.fromString("0000fff0-0000-1000-8000-00805f9b34fb")), beacon).build()
        actual.startAdvertising(AdvertiseSettings.Builder().setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
            .setConnectable(false).setTimeout(10000).build(), data, listener)
        return try {
            val code = withTimeout(5000) { ready.await() }
            if (code != 0) "BLE_ADVERTISE_FAILED: $code" else { delay(8000); "Advertised a random test beacon for 8 seconds. PC observation is a separate check." }
        } finally { close() }
    }
    @SuppressLint("MissingPermission")
    override fun close() { callback?.let { advertiser?.stopAdvertising(it) }; callback = null; advertiser = null }
}
