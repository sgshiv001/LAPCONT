// LapCont — Tests — Nested screens return to their PC rather than unexpectedly exiting
package com.lapcont.mobile.presentation
import org.junit.Assert.assertEquals
import org.junit.Test
class AppNavigationTest {
    @Test fun pcFeaturesReturnToPcControls() { for(screen in listOf(AppScreen.LIVE, AppScreen.PROXIMITY, AppScreen.SETTINGS)) assertEquals(AppScreen.DETAILS, backDestination(screen)) }
    @Test fun pcControlsReturnToSavedPcs() { assertEquals(AppScreen.PCS, backDestination(AppScreen.DETAILS)) }
    @Test fun backClosesScannerBeforeLeavingPairing() { assertEquals(AppScreen.PAIRING, backDestination(AppScreen.PAIRING, scannerOpen = true)); assertEquals(AppScreen.PCS, backDestination(AppScreen.PAIRING)) }
}
