// LapCont — Android — Shared colors, readable type and comfortable controls
// License: MIT
package com.lapcont.mobile.presentation
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

@Composable fun LapContTheme(content: @Composable () -> Unit) {
    val colors = if (isSystemInDarkTheme()) darkColorScheme(
        primary = Color(0xFFC3B8FF), onPrimary = Color(0xFF29205D),
        primaryContainer = Color(0xFF38305F), onPrimaryContainer = Color(0xFFEEE9FF),
        background = Color(0xFF11141D), onBackground = Color(0xFFECEEF7),
        surface = Color(0xFF1B2030), onSurface = Color(0xFFECEEF7),
        surfaceVariant = Color(0xFF282D3E), onSurfaceVariant = Color(0xFFBCC2D3),
        outlineVariant = Color(0xFF363C50)
    ) else lightColorScheme(
        primary = Color(0xFF5B4EE8), onPrimary = Color.White,
        primaryContainer = Color(0xFFEAE6FF), onPrimaryContainer = Color(0xFF332665),
        background = Color(0xFFF5F6FB), onBackground = Color(0xFF20263A),
        surface = Color.White, onSurface = Color(0xFF20263A),
        surfaceVariant = Color(0xFFEEF0F7), onSurfaceVariant = Color(0xFF59637A),
        outlineVariant = Color(0xFFE1E5F0)
    )
    val base = Typography()
    MaterialTheme(colorScheme = colors, typography = base.copy(
        headlineLarge = base.headlineLarge.copy(fontSize = 30.sp, fontWeight = FontWeight.Bold),
        headlineMedium = base.headlineMedium.copy(fontSize = 26.sp, fontWeight = FontWeight.Bold),
        titleLarge = base.titleLarge.copy(fontSize = 21.sp, fontWeight = FontWeight.SemiBold),
        titleMedium = base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
        bodyLarge = base.bodyLarge.copy(lineHeight = 25.sp)
    )) { CompositionLocalProvider(LocalContentColor provides colors.onBackground, content = content) }
}
