package com.motalim.adawat.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.LayoutDirection
import com.motalim.adawat.R
import com.motalim.adawat.core.L10n

/** The app's colours and type, the same as the desktop apps. */
object Look {
    val Accent = Color(0xFF5B47E6)
    val AccentPressed = Color(0xFF4836C4)
    val AccentSoft = Color(0x265B47E6)
    val Mint = Color(0xFF7CF2C8)
    val Ink = Color(0xFF282B29)
    val Muted = Color(0xFF757575)
    val Surface = Color(0xFFFAFAFB)
    val PetalFill = Color(0xF0F8F9FA)
    val PetalStroke = Color(0x385D6666)
    val Disc = Color(0xB8EEF0F2)
    val Failure = Color(0xFFD83B3B)
    val Shadow = Color(0xFF263438)

    val Arabic = FontFamily(
        Font(R.font.noto_arabic_regular, FontWeight.Normal),
        Font(R.font.noto_arabic_medium, FontWeight.Medium),
        Font(R.font.noto_arabic_semibold, FontWeight.SemiBold),
        Font(R.font.noto_arabic_bold, FontWeight.Bold),
    )
    val Icons = FontFamily(Font(R.font.material_icons_round))

    val font: FontFamily get() = if (L10n.isArabic) Arabic else FontFamily.Default
}

@Composable
fun AdawatTheme(content: @Composable () -> Unit) {
    val dark = isSystemInDarkTheme()
    val colors = if (dark) darkColorScheme(primary = Color(0xFF9C8FFF), onPrimary = Color.White, secondary = Look.Mint)
                 else lightColorScheme(primary = Look.Accent, onPrimary = Color.White, secondary = Look.Mint, background = Look.Surface, surface = Look.Surface)
    val base = Typography()
    val family = Look.font
    val typography = Typography(
        displaySmall = base.displaySmall.copy(fontFamily = family),
        headlineMedium = base.headlineMedium.copy(fontFamily = family, fontWeight = FontWeight.SemiBold),
        headlineSmall = base.headlineSmall.copy(fontFamily = family, fontWeight = FontWeight.SemiBold),
        titleLarge = base.titleLarge.copy(fontFamily = family, fontWeight = FontWeight.SemiBold),
        titleMedium = base.titleMedium.copy(fontFamily = family, fontWeight = FontWeight.SemiBold),
        titleSmall = base.titleSmall.copy(fontFamily = family),
        bodyLarge = base.bodyLarge.copy(fontFamily = family),
        bodyMedium = base.bodyMedium.copy(fontFamily = family),
        bodySmall = base.bodySmall.copy(fontFamily = family),
        labelLarge = base.labelLarge.copy(fontFamily = family, fontWeight = FontWeight.SemiBold),
        labelMedium = base.labelMedium.copy(fontFamily = family),
        labelSmall = base.labelSmall.copy(fontFamily = family),
    )
    MaterialTheme(colorScheme = colors, typography = typography) {
        CompositionLocalProvider(LocalLayoutDirection provides if (L10n.isRightToLeft) LayoutDirection.Rtl else LayoutDirection.Ltr) {
            content()
        }
    }
}

fun iconText(code: Char) = code.toString()

val IconStyle = TextStyle(fontFamily = Look.Icons)
