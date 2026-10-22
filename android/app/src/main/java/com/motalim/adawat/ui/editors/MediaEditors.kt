package com.motalim.adawat.ui.editors

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ui.Look
import java.io.File
import java.util.Locale

/** Trim: keep the part between the two handles. */
@Composable
fun TrimEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    var start by remember { mutableLongStateOf(0L) }
    var end by remember { mutableLongStateOf(player.durationUs) }
    EditorFrame("Trim", "Save Trimmed Copy", onAction = { onRun(mapOf("start" to start.toString(), "end" to end.toString())) }, onBack = onBack,
                enabled = start > 0 || end < player.durationUs) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Transport(player)
            RangeTimeline(file, player, start, end) { s, e -> start = s; end = e }
            // Times read left to right like the timeline above them.
            androidx.compose.runtime.CompositionLocalProvider(androidx.compose.ui.platform.LocalLayoutDirection provides androidx.compose.ui.unit.LayoutDirection.Ltr) {
            Row(Modifier.fillMaxWidth().padding(top = 8.dp)) {
                Text(L10n.t("Start") + " " + clock(start), color = EditorText, style = MaterialTheme.typography.bodySmall)
                Box(Modifier.weight(1f))
                Text(L10n.t("Length") + " " + clock(end - start), color = Look.Mint, style = MaterialTheme.typography.bodySmall)
                Box(Modifier.weight(1f))
                Text(L10n.t("End") + " " + clock(end), color = EditorText, style = MaterialTheme.typography.bodySmall)
            }
            }
        }
    }
}

/** Change Speed: from a quarter to four times, the sound keeping its pitch. */
@Composable
fun SpeedEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    var speed by remember { mutableStateOf(2f) }
    val speeds = listOf(0.25f, 0.5f, 0.75f, 1.25f, 1.5f, 2f, 3f, 4f)
    EditorFrame("Change Speed", "Save", onAction = { onRun(mapOf("speed" to speed.toString())) }, onBack = onBack) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Transport(player)
            PanelLabel("Speed")
            DarkChoices(speeds.map { it to String.format(Locale.US, "%sx", if (it == it.toInt().toFloat()) it.toInt().toString() else it.toString()) }, speed) {
                speed = it
                player.player.setPlaybackSpeed(it)
            }
            Text(L10n.t("Audio keeps its pitch. The new length is {1}.").replace("{1}", clock((player.durationUs / speed).toLong(), tenths = false)),
                 color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 8.dp))
        }
    }
}

/** Crop Video: a frame over the picture. */
@Composable
fun CropVideoEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    var area by remember { mutableStateOf(Area.Whole) }
    var ratio by remember { mutableStateOf<Float?>(null) }
    val turned = player.info.rotation % 180 != 0
    val pixelRatio = (if (turned) player.info.height else player.info.width).toFloat() / (if (turned) player.info.width else player.info.height).coerceAtLeast(1)
    fun setRatio(value: Float?) {
        ratio = value
        if (value == null) return
        val share = value / pixelRatio
        val (w, h) = if (share >= 1f) 1f to 1f / share else share to 1f
        area = Area((1f - w) / 2, (1f - h) / 2, w, h)
    }
    EditorFrame("Crop Video", "Save Cropped Copy", onAction = { onRun(mapOf("area" to area.encode())) }, onBack = onBack,
                enabled = area != Area.Whole, secondary = "Reset" to { area = Area.Whole; ratio = null }) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) { picture -> CropFrame(area, { area = it }, picture, ratio, pixelRatio) } }
        ControlPanel {
            Transport(player)
            Timeline(file, player)
            PanelLabel("Aspect ratio")
            DarkChoices(listOf(null to L10n.t("Free"), 1f to "1:1", 16f / 9 to "16:9", 9f / 16 to "9:16", 4f / 5 to "4:5", 4f / 3 to "4:3"), ratio, ::setRatio)
        }
    }
}

