// LapCont — Android tests — Real codecs and platform TLS, supplementary emulator evidence
// License: MIT
package com.lapcont.app
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lapcont.app.media.OpusProbe
import com.lapcont.app.media.VideoProbe
import com.lapcont.transport.interoperability
import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

/** Instrumented platform probes. No physical BLE/WAN/PC video claims follow from these tests. */
@RunWith(AndroidJUnit4::class)
class ProbeTests {
    @Test fun nativeOpusRoundTrip() {
        val result = OpusProbe.run(); Log.i("LapContPhase0", result)
        assertTrue(result.contains("960 samples")); assertTrue(result.contains("libopus"))
    }
    @Test fun realMediaCodecRoundTripWithSyntheticInput() = runBlocking {
        val result = VideoProbe.run(); Log.i("LapContPhase0", result)
        assertTrue(result.contains("30 encoded, 30 decoded synthetic frames"))
    }
    @Test fun androidPlatformTlsInteroperatesWithCSharp() = runBlocking {
        interoperability(fixture()) { Log.i("LapContPhase0", it) }
    }
    @Test fun androidRejectsWrongPcIdentity() = runBlocking {
        interoperability(fixture(), "bad-inner-pin") { Log.i("LapContPhase0", it) }
    }
    @Test fun androidRejectsMissingPhoneIdentity() = runBlocking {
        interoperability(fixture(), "no-client") { Log.i("LapContPhase0", it) }
    }
    @Test fun authenticatedChannelRejectsWrongContextPc() { rejectsContext("pc_id") }
    @Test fun authenticatedChannelRejectsWrongContextPhone() { rejectsContext("phone_id") }
    private fun rejectsContext(field: String): Unit = runBlocking {
        // A real positive exchange precedes the rejection so an offline host cannot satisfy this check.
        interoperability(fixture()) { Log.i("LapContPhase0", it) }
        var rejected = false
        try { interoperability(fixture().put(field, "wrong-context-identity")) { Log.i("LapContPhase0", it) } }
        catch (expected: IllegalStateException) { rejected = true }
        assertTrue("Authenticated context must match the intended $field", rejected)
        Log.i("LapContPhase0", "VERIFIED authenticated context rejection: $field")
    }
    private fun fixture(): JSONObject {
        val folder = InstrumentationRegistry.getInstrumentation().targetContext.cacheDir
        val file = File(folder, "fixture.json")
        check(file.exists()) { "Start the C# channel host and provision the temporary fixture; see docs/verification.md" }
        return JSONObject(file.readText()).put("client_identity", File(folder, "phone.p12").absolutePath)
    }
}
