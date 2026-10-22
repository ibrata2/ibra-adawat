package com.motalim.adawat.ui.editors

import android.graphics.Bitmap
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ops.Adjust
import com.motalim.adawat.ops.Adjustments
import com.motalim.adawat.ops.Images
import com.motalim.adawat.ui.Look
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.File
import java.util.Locale
import kotlin.math.roundToInt

/** Edit Photo: the desktop's light, colour, detail and effect sliders over a live preview; hold the picture to see the original. */
@Composable
fun PhotoEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val original = rememberPreview(files[0], 1200)
    val fullWidth = remember(files) { runCatching { Images.size(files[0]).first }.getOrDefault(1) }
    val values = remember { mutableStateMapOf<Adjust, Float>() }
    var edited by remember { mutableStateOf<Bitmap?>(null) }
    var holding by remember { mutableStateOf(false) }
    val snapshot = values.toMap()
    LaunchedEffect(original, snapshot) {
        val source = original ?: return@LaunchedEffect
        delay(60) // Let a slider settle before drawing again.
        edited = withContext(Dispatchers.Default) { Adjustments.apply(source, snapshot, source.width / fullWidth.toFloat()) }
    }
    val neutral = snapshot.values.all { it == 0f }
    EditorFrame("Edit Photo", "Save Edited Copy", onAction = { onRun(mapOf("adjust" to Adjustments.encode(snapshot))) }, onBack = onBack,
                enabled = !neutral, secondary = "Reset" to { values.clear() }) {
        Stage {
            Box(Modifier.fillMaxSize().pointerInput(Unit) {
                awaitEachGesture {
                    awaitFirstDown()
                    holding = true
                    waitForUpOrCancellation()
                    holding = false
                }
            }) {
                FittedPicture(if (holding || neutral) original else edited ?: original, Modifier.fillMaxSize())
                Text(
                    L10n.t(if (holding || neutral) "Original" else "Edited"), color = Color.White, style = MaterialTheme.typography.labelMedium,
                    modifier = Modifier.align(Alignment.TopEnd).padding(8.dp).clip(RoundedCornerShape(50)).background(Color(0xCC000000))
                        .padding(horizontal = 10.dp, vertical = 3.dp),
                )
            }
        }
        ControlPanel {
            Column(Modifier.heightIn(max = 300.dp).verticalScroll(rememberScrollState())) {
                Adjustments.sections.forEach { (section, items) ->
                    // "Light" alone reads as "not heavy" in Arabic; the desktop names this section the same way.
                    if (section == "Light" && L10n.isArabic) Text("الإضاءة", style = MaterialTheme.typography.labelLarge, color = androidx.compose.ui.graphics.Color(0xFF9A9CA3),
                                                                  modifier = Modifier.padding(top = 8.dp, bottom = 6.dp))
                    else PanelLabel(section)
                    items.forEach { key ->
                        val value = values[key] ?: 0f
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(L10n.t(key.title), color = EditorText, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.width(110.dp))
                            Slider(
                                value, { values[key] = if (key == Adjust.Exposure) (it * 100).roundToInt() / 100f else it.roundToInt().toFloat() },
                                valueRange = key.min..key.max, modifier = Modifier.weight(1f),
                                colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = Look.Accent, inactiveTrackColor = Color(0xFF3A3B40)),
                            )
                            Text(
                                "⁦" + (if (key == Adjust.Exposure) String.format(Locale.US, "%.2f", value) else value.roundToInt().toString()) + key.unit + "⁩",
                                color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.width(56.dp),
                            )
                        }
                    }
                }
            }
        }
    }
}
