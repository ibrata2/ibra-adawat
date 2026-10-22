package com.motalim.adawat.ui.editors

import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ops.Images
import java.io.File
import kotlin.math.roundToInt

/** Crop: a frame over the picture with the desktop's ratios; several pictures take the same share of each. */
@Composable
fun CropEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val bitmap = rememberPreview(files[0])
    val size = remember(files) { runCatching { Images.size(files[0]) }.getOrDefault(1 to 1) }
    var area by remember { mutableStateOf(Area.Whole) }
    var ratio by remember { mutableStateOf<Float?>(null) }
    val pixelRatio = size.first.toFloat() / size.second
    fun setRatio(value: Float?) {
        ratio = value
        if (value == null) return
        // The largest centred frame of that shape.
        val share = value / pixelRatio
        val (w, h) = if (share >= 1f) 1f to 1f / share else share to 1f
        area = Area((1f - w) / 2, (1f - h) / 2, w, h)
    }
    EditorFrame("Crop", "Apply", onAction = { onRun(mapOf("area" to area.encode())) }, onBack = onBack,
                secondary = "Reset" to { ratio = null; area = Area.Whole }) {
        Stage {
            FittedPicture(bitmap, Modifier.fillMaxSize()) { picture -> CropFrame(area, { area = it }, picture, ratio, pixelRatio) }
        }
        ControlPanel {
            PanelLabel("Aspect ratio")
            DarkChoices(listOf(null to L10n.t("Free"), 1f to "1:1", 16f / 9 to "16:9", 9f / 16 to "9:16", 4f / 3 to "4:3", 3f / 4 to "3:4", 4f / 5 to "4:5"), ratio, ::setRatio)
            Row {
                Text("⁦${(area.w * size.first).roundToInt()} × ${(area.h * size.second).roundToInt()} px⁩", style = MaterialTheme.typography.bodyMedium,
                     color = EditorText, modifier = Modifier)
            }
            if (files.size > 1) {
                Text(L10n.t("Uses this crop, as the same share of each image, for all ${files.size} images."), style = MaterialTheme.typography.bodySmall,
                     color = EditorText)
            }
        }
    }
}
