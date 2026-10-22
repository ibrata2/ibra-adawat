package com.motalim.adawat.ui.editors

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ops.Images
import com.motalim.adawat.ops.Recognition
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/** Redact: boxes over the parts to hide, drawn by hand or found (faces, text), filled solid or blurred. */
@Composable
fun RedactEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val bitmap = rememberPreview(files[0])
    var boxes by remember { mutableStateOf(listOf<Area>()) }
    var selected by remember { mutableStateOf<Int?>(null) }
    var blur by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    fun detect(faces: Boolean) {
        status = L10n.t(if (faces) "Detecting faces…" else "Detecting text…")
        scope.launch {
            val found = withContext(Dispatchers.Default) {
                runCatching {
                    val picture = Images.decode(files[0], 2400)
                    (if (faces) Recognition.faces(picture) else Recognition.textBoxes(picture)).map {
                        // Room around text, so the tops of tall letters and dots below are covered too.
                        if (!faces) it.inset(-it.height() * 0.25f, -it.height() * 0.3f)
                        Area(it.left / picture.width, it.top / picture.height, it.width() / picture.width, it.height() / picture.height)
                    }.map { a -> Area(a.x.coerceIn(0f, 1f), a.y.coerceIn(0f, 1f), a.w.coerceAtMost(1f - a.x.coerceIn(0f, 1f)), a.h.coerceAtMost(1f - a.y.coerceIn(0f, 1f))) }
                }.getOrDefault(emptyList())
            }
            boxes = boxes + found
            status = when {
                found.isEmpty() -> L10n.t(if (faces) "No faces found" else "No text found")
                faces -> L10n.t(if (found.size == 1) "Covered 1 face" else "Covered ${found.size} faces")
                else -> L10n.t(if (found.size == 1) "Covered 1 text area" else "Covered ${found.size} text areas")
            }
        }
    }
    EditorFrame("Redact Photo", "Save Redacted Copy", onAction = { onRun(mapOf("boxes" to boxes.joinToString("|") { it.encode() }, "blur" to blur.toString())) },
                onBack = onBack, enabled = boxes.isNotEmpty(),
                secondary = if (selected != null) "Delete Redaction" to { boxes = boxes.filterIndexed { i, _ -> i != selected }; selected = null } else null) {
        Stage {
            FittedPicture(bitmap, Modifier.fillMaxSize()) { picture ->
                BoxLayer(boxes, selected, { list, index -> boxes = list; selected = index }, picture,
                         if (blur) Color(0x99808080) else Color(0xEE000000))
            }
        }
        ControlPanel {
            Text(
                status ?: L10n.t(if (blur) "Blur softens details. Use Solid for sensitive information." else "Draw rectangles over the areas to hide. Drag a box to move it, or its corners to resize."),
                style = MaterialTheme.typography.bodySmall, color = EditorText,
            )
            PanelLabel("Style")
            DarkChoices(listOf(false to L10n.t("Solid"), true to L10n.t("Blur")), blur) { blur = it }
            PanelLabel("Detect")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { detect(true) }) { Text(L10n.t("Faces"), color = EditorText) }
                OutlinedButton(onClick = { detect(false) }) { Text(L10n.t("Text"), color = EditorText) }
            }
        }
    }
}
