package com.motalim.adawat.ui

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.core.Operation
import com.motalim.adawat.ops.ImageTools
import com.motalim.adawat.ops.Images
import com.motalim.adawat.ops.QrCode
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/** The screens a tool opens before it runs, by the window name the catalog gives it. */
val optionScreens = setOf("compress", "frame", "collage", "qr", "channels")

@Composable
fun OptionsCard(operation: Operation, files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val options = remember { mutableStateOf(mapOf<String, String>()) }
    Surface(
        modifier = Modifier.widthIn(max = 420.dp).padding(horizontal = 16.dp)
            .clickable(remember { MutableInteractionSource() }, indication = null) {},
        shape = RoundedCornerShape(24.dp), color = Color(0xFCFFFFFF), shadowElevation = 12.dp,
    ) {
        Column(Modifier.padding(20.dp)) {
            Text(L10n.t(operation.title), style = MaterialTheme.typography.titleMedium, color = Look.Ink)
            Spacer(Modifier.height(14.dp))
            Column(Modifier.heightIn(max = 520.dp).verticalScroll(rememberScrollState())) {
                when (operation.window) {
                    "compress" -> CompressOptions(options)
                    "frame" -> FrameOptions(files, options)
                    "collage" -> CollageOptions(files, options)
                    "qr" -> QrOptions(files, options)
                    "channels" -> ChannelOptions(options)
                }
            }
            Spacer(Modifier.height(16.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack) { Text(L10n.t("Back")) }
                Spacer(Modifier.weight(1f))
                Button(onClick = { onRun(options.value) }) { Text(L10n.t(runLabel(operation.window))) }
            }
        }
    }
}

private fun runLabel(window: String?) = when (window) {
    "compress" -> "Compress"
    "frame" -> "Add Background"
    "collage" -> "Create Collage"
    "qr" -> "Create QR Code"
    else -> "Convert"
}

@Composable
private fun Label(text: String) {
    Text(L10n.t(text), style = MaterialTheme.typography.labelLarge, color = Look.Muted, modifier = Modifier.padding(top = 10.dp, bottom = 6.dp))
}

/** A row of choices, one selected. */
@Composable
fun <T> Choices(items: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit) {
    Row(
        Modifier.clip(RoundedCornerShape(50)).background(Color(0x12000000)).padding(3.dp).horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        items.forEach { (value, title) ->
            val on = value == selected
            Text(
                title, color = if (on) Color.White else Look.Ink, style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.clip(RoundedCornerShape(50)).background(if (on) Look.Accent else Color.Transparent)
                    .clickable { onSelect(value) }.padding(horizontal = 14.dp, vertical = 8.dp),
            )
        }
    }
}

@Composable
private fun SliderRow(title: String, value: Float, range: ClosedFloatingPointRange<Float>, unit: String = "px", onChange: (Float) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(L10n.t(title), style = MaterialTheme.typography.bodyMedium, color = Look.Ink, modifier = Modifier.width(96.dp))
        Slider(value, onChange, valueRange = range, modifier = Modifier.weight(1f),
               colors = SliderDefaults.colors(thumbColor = Look.Accent, activeTrackColor = Look.Accent))
        Text("⁦${value.toInt()} $unit⁩", style = MaterialTheme.typography.bodySmall, color = Look.Muted, modifier = Modifier.width(56.dp))
    }
}

// Compress

@Composable
private fun CompressOptions(options: androidx.compose.runtime.MutableState<Map<String, String>>) {
    var preset by remember { mutableStateOf("Balanced") }
    Label("Preset")
    Choices(listOf("Light" to L10n.t("Light"), "Balanced" to L10n.t("Balanced"), "Strong" to L10n.t("Strong")), preset) {
        preset = it
        options.value = options.value + ("preset" to it)
    }
    Spacer(Modifier.height(8.dp))
    Text(
        L10n.t(when (preset) {
            "Light" -> "Better visual quality with moderate file-size savings."
            "Balanced" -> "Smaller files with less quality loss."
            else -> "Smaller files with more quality loss."
        }),
        style = MaterialTheme.typography.bodySmall, color = Look.Muted,
    )
}

// Background

