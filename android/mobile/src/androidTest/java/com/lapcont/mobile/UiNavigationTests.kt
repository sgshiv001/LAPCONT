// LapCont — QA — Disposable emulator metadata exercises native navigation, never real pairing/media
// License: MIT
package com.lapcont.mobile

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.platform.app.InstrumentationRegistry
import com.lapcont.mobile.data.*
import com.lapcont.mobile.presentation.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*

class UiNavigationTests {
    @get:Rule val ui = createAndroidComposeRule<MainActivity>()
    private lateinit var vm:DashboardViewModel
    private lateinit var dao:PcDao
    private val id = "00000000000000000000000000000001"
    @Before fun setup() = runBlocking {
        Assume.assumeTrue("Disposable emulator only", InstrumentationRegistry.getArguments().getString("uiEmulatorQa") == "true")
        ui.runOnUiThread { vm = ViewModelProvider(ui.activity)[DashboardViewModel::class.java] }
        dao = PcRepository::class.java.getDeclaredField("dao").let { it.isAccessible = true; it.get(vm.repository) as PcDao }
        dao.save(PairedPc(id, "Demo PC", "127.0.0.1", 4433, "0".repeat(64), "S-1-0-0", 31))
        ui.waitUntil(10000) { vm.pcs.value.any { it.id == id } }
        if (ui.onAllNodesWithText("Get started").fetchSemanticsNodes().isNotEmpty()) click("Get started")
    }
    @After fun cleanup() = runBlocking {
        if (::dao.isInitialized) { vm.repository.monitoringStop(); dao.remove(id) }
    }
    private fun click(text:String) { ui.onNodeWithText(text).performScrollTo().performClick() }
    private fun back() { ui.runOnUiThread { ui.activity.onBackPressedDispatcher.onBackPressed() }; ui.waitForIdle() }
    private fun open() { click("Open PC controls"); ui.onNodeWithText("PC controls").assertExists() }
    private fun capture(name:String) {
        if(InstrumentationRegistry.getArguments().getString("captureUi") != "true") return
        ui.waitForIdle()
        InstrumentationRegistry.getInstrumentation().waitForIdleSync()
        android.os.SystemClock.sleep(350) // Let the native compositor present the completed transition.
        val bitmap = InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()
        java.io.File(InstrumentationRegistry.getInstrumentation().targetContext.cacheDir, "ui-$name.png").outputStream().use { bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
        bitmap.recycle()
    }
    @Test fun nestedFeaturesAndRemovalCancelKeepPairing() {
        open(); capture("details"); click("Nearby protection"); capture("proximity"); ui.onNodeWithText("Query calibration").performScrollTo().assertIsNotEnabled()
        back(); ui.onNodeWithText("PC controls").assertExists()
        click("Connection settings"); capture("settings"); click("Remove this PC"); ui.onNodeWithText("Keep PC").performClick()
        assertTrue(vm.pcs.value.any { it.id == id })
        ui.onNodeWithTag("nav-back").performClick(); ui.onNodeWithText("PC controls").assertExists()
        back(); ui.onNodeWithText("Add a PC").assertExists(); assertFalse(ui.activity.isFinishing)
    }
    @Test fun detailsAndBackSurviveActivityRecreation() {
        open(); click("Connection settings")
        ui.activityRule.scenario.recreate(); ui.onNodeWithText("Connection settings").assertExists()
        back(); ui.onNodeWithText("PC controls").assertExists(); back(); ui.onNodeWithText("Add a PC").assertExists()
    }
    @Test fun liveViewBackReturnsToControlsAndClearsLocalMedia() {
        // Verified UI fixture enables the screen. No clients, keys, connections or live packets are created.
        @Suppress("UNCHECKED_CAST")
        val states = PcRepository::class.java.getDeclaredField("mutable").let { it.isAccessible = true; it.get(vm.repository) as MutableStateFlow<Map<String,PcStatus>> }
        states.value = mapOf(id to PcStatus(true, "QA layout fixture", "UI fixture", JSONObject("""{"grants":31,"sessions":[{"windows_session_id":1,"agent_available":true,"lock_state":"unlocked"}]}""")))
        open(); click("Open Live View"); ui.onNodeWithText("Stop live view").assertExists(); capture("live")
        back(); ui.onNodeWithText("PC controls").assertExists(); assertNull(vm.live.value)
    }
    @Test fun scannerBackClosesScannerBeforeLeavingPairing() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand("pm grant ${context.packageName} android.permission.CAMERA").close()
        click("Add a PC"); click("Scan QR code"); ui.onNodeWithText("Close scanner").assertExists()
        back(); ui.onNodeWithText("Close scanner").assertDoesNotExist(); ui.onNodeWithText("Scan QR code").assertExists()
        back(); ui.onNodeWithText("Add a PC").assertExists()
    }
}
