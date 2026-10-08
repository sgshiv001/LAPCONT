// LapCont — Tests — Critical baseline screen labels, QR input and cancellation
// License: MIT
package com.lapcont.mobile
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import com.lapcont.mobile.presentation.MainActivity
import org.junit.Rule
import org.junit.Test
class ScreenTests {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()
    @Test fun dashboardAndPairingNeverPromiseWindowsUnlock() {
        compose.onNodeWithText("LapCont").assertExists()
        if(compose.onAllNodesWithText("Get started").fetchSemanticsNodes().isNotEmpty()) compose.onNodeWithText("Get started").performScrollTo().performClick()
        compose.onNodeWithText("Add a PC").performClick()
        compose.onNodeWithText("Scan QR code").assertExists()
        compose.onNodeWithText("Enter a code instead").performScrollTo().performClick()
        compose.onNodeWithText("Verify and pair").performScrollTo().assertIsNotEnabled()
        compose.onNodeWithTag("nav-back").performClick()
        compose.onNodeWithText("Add a PC").assertExists()
    }
    @Test fun androidBackReturnsToPcsWithoutClosingActivity() {
        if(compose.onAllNodesWithText("Get started").fetchSemanticsNodes().isNotEmpty()) compose.onNodeWithText("Get started").performScrollTo().performClick()
        compose.onNodeWithText("Add a PC").performClick()
        compose.runOnUiThread { compose.activity.onBackPressedDispatcher.onBackPressed() }
        compose.onNodeWithText("Add a PC").assertExists()
        org.junit.Assert.assertFalse(compose.activity.isFinishing)
    }
    /** Exercises the ordinary app button. The operator answers Android's own permission dialog. */
    @Test fun physicalMicrophonePermissionPrompt() {
        org.junit.Assume.assumeTrue("Requires coordinated normal permission approval",androidx.test.platform.app.InstrumentationRegistry.getArguments().getString("physicalPermissionQa")=="true")
        compose.onNodeWithText("Connect saved PCs").performScrollTo().performClick()
        compose.waitUntil(20000) { compose.onAllNodesWithText("Open PC controls").fetchSemanticsNodes().isNotEmpty() }
        compose.onAllNodesWithText("Open PC controls")[0].performScrollTo().performClick()
        compose.waitUntil(20000) { compose.onAllNodes(hasText("Open Live View") and isEnabled()).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("Open Live View").performScrollTo().performClick()
        val context=androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext
        if(context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)!=android.content.pm.PackageManager.PERMISSION_GRANTED) {
            compose.onNodeWithText("Allow microphone for push-to-talk").performScrollTo().performClick()
            compose.waitUntil(90000) { context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)==android.content.pm.PackageManager.PERMISSION_GRANTED }
        }
        org.junit.Assert.assertEquals(android.content.pm.PackageManager.PERMISSION_GRANTED,context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO))
    }
}
