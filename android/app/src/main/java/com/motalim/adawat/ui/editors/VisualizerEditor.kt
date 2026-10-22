package com.motalim.adawat.ui.editors

import android.graphics.Bitmap
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import com.motalim.adawat.core.L10n
import com.motalim.adawat.media.Visualizer
import com.motalim.adawat.ops.Images
import com.motalim.adawat.ui.Look
import com.motalim.adawat.work.Inputs
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/** Audio Visualizer: a waveform in a colour, or a still picture, on one of three shapes, made into an MP4 with the sound. */
@androidx.annotation.OptIn(UnstableApi::class)
@Composable
fun VisualizerEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val context = LocalContext.current
    val player = rememberMediaPlayer(file)
    var still by remember { mutableStateOf(false) }
    var shape by remember { mutableStateOf(Visualizer.Shape.Landscape) }
    var color by remember { mutableIntStateOf(0xFF5B47E6.toInt()) }
    var image by remember { mutableStateOf<File?>(null) }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri: Uri? ->
        if (uri != null) image = Inputs.stage(context, listOf(uri)).firstOrNull()
    }
    val samples by produceState(ShortArray(0), file) { value = withContext(Dispatchers.Default) { runCatching { Visualizer.samples(file) }.getOrDefault(ShortArray(0)) } }
    // A small frame of the result at the playhead.
    val frame by produceState<Bitmap?>(null, shape, color, still, image, samples, player.position / 200_000) {
        value = withContext(Dispatchers.Default) {
            val w = shape.width / 4; val h = shape.height / 4
            val bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888).apply { eraseColor(android.graphics.Color.BLACK) }
            val canvas = android.graphics.Canvas(bitmap)
            if (still) image?.let { runCatching { Images.decode(it, maxOf(w, h)) }.getOrNull() }?.let { photo ->
                val scale = minOf(w.toFloat() / photo.width, h.toFloat() / photo.height)
                val pw = photo.width * scale; val ph = photo.height * scale
                canvas.drawBitmap(photo, null, android.graphics.RectF((w - pw) / 2, (h - ph) / 2, (w + pw) / 2, (h + ph) / 2), null)
            } else Visualizer.drawWave(canvas, samples, player.position, w, h, color)
            bitmap
        }
    }
    EditorFrame("Audio Visualizer", "Create Visualization", enabled = !still || image != null, onBack = onBack, onAction = {
        onRun(buildMap {
            put("shape", shape.name); put("color", color.toString())
            if (still) image?.let { put("image", it.path) }
        })
    }) {
        Stage {
            Box(Modifier.fillMaxSize(), contentAlignment = androidx.compose.ui.Alignment.Center) {
                frame?.let { Image(it.asImageBitmap(), null, modifier = Modifier.fillMaxSize()) }
            }
        }
        ControlPanel {
            Transport(player)
            PanelLabel("Style")
            DarkChoices(listOf(false to L10n.t("Waveform"), true to L10n.t("Still image")), still) { still = it }
            if (still) {
                OutlinedButton(onClick = { pick.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) }) {
                    Text(L10n.t(if (image == null) "Choose Image…" else image!!.name), color = EditorText)
                }
            } else {
                PanelLabel("Color")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf(0xFF5B47E6.toInt(), 0xFF7CF2C8.toInt(), 0xFFFFFFFF.toInt(), 0xFFFFCC1A.toInt(), 0xFFED4236.toInt(), 0xFF2978F5.toInt()).forEach { c ->
                        Box(Modifier.size(30.dp).clip(CircleShape).background(Color(c))
                            .border(if (c == color) 3.dp else 1.dp, if (c == color) Look.Accent else Color(0x55FFFFFF), CircleShape).clickable { color = c })
                    }
                }
            }
            PanelLabel("Shape")
            DarkChoices(Visualizer.Shape.entries.map { it to L10n.t(it.title) }, shape) { shape = it }
        }
    }
}