/** Split: cuts at the playhead; tap a clip to leave it out; the kept clips are saved apart or joined. */
@Composable
fun SplitEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    val points = remember { mutableStateListOf<Long>() }
    val removed = remember { mutableStateListOf<Int>() }
    var join by remember { mutableStateOf(false) }
    val bounds = listOf(0L) + points.sorted() + player.durationUs
    EditorFrame("Split Video", if (join) "Save Edited Video" else "Save Clips", enabled = points.isNotEmpty() && removed.size < bounds.size - 1,
                onAction = { onRun(mapOf("points" to points.sorted().joinToString(","), "removed" to removed.joinToString(","), "join" to join.toString())) },
                onBack = onBack, secondary = if (points.isNotEmpty()) "Clear" to { points.clear(); removed.clear() } else null) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Transport(player)
                Box(Modifier.weight(1f))
                OutlinedButton(onClick = {
                    val at = player.position
                    if (at > 100_000 && at < player.durationUs - 100_000 && points.none { kotlin.math.abs(it - at) < 200_000 }) { points += at; removed.clear() }
                }) { Text(L10n.t("Split Here"), color = EditorText) }
            }
            Timeline(file, player, onTap = null) { toX ->
                for (i in 0 until bounds.size - 1) {
                    if (i in removed) {
                        val a = toX(bounds[i]); val b = toX(bounds[i + 1])
                        drawRect(Color(0xCC000000), Offset(a, 0f), Size(b - a, size.height))
                        drawLine(Look.Failure, Offset(a, 0f), Offset(b, size.height), 3f)
                    }
                }
                points.forEach { p -> val x = toX(p); drawRect(Look.Mint, Offset(x - 2.dp.toPx(), 0f), Size(4.dp.toPx(), size.height)) }
            }
            if (points.isNotEmpty()) {
                PanelLabel("Clips")
                Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    for (i in 0 until bounds.size - 1) {
                        val gone = i in removed
                        Text("⁦${i + 1} · ${clock(bounds[i + 1] - bounds[i], tenths = false)}⁩", color = if (gone) Color(0xFF6A6B70) else EditorText,
                             style = MaterialTheme.typography.labelLarge,
                             modifier = Modifier.clip(RoundedCornerShape(50)).background(if (gone) Color(0xFF2A2B30) else Color(0xFF3A3570))
                                 .clickable { if (gone) removed.remove(i) else removed.add(i) }.padding(horizontal = 12.dp, vertical = 6.dp))
                    }
                }
                Text(L10n.t("Tap a clip to leave it out."), color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 6.dp))
            }
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
                Text(L10n.t("Join the kept clips into one video"), color = EditorText, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                Switch(join, { join = it })
            }
        }
    }
}

/** Save Frames: add the frame at the playhead, as many as wanted. */
@Composable
fun FramesEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    val times = remember { mutableStateListOf<Long>() }
    EditorFrame("Save Frames", if (times.size <= 1) "Save Frame" else "Save ${times.size} Frames", enabled = times.isNotEmpty(),
                onAction = { onRun(mapOf("times" to times.sorted().joinToString(","))) }, onBack = onBack,
                secondary = if (times.isNotEmpty()) "Clear" to { times.clear() } else null) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Transport(player)
                Box(Modifier.weight(1f))
                OutlinedButton(onClick = { if (times.none { kotlin.math.abs(it - player.position) < 40_000 }) times += player.position }) {
                    Text(L10n.t("Add Frame"), color = EditorText)
                }
            }
            Timeline(file, player) { toX ->
                times.forEach { t -> val x = toX(t); drawCircle(Look.Mint, 5.dp.toPx(), Offset(x, size.height - 8.dp.toPx())) }
            }
            Text(L10n.t(if (times.isEmpty()) "Move to a frame and add it." else if (times.size == 1) "1 frame" else "${times.size} frames"),
                 color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 6.dp))
        }
    }
}