@Composable
private fun FrameOptions(files: List<File>, options: androidx.compose.runtime.MutableState<Map<String, String>>) {
    var gradient by remember { mutableIntStateOf(0) }
    var solid by remember { mutableStateOf<Int?>(null) }
    var blurred by remember { mutableStateOf(false) }
    var padding by remember { mutableFloatStateOf(160f) }
    var corners by remember { mutableFloatStateOf(24f) }
    var shadow by remember { mutableFloatStateOf(24f) }
    var ratio by remember { mutableStateOf<Float?>(null) }
    fun publish() {
        options.value = buildMap {
            put("gradient", gradient.toString())
            solid?.let { put("solid", it.toString()) }
            put("blurred", blurred.toString())
            put("padding", padding.toInt().toString())
            put("corners", corners.toInt().toString())
            put("shadow", shadow.toInt().toString())
            ratio?.let { put("ratio", it.toString()) }
        }
    }
    val chosen = ImageTools.FrameOptions(gradient, solid, blurred, padding, corners, shadow, ratio)
    // A live preview, drawn small from the first picture.
    val preview by produceState<Bitmap?>(null, chosen) {
        value = withContext(Dispatchers.Default) {
            runCatching {
                // Measures are in the full picture's pixels, so the preview scales them with the picture.
                val photo = Images.decode(files[0], maxSide = 480)
                ImageTools.frame(photo, chosen, scale = photo.width / Images.size(files[0]).first.toFloat())
            }.getOrNull()
        }
    }
    Preview(preview)
    Label("Background")
    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ImageTools.gradients.forEachIndexed { index, (from, to) ->
            Swatch(Brush.linearGradient(listOf(Color(from or 0xFF000000.toInt()), Color(to or 0xFF000000.toInt()))),
                   selected = solid == null && !blurred && gradient == index) {
                gradient = index; solid = null; blurred = false; publish()
            }
        }
        listOf(0xFFFFFF, 0x282B29, 0x5B47E6, 0x7CF2C8, 0xEEC761).forEach { color ->
            Swatch(Brush.linearGradient(listOf(Color(color or 0xFF000000.toInt()), Color(color or 0xFF000000.toInt()))), selected = solid == color) {
                solid = color; blurred = false; publish()
            }
        }
        Box(
            Modifier.size(36.dp).clip(CircleShape).background(Color(0x22000000))
                .border(if (blurred) 3.dp else 0.dp, Look.Accent, CircleShape).clickable { blurred = true; solid = null; publish() },
            contentAlignment = Alignment.Center,
        ) { Text(iconText(''), fontFamily = Look.Icons, color = Look.Ink) }
    }
    Label("Shape")
    SliderRow("Padding", padding, 0f..400f) { padding = it; publish() }
    SliderRow("Corners", corners, 0f..120f) { corners = it; publish() }
    SliderRow("Shadow", shadow, 0f..80f) { shadow = it; publish() }
    Label("Ratio")
    Choices(listOf(null to L10n.t("Auto"), 1f to "1:1", 16f / 9 to "16:9", 4f / 5 to "4:5", 9f / 16 to "9:16"), ratio) { ratio = it; publish() }
}

@Composable
private fun Swatch(brush: Brush, selected: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.size(36.dp).clip(CircleShape).background(brush)
            .border(if (selected) 3.dp else 1.dp, if (selected) Look.Accent else Color(0x22000000), CircleShape)
            .clickable(onClick = onClick),
    )
}

@Composable
private fun Preview(bitmap: Bitmap?) {
    Box(
        Modifier.fillMaxWidth().height(200.dp).clip(RoundedCornerShape(14.dp)).background(Color(0x0D000000)),
        contentAlignment = Alignment.Center,
    ) {
        bitmap?.let { Image(it.asImageBitmap(), null, contentScale = ContentScale.Fit, modifier = Modifier.padding(8.dp)) }
    }
}

// Collage

