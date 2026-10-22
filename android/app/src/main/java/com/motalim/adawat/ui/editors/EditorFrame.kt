package com.motalim.adawat.ui.editors

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.core.Operation
import com.motalim.adawat.ui.Look
import java.io.File

/** The editors that take the whole screen, by the window name the catalog gives a tool. */
val editorScreens = setOf("crop", "edit", "redact", "metadata", "organize", "merge", "join", "trim", "speed", "crop-video", "split", "frames",
                          "redact-video", "bleep", "normalize", "annotate", "visualizer")

@Composable
fun EditorHost(operation: Operation, files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    when (operation.window) {
        "crop" -> CropEditor(files, onRun, onBack)
        "edit" -> PhotoEditor(files, onRun, onBack)
        "redact" -> RedactEditor(files, onRun, onBack)
        "metadata" -> MetadataEditor(files, onRun, onBack)
        "organize" -> OrganizeEditor(files, onRun, onBack)
        "merge" -> MergeEditor(files, onRun, onBack)
        "join" -> JoinEditor(files, onRun, onBack)
        "trim" -> TrimEditor(files, onRun, onBack)
        "speed" -> SpeedEditor(files, onRun, onBack)
        "crop-video" -> CropVideoEditor(files, onRun, onBack)
        "split" -> SplitEditor(files, onRun, onBack)
        "frames" -> FramesEditor(files, onRun, onBack)
        "redact-video" -> RedactVideoEditor(files, onRun, onBack)
        "bleep" -> BleepEditor(files, onRun, onBack)
        "normalize" -> NormalizeEditor(files, onRun, onBack)
        "annotate" -> AnnotateEditor(files, onRun, onBack)
        "visualizer" -> VisualizerEditor(files, onRun, onBack)
        else -> onBack()
    }
}

/** A full-screen editor: title, the work area, and Cancel / the main action at the bottom. */
@Composable
fun EditorFrame(
    title: String,
    action: String,
    onAction: () -> Unit,
    onBack: () -> Unit,
    enabled: Boolean = true,
    secondary: Pair<String, () -> Unit>? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    Column(Modifier.fillMaxSize().background(Color(0xFF111214)).systemBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text(L10n.t("Cancel"), color = Color.White) }
            Spacer(Modifier.weight(1f))
            Text(L10n.t(title), style = MaterialTheme.typography.titleMedium, color = Color.White)
            Spacer(Modifier.weight(1f))
            Spacer(Modifier.width(72.dp))
        }
        Column(Modifier.weight(1f).fillMaxWidth(), content = content)
        Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            secondary?.let { (label, run) -> TextButton(onClick = run) { Text(L10n.t(label), color = Color(0xFFB9B2FF)) } }
            Spacer(Modifier.weight(1f))
            Button(onClick = onAction, enabled = enabled) { Text(L10n.t(action)) }
        }
    }
}

/** A dark panel under the work area for an editor's controls. */
@Composable
fun ControlPanel(content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().background(Color(0xFF1C1D21)).padding(horizontal = 16.dp, vertical = 12.dp), content = content)
}

@Composable
fun PanelLabel(text: String) {
    Text(L10n.t(text), style = MaterialTheme.typography.labelLarge, color = Color(0xFF9A9CA3), modifier = Modifier.padding(top = 8.dp, bottom = 6.dp))
}

/** Centres [content] in the space left. */
@Composable
fun ColumnScope.Stage(content: @Composable () -> Unit) {
    Box(Modifier.weight(1f).fillMaxWidth().padding(16.dp), contentAlignment = Alignment.Center) { content() }
}

val EditorText = Color(0xFFE8E8EC)
val EditorAccent = Look.Accent
