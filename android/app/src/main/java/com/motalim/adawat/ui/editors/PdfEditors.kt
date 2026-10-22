package com.motalim.adawat.ui.editors

import android.graphics.Bitmap
import android.graphics.Color as AndroidColor
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import com.motalim.adawat.core.FileKind
import com.motalim.adawat.core.L10n
import com.motalim.adawat.core.kind
import com.motalim.adawat.ops.Images
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ui.iconText
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

private data class Slot(val id: Int, val source: Int, val rotation: Int = 0)

/** Organize Pages: page cards to drag into a new order (hold, then move), turn, copy or delete. */
@Composable
fun OrganizeEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val count = remember(file) { runCatching { ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { PdfRenderer(it).use { r -> r.pageCount } } }.getOrDefault(0) }
    val slots = remember { mutableStateListOf<Slot>().apply { addAll(List(count) { Slot(it, it) }) } }
    var nextId by remember { mutableStateOf(count) }
    var equal by remember { mutableStateOf(false) }
    val thumbnails = remember { mutableStateMapOf<Int, Bitmap>() }
    // Thumbnails are drawn once, in the background, page by page.
    produceState(0, file) {
        withContext(Dispatchers.IO) {
            runCatching {
                ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
                    PdfRenderer(descriptor).use { renderer ->
                        for (i in 0 until renderer.pageCount) renderer.openPage(i).use { page ->
                            val scale = 260f / maxOf(page.width, page.height)
                            val bitmap = Bitmap.createBitmap((page.width * scale).toInt().coerceAtLeast(1), (page.height * scale).toInt().coerceAtLeast(1), Bitmap.Config.ARGB_8888)
                            bitmap.eraseColor(AndroidColor.WHITE)
                            page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                            thumbnails[i] = bitmap
                        }
                    }
                }
            }
        }
    }
    val changed = slots.map { it.source } != (0 until count).toList() || slots.any { it.rotation != 0 } || equal
    val grid = rememberLazyGridState()
    var dragging by remember { mutableStateOf<Int?>(null) }
    var dragOffset by remember { mutableStateOf(Offset.Zero) }
    EditorFrame("Organize PDF", "Save Organized PDF", enabled = changed && slots.isNotEmpty(),
                onAction = { onRun(mapOf("pages" to slots.joinToString(",") { "${it.source}:${it.rotation}" }, "equalWidths" to equal.toString())) },
                onBack = onBack, secondary = "Reset Order" to { slots.clear(); slots.addAll(List(count) { Slot(it, it) }); equal = false }) {
        Text(L10n.t(if (slots.size == 1) "1 page" else "${slots.size} pages") + " · " + L10n.t("Hold a page to move it."),
             color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(horizontal = 16.dp))
        LazyVerticalGrid(GridCells.Adaptive(108.dp), state = grid, modifier = Modifier.weight(1f).fillMaxWidth().padding(8.dp)) {
            itemsIndexed(slots, key = { _, slot -> slot.id }) { index, slot ->
                val lifted = dragging == slot.id
                var menu by remember { mutableStateOf(false) }
                Box(
                    Modifier.padding(6.dp).aspectRatio(0.78f)
                        .zIndex(if (lifted) 1f else 0f)
                        .graphicsLayer {
                            if (lifted) { translationX = dragOffset.x; translationY = dragOffset.y; scaleX = 1.06f; scaleY = 1.06f; alpha = 0.9f }
                        }
                        .pointerInput(slot.id) {
                            detectDragGesturesAfterLongPress(
                                onDragStart = { dragging = slot.id; dragOffset = Offset.Zero },
                                onDrag = { change, amount ->
                                    change.consume()
                                    dragOffset += amount
                                    // Over another card: move into its place, keeping the card under the finger.
                                    val info = grid.layoutInfo.visibleItemsInfo
                                    val me = info.firstOrNull { it.key == slot.id } ?: return@detectDragGesturesAfterLongPress
                                    val center = Offset(me.offset.x + me.size.width / 2f + dragOffset.x, me.offset.y + me.size.height / 2f + dragOffset.y)
                                    val over = info.firstOrNull { it.key != slot.id && center.x in it.offset.x.toFloat()..(it.offset.x + it.size.width).toFloat() &&
                                                                  center.y in it.offset.y.toFloat()..(it.offset.y + it.size.height).toFloat() }
                                    if (over != null) {
                                        val from = slots.indexOfFirst { it.id == slot.id }
                                        val to = slots.indexOfFirst { it.id == over.key }
                                        if (from >= 0 && to >= 0) {
                                            slots.add(to, slots.removeAt(from))
                                            dragOffset += Offset((me.offset.x - over.offset.x).toFloat(), (me.offset.y - over.offset.y).toFloat())
                                        }
                                    }
                                },
                                onDragEnd = { dragging = null; dragOffset = Offset.Zero },
                                onDragCancel = { dragging = null; dragOffset = Offset.Zero },
                            )
                        }
                        .clip(RoundedCornerShape(10.dp)).background(Color.White).clickable { menu = true },
                ) {
                    thumbnails[slot.source]?.let {
                        Image(it.asImageBitmap(), null, modifier = Modifier.fillMaxSize().padding(10.dp).rotate(slot.rotation.toFloat()))
                    }
                    Text("⁦${index + 1}⁩", color = Color.White, fontSize = 11.sp, modifier = Modifier.align(Alignment.TopStart).padding(6.dp)
                        .size(22.dp).clip(CircleShape).background(Look.Accent).padding(top = 2.dp), textAlign = androidx.compose.ui.text.style.TextAlign.Center)
                    Text(iconText(''), fontFamily = Look.Icons, color = Color.White, fontSize = 14.sp,
                         modifier = Modifier.align(Alignment.TopEnd).padding(6.dp).size(24.dp).clip(CircleShape).background(Color(0x99000000))
                             .clickable { slots[index] = slot.copy(rotation = (slot.rotation + 90) % 360) }.padding(5.dp))
                    DropdownMenu(menu, { menu = false }) {
                        DropdownMenuItem({ Text(L10n.t("Rotate Clockwise")) }, { slots[index] = slot.copy(rotation = (slot.rotation + 90) % 360); menu = false })
                        DropdownMenuItem({ Text(L10n.t("Duplicate Page")) }, { slots.add(index + 1, slot.copy(id = nextId)); nextId++; menu = false })
                        DropdownMenuItem({ Text(L10n.t("Delete Page")) }, { if (slots.size > 1) slots.removeAt(index); menu = false }, enabled = slots.size > 1)
                    }
                }
            }
        }
        ControlPanel {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(L10n.t("Same width for every page"), color = EditorText, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                Switch(equal, { equal = it })
            }
        }
    }
}

