// LapCont — Android — One back path for the toolbar and system gesture
// License: MIT
package com.lapcont.mobile.presentation

enum class AppScreen(val title: String) {
    WELCOME("Welcome"), PCS("Your PCs"), PAIRING("Add a PC"), DETAILS("PC controls"),
    LIVE("Live view"), PROXIMITY("Nearby protection"), SETTINGS("Connection settings")
}

internal fun backDestination(screen: AppScreen, scannerOpen: Boolean = false): AppScreen = when {
    scannerOpen -> AppScreen.PAIRING
    screen in setOf(AppScreen.LIVE, AppScreen.PROXIMITY, AppScreen.SETTINGS) -> AppScreen.DETAILS
    else -> AppScreen.PCS
}
