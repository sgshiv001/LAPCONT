// LapCont — Android tests — Honest probe labels and local stop affordance
// License: MIT
package com.lapcont.app
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

/** UI state assertions use the real activity; no mocked PC connection or media is shown. */
class ProbeUiTests {
    @get:Rule val compose = createAndroidComposeRule<MainActivity>()
    @Test fun phaseAndStopAreVisible() {
        compose.onNodeWithText("LapCont / Phase 0").assertIsDisplayed()
        compose.onNodeWithText("Stop all probes").assertIsDisplayed().assertHasClickAction().performClick()
        compose.onNodeWithText("No pairing or remote control yet. Media shown here is a generated codec test pattern. No camera/audio recording.").assertIsDisplayed()
        compose.waitForIdle()
        // Semantics passed even when API 29 SurfaceView composition hid all controls.
        // Inspect the actual composed display, including its separate Surface layers.
        val bounds = compose.onNodeWithText("Stop all probes").fetchSemanticsNode().boundsInWindow
        val screenshot = checkNotNull(InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot())
        try {
            val left = bounds.left.toInt().coerceIn(0, screenshot.width - 1)
            val top = bounds.top.toInt().coerceIn(0, screenshot.height - 1)
            val width = (bounds.right.toInt() - left).coerceIn(1, screenshot.width - left)
            val height = (bounds.bottom.toInt() - top).coerceIn(1, screenshot.height - top)
            val pixels = IntArray(width * height)
            screenshot.getPixels(pixels, 0, width, left, top, width, height)
            assertTrue("Stop control must render text, rather than a uniform blank Surface", pixels.map { it and 0xFFFFFF }.distinct().size > 1)
        } finally { screenshot.recycle() }
    }
}