/** The order of files before merging PDFs or joining videos. */
@Composable
fun OrderEditor(files: List<File>, title: String, caption: String, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val order = remember { mutableStateListOf<Int>().apply { addAll(files.indices) } }
    EditorFrame(title, title, onAction = { onRun(mapOf("order" to order.joinToString(","))) }, onBack = onBack) {
        Text(L10n.t(caption), color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(horizontal = 16.dp))
        LazyColumn(Modifier.weight(1f).fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            itemsIndexed(order, key = { _, i -> i }) { position, index ->
                val file = files[index]
                val thumb by produceState<Bitmap?>(null, file) {
                    value = if (file.kind == FileKind.Image) withContext(Dispatchers.IO) { runCatching { Images.decode(file, 160) }.getOrNull() }
                            else if (file.kind == FileKind.Video) withContext(Dispatchers.IO) {
                                runCatching { android.media.MediaMetadataRetriever().use { it.setDataSource(file.path); it.getScaledFrameAtTime(0, 0, 160, 160) } }.getOrNull()
                            } else null
                }
                Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Color(0xFF1C1D21)).padding(horizontal = 12.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("⁦${position + 1}⁩", color = Color(0xFF9A9CA3), modifier = Modifier.padding(end = 10.dp))
                    Box(Modifier.size(44.dp).clip(RoundedCornerShape(6.dp)).background(Color(0xFF2A2B30)), contentAlignment = Alignment.Center) {
                        thumb?.let { Image(it.asImageBitmap(), null, modifier = Modifier.fillMaxSize()) }
                            ?: Text(iconText(if (file.kind == FileKind.Pdf) '' else ''), fontFamily = Look.Icons, color = EditorText)
                    }
                    Text(file.name, color = EditorText, style = MaterialTheme.typography.bodyMedium, maxLines = 1, modifier = Modifier.weight(1f).padding(horizontal = 10.dp))
                    Column {
                        Text(iconText(''), fontFamily = Look.Icons, fontSize = 22.sp, color = if (position > 0) EditorText else Color(0xFF4A4B50),
                             modifier = Modifier.clickable(enabled = position > 0) { order.add(position - 1, order.removeAt(position)) })
                        Text(iconText(''), fontFamily = Look.Icons, fontSize = 22.sp, color = if (position < order.lastIndex) EditorText else Color(0xFF4A4B50),
                             modifier = Modifier.clickable(enabled = position < order.lastIndex) { order.add(position + 1, order.removeAt(position)) })
                    }
                }
            }
        }
    }
}

@Composable
fun MergeEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) =
    OrderEditor(files, "Merge PDFs", "Document order", onRun, onBack)

@Composable
fun JoinEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) =
    OrderEditor(files, "Join Videos", "Playback order", onRun, onBack)
