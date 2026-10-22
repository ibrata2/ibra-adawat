package com.motalim.adawat.ui.editors

import android.graphics.PointF
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ops.Annotation
import com.motalim.adawat.ops.AnnotationRenderer
import com.motalim.adawat.ops.AnnotationTool
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ui.iconText
import java.io.File

private val tools = listOf(
    Triple(AnnotationTool.Arrow, '', "Drag from the tail toward the arrowhead. Drag an annotation to move it."),
    Triple(AnnotationTool.Highlight, '', "Drag over the image to highlight."),
    Triple(AnnotationTool.Text, '', "Tap to add a text box, then type below. Drag a box to move it."),
    Triple(AnnotationTool.Rectangle, '', "Drag on the image to draw. Drag an annotation to move it."),
    Triple(AnnotationTool.Ellipse, '', "Drag on the image to draw. Drag an annotation to move it."),
    Triple(AnnotationTool.Draw, '', "Drag to draw freehand. Undo removes the last stroke."),
    Triple(AnnotationTool.Callout, '', "Tap to add numbered callouts; drag to move them. Their size stays fixed."),
    Triple(AnnotationTool.Magnifier, '', "Drag out from the spot to magnify. Drag a magnifier to move it."),
)

/** Annotate Photo: arrows, highlights, text, shapes, drawing, numbered callouts and magnifiers, in the desktop's colours. */
@Composable
fun AnnotateEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val bitmap = rememberPreview(files[0], 1600)
    var notes by remember { mutableStateOf(listOf<Annotation>()) }
    var tool by remember { mutableStateOf(AnnotationTool.Arrow) }
    var color by remember { mutableIntStateOf(AnnotationRenderer.colors[0]) }
    var selected by remember { mutableStateOf<Int?>(null) }
    // Bumped on every change so the canvas redraws marks that were edited in place.
    var version by remember { mutableIntStateOf(0) }
    val selectedNote = selected?.let { notes.getOrNull(it) }
    EditorFrame("Annotate Photo", "Save Annotated Copy", enabled = notes.isNotEmpty(), onBack = onBack,
                onAction = { onRun(mapOf("annotations" to AnnotationRenderer.encode(notes))) },
                secondary = if (notes.isNotEmpty()) "Undo" to { notes = notes.dropLast(1); selected = null; version++ } else null) {
        Stage {
            FittedPicture(bitmap, Modifier.fillMaxSize()) { picture ->
                AnnotationLayer(notes, version, picture, tool, color, bitmap, selected,
                                onNotes = { list, index -> notes = list; selected = index; version++ })
            }
        }
        ControlPanel {
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                tools.forEach { (t, glyph, _) ->
                    val on = t == tool
                    Text(iconText(glyph), fontFamily = Look.Icons, fontSize = 20.sp, color = if (on) Color.White else EditorText,
                         modifier = Modifier.size(42.dp).clip(RoundedCornerShape(10.dp)).background(if (on) Look.Accent else Color(0xFF2A2B30))
                             .clickable { tool = t; selected = null }.padding(10.dp))
                }
            }
            Row(Modifier.padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AnnotationRenderer.colors.forEach { c ->
                    Box(Modifier.size(30.dp).clip(CircleShape).background(Color(c))
                        .border(if (c == color) 3.dp else 1.dp, if (c == color) Look.Accent else Color(0x55FFFFFF), CircleShape).clickable { color = c })
                }
            }
            if (selectedNote?.tool == AnnotationTool.Text) {
                OutlinedTextField(selectedNote.text, { selectedNote.text = it; version++; notes = notes.toList() }, modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                                  placeholder = { Text(L10n.t("Type the text")) }, textStyle = MaterialTheme.typography.bodyMedium.copy(color = EditorText),
                                  colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Look.Accent, unfocusedBorderColor = Color(0xFF3A3B40)))
            } else {
                Text(L10n.t(tools.first { it.first == tool }.third), color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 8.dp))
            }
        }
    }
}