/** Redact Video: boxes drawn on the picture, each shown for the range chosen on the timeline. */
@Composable
fun RedactVideoEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    var boxes by remember { mutableStateOf(listOf<Area>()) }
    var ranges by remember { mutableStateOf(listOf<Pair<Long, Long>>()) }
    var selected by remember { mutableStateOf<Int?>(null) }
    EditorFrame("Redact Video", "Save Redacted Copy", enabled = boxes.isNotEmpty(), onBack = onBack,
                onAction = { onRun(mapOf("boxes" to boxes.indices.joinToString("|") { i -> boxes[i].encode() + "," + ranges[i].first + "," + ranges[i].second })) },
                secondary = if (selected != null) "Delete Redaction" to {
                    val i = selected!!
                    boxes = boxes.filterIndexed { n, _ -> n != i }; ranges = ranges.filterIndexed { n, _ -> n != i }; selected = null
                } else null) {
        Stage {
            MediaStage(file, player, Modifier.fillMaxSize()) { picture ->
                // Boxes showing now; a new box covers the whole video until its range is changed.
                val visible = boxes.indices.filter { player.position in ranges[it].first..ranges[it].second || it == selected }
                BoxLayer(visible.map { boxes[it] }, visible.indexOf(selected ?: -1).takeIf { it >= 0 }, { list, index ->
                    val updated = boxes.toMutableList()
                    val newRanges = ranges.toMutableList()
                    list.forEachIndexed { n, area -> if (n < visible.size) updated[visible[n]] = area else { updated += area; newRanges += 0L to player.durationUs } }
                    boxes = updated; ranges = newRanges
                    selected = index?.let { if (it < visible.size) visible[it] else updated.lastIndex }
                }, picture, Color(0xEE000000))
            }
        }
        ControlPanel {
            Transport(player)
            val chosen = selected
            if (chosen != null && chosen in ranges.indices) {
                RangeTimeline(file, player, ranges[chosen].first, ranges[chosen].second) { s, e -> ranges = ranges.toMutableList().also { it[chosen] = s to e } }
                Text(L10n.t("Shown from {1} to {2}").replace("{1}", clock(ranges[chosen].first)).replace("{2}", clock(ranges[chosen].second)),
                     color = EditorText, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 6.dp))
            } else {
                Timeline(file, player)
                Text(L10n.t("Draw rectangles over the areas to hide. Drag a box to move it, or its corners to resize."),
                     color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 6.dp))
            }
        }
    }
}

/** Bleep: mark parts on the waveform; each is replaced with a tone or silence. */
@Composable
fun BleepEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    val ranges = remember { mutableStateListOf<Pair<Long, Long>>() }
    var start by remember { mutableStateOf<Long?>(null) }
    var silence by remember { mutableStateOf(false) }
    EditorFrame("Bleep Audio", "Save", enabled = ranges.isNotEmpty(), onBack = onBack,
                onAction = { onRun(mapOf("ranges" to ranges.joinToString("|") { "${it.first},${it.second}" }, "sound" to if (silence) "silence" else "tone")) },
                secondary = if (ranges.isNotEmpty()) "Clear" to { ranges.clear(); start = null } else null) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Transport(player)
                Box(Modifier.weight(1f))
                OutlinedButton(onClick = {
                    val at = player.position
                    val begin = start
                    if (begin == null) start = at else { if (at > begin) ranges += begin to at else if (at < begin) ranges += at to begin; start = null }
                }) { Text(L10n.t(if (start == null) "Mark Start" else "Mark End"), color = EditorText) }
            }
            Timeline(file, player) { toX ->
                ranges.forEach { (a, b) -> drawRect(Color(0x99D83B3B), Offset(toX(a), 0f), Size(toX(b) - toX(a), size.height)) }
                start?.let { val x = toX(it); drawRect(Look.Failure, Offset(x - 2.dp.toPx(), 0f), Size(4.dp.toPx(), size.height)) }
            }
            PanelLabel("Sound")
            DarkChoices(listOf(false to L10n.t("Bleep"), true to L10n.t("Silence")), silence) { silence = it }
        }
    }
}

/** Normalize Volume: the loudness to aim for. */
@Composable
fun NormalizeEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val player = rememberMediaPlayer(file)
    var target by remember { mutableStateOf(-16f) }
    EditorFrame("Normalize Volume", "Normalize", onAction = { onRun(mapOf("target" to target.toString())) }, onBack = onBack) {
        Stage { MediaStage(file, player, Modifier.fillMaxSize()) }
        ControlPanel {
            Transport(player)
            PanelLabel("Loudness")
            DarkChoices(listOf(-14f to L10n.t("Loud · −14"), -16f to L10n.t("Standard · −16"), -19f to L10n.t("Podcast · −19"), -23f to L10n.t("Broadcast · −23")), target) { target = it }
            Text(L10n.t("Peaks stay under −1 dB, so nothing clips."), color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall,
                 modifier = Modifier.padding(top = 8.dp))
        }
    }
}