@Composable
private fun CollageOptions(files: List<File>, options: androidx.compose.runtime.MutableState<Map<String, String>>) {
    var style by remember { mutableStateOf(ImageTools.CollageStyle.Grid) }
    var columns by remember { mutableIntStateOf(if (files.size <= 4) 2 else 3) }
    var ratio by remember { mutableFloatStateOf(1f) }
    var spacing by remember { mutableFloatStateOf(16f) }
    var corners by remember { mutableFloatStateOf(0f) }
    var background by remember { mutableIntStateOf(0xFFFFFF) }
    fun publish() {
        options.value = mapOf("style" to style.name, "columns" to columns.toString(), "ratio" to ratio.toString(),
                              "spacing" to spacing.toInt().toString(), "corners" to corners.toInt().toString(), "background" to background.toString())
    }
    val chosen = ImageTools.CollageOptions(style, columns, ratio, 2048, true, spacing, corners, background)
    val thumbnails by produceState<List<Bitmap>>(emptyList(), files) {
        value = withContext(Dispatchers.Default) { files.mapNotNull { runCatching { Images.decode(it, maxSide = 400) }.getOrNull() } }
    }
    val preview by produceState<Bitmap?>(null, chosen, thumbnails) {
        value = if (thumbnails.isEmpty()) null else withContext(Dispatchers.Default) { ImageTools.collage(thumbnails, chosen, scale = 0.25f) }
    }
    Preview(preview)
    Label("Layout")
    Choices(ImageTools.CollageStyle.entries.map { it to L10n.t(it.name) }, style) { style = it; publish() }
    if (style == ImageTools.CollageStyle.Grid) {
        Label("Columns")
        Choices((1..minOf(4, files.size)).map { it to it.toString() }, columns) { columns = it; publish() }
    }
    Label("Canvas")
    Choices(listOf(1f to "1:1", 4f / 5 to "4:5", 9f / 16 to "9:16", 16f / 9 to "16:9", 4f / 3 to "4:3"), ratio) { ratio = it; publish() }
    Label("Shape")
    SliderRow("Spacing", spacing, 0f..80f) { spacing = it; publish() }
    SliderRow("Corners", corners, 0f..120f) { corners = it; publish() }
    Label("Background")
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf(0xFFFFFF, 0x000000, 0xF2F2F2, 0x5B47E6, 0xEEC761).forEach { color ->
            Swatch(Brush.linearGradient(listOf(Color(color or 0xFF000000.toInt()), Color(color or 0xFF000000.toInt()))), background == color) {
                background = color; publish()
            }
        }
    }
}

// QR code

@Composable
private fun QrOptions(files: List<File>, options: androidx.compose.runtime.MutableState<Map<String, String>>) {
    var text by remember { mutableStateOf(QrCode.text(files[0]).orEmpty()) }
    var level by remember { mutableStateOf("M") }
    var dots by remember { mutableStateOf(false) }
    var foreground by remember { mutableIntStateOf(0xFF000000.toInt()) }
    fun publish() {
        options.value = mapOf("text" to text, "level" to level, "dots" to dots.toString(), "foreground" to foreground.toString())
    }
    if (options.value.isEmpty()) publish()
    val preview by produceState<Bitmap?>(null, text, level, dots, foreground) {
        value = withContext(Dispatchers.Default) {
            runCatching { QrCode.render(text, 360, com.google.zxing.qrcode.decoder.ErrorCorrectionLevel.valueOf(level), foreground, android.graphics.Color.WHITE, dots) }.getOrNull()
        }
    }
    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Box(Modifier.size(180.dp).clip(RoundedCornerShape(12.dp)).background(Color.White).border(1.dp, Color(0x1A000000), RoundedCornerShape(12.dp))) {
            preview?.let { Image(it.asImageBitmap(), null, modifier = Modifier.aspectRatio(1f)) }
                ?: Text(L10n.t("This is too long for a QR code. Shorten it or lower the error correction."), style = MaterialTheme.typography.bodySmall,
                        color = Look.Failure, modifier = Modifier.padding(12.dp))
        }
    }
    Label("Text")
    OutlinedTextField(text, { text = it; publish() }, modifier = Modifier.fillMaxWidth(), maxLines = 4,
                      keyboardOptions = KeyboardOptions.Default)
    Label("Error correction")
    Choices(listOf("L" to "7%", "M" to "15%", "Q" to "25%", "H" to "30%"), level) { level = it; publish() }
    Label("Style")
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(L10n.t("Dots"), style = MaterialTheme.typography.bodyMedium, color = Look.Ink, modifier = Modifier.weight(1f))
        Switch(dots, { dots = it; publish() })
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 8.dp)) {
        listOf(0xFF000000.toInt(), 0xFF5B47E6.toInt(), 0xFF1F6F5C.toInt(), 0xFFB3261E.toInt(), 0xFF21546B.toInt()).forEach { color ->
            Swatch(Brush.linearGradient(listOf(Color(color), Color(color))), foreground == color) { foreground = color; publish() }
        }
    }
}

// Channels

@Composable
private fun ChannelOptions(options: androidx.compose.runtime.MutableState<Map<String, String>>) {
    var channels by remember { mutableStateOf("mono") }
    Label("Channels")
    Choices(listOf("mono" to L10n.t("Mono"), "stereo" to L10n.t("Stereo")), channels) { channels = it; options.value = mapOf("channels" to it) }
    Spacer(Modifier.height(8.dp))
    Text(L10n.t(if (channels == "mono") "Both sides mixed into one channel." else "A mono recording played on both sides."),
         style = MaterialTheme.typography.bodySmall, color = Look.Muted)
}