@Composable
private fun AnnotationLayer(
    notes: List<Annotation>,
    version: Int,
    picture: Rect,
    tool: AnnotationTool,
    color: Int,
    photo: android.graphics.Bitmap?,
    selected: Int?,
    onNotes: (List<Annotation>, Int?) -> Unit,
) {
    val current by rememberUpdatedState(notes)
    val activeTool by rememberUpdatedState(tool)
    val activeColor by rememberUpdatedState(color)
    val update by rememberUpdatedState(onNotes)
    val aspect = picture.width / picture.height
    fun share(p: Offset) = PointF(((p.x - picture.left) / picture.width).coerceIn(0f, 1f), ((p.y - picture.top) / picture.height).coerceIn(0f, 1f))
    fun hit(p: PointF) = current.indexOfLast { it.bounds(aspect).contains(p.x, p.y) }
    Canvas(
        Modifier.fillMaxSize().systemGestureExclusion()
            .pointerInput(picture) {
                detectTapGestures { at ->
                    val p = share(at)
                    val index = hit(p)
                    when {
                        index >= 0 -> update(current, index)
                        activeTool == AnnotationTool.Text -> update(current + Annotation(AnnotationTool.Text, activeColor, p), current.size)
                        activeTool == AnnotationTool.Callout ->
                            update(current + Annotation(AnnotationTool.Callout, activeColor, p, number = current.count { it.tool == AnnotationTool.Callout } + 1), null)
                        else -> update(current, null)
                    }
                }
            }
            .pointerInput(picture) {
                var moving = -1
                var drawing: Annotation? = null
                // The list as this gesture changed it; the composed one may not have caught up between events.
                var working = emptyList<Annotation>()
                detectDragGestures(
                    onDragStart = { at ->
                        val p = share(at)
                        working = current
                        val index = if (activeTool == AnnotationTool.Draw) -1 else hit(p)
                        if (index >= 0) { moving = index; update(working, index) }
                        else {
                            moving = -1
                            val note = when (activeTool) {
                                AnnotationTool.Magnifier -> Annotation(AnnotationTool.Magnifier, activeColor, p, PointF(0.02f, 0f))
                                AnnotationTool.Draw -> Annotation(AnnotationTool.Draw, activeColor, p, points = mutableListOf(PointF(p.x, p.y)))
                                AnnotationTool.Text, AnnotationTool.Callout -> null
                                else -> Annotation(activeTool, activeColor, p)
                            }
                            drawing = note
                            if (note != null) { working = working + note; update(working, null) }
                        }
                    },
                    onDrag = { change, amount ->
                        change.consume()
                        val p = share(change.position)
                        if (moving >= 0) {
                            working.getOrNull(moving)?.move(amount.x / picture.width, amount.y / picture.height)
                            update(working, moving)
                        } else drawing?.let { note ->
                            when (note.tool) {
                                AnnotationTool.Draw -> note.points += p
                                AnnotationTool.Magnifier -> note.b = PointF(kotlin.math.hypot(p.x - note.a.x, (p.y - note.a.y) / aspect).coerceIn(0.02f, 0.4f), 0f)
                                else -> note.b = p
                            }
                            update(working, null)
                        }
                    },
                    onDragEnd = { moving = -1; drawing = null },
                )
            },
    ) {
        @Suppress("UNUSED_EXPRESSION") version
        drawIntoCanvas { canvas ->
            canvas.nativeCanvas.save()
            canvas.nativeCanvas.translate(picture.left, picture.top)
            canvas.nativeCanvas.clipRect(0f, 0f, picture.width, picture.height)
            val scaled = photo
            AnnotationRenderer.draw(canvas.nativeCanvas, notes, picture.width, picture.height, scaled)
            canvas.nativeCanvas.restore()
        }
        selected?.let { notes.getOrNull(it) }?.let { note ->
            val b = note.bounds(aspect)
            drawRect(Look.Accent, Offset(picture.left + b.left * picture.width, picture.top + b.top * picture.height),
                     androidx.compose.ui.geometry.Size(b.width() * picture.width, b.height() * picture.height),
                     style = androidx.compose.ui.graphics.drawscope.Stroke(2.dp.toPx()))
        }
    }
}
