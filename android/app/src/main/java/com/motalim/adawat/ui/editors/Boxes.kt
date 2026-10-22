package com.motalim.adawat.ui.editors

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.motalim.adawat.ui.Look
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/**
 * Boxes over a picture: drag on an empty spot to draw one, drag a box to move it, drag its corner to resize it,
 * tap to select it. Boxes are [Area]s, shares of the picture.
 */
@Composable
fun BoxLayer(boxes: List<Area>, selected: Int?, onChange: (List<Area>, Int?) -> Unit, picture: Rect, fill: Color) {
    val current by rememberUpdatedState(boxes)
    val chosen by rememberUpdatedState(selected)
    val change by rememberUpdatedState(onChange)
    val grab = with(LocalDensity.current) { 26.dp.toPx() }
    fun toShare(p: Offset) = Offset(((p.x - picture.left) / picture.width).coerceIn(0f, 1f), ((p.y - picture.top) / picture.height).coerceIn(0f, 1f))
    fun screen(a: Area) = Rect(picture.left + a.x * picture.width, picture.top + a.y * picture.height,
                               picture.left + (a.x + a.w) * picture.width, picture.top + (a.y + a.h) * picture.height)
    Canvas(
        Modifier.fillMaxSize().systemGestureExclusion()
            .pointerInput(picture) {
                detectTapGestures { at ->
                    val hit = current.indexOfLast { screen(it).inflate(grab / 3).contains(at) }
                    change(current, hit.takeIf { it >= 0 })
                }
            }
            .pointerInput(picture) {
                var mode = 0 // 1 draw, 2 move, 3 resize from the bottom-right corner, 4 from the top-left
                var index = -1
                var anchor = Offset.Zero
                // The list as this gesture changed it; the composed one may not have caught up between events.
                var working = emptyList<Area>()
                detectDragGestures(
                    onDragStart = { at ->
                        val list = current
                        working = list
                        val corner = list.indexOfLast { val r = screen(it); (r.bottomRight - at).getDistance() < grab || (r.topLeft - at).getDistance() < grab }
                        val inside = list.indexOfLast { screen(it).contains(at) }
                        when {
                            corner >= 0 -> {
                                index = corner
                                mode = if ((screen(list[corner]).bottomRight - at).getDistance() < grab) 3 else 4
                            }
                            inside >= 0 -> { index = inside; mode = 2 }
                            else -> {
                                anchor = toShare(at)
                                index = list.size
                                mode = 1
                                working = list + Area(anchor.x, anchor.y, 0f, 0f)
                                change(working, index)
                            }
                        }
                        if (mode != 1) change(list, index)
                    },
                    onDrag = { pointer, drag ->
                        pointer.consume()
                        val list = working.toMutableList()
                        if (index !in list.indices) return@detectDragGestures
                        val a = list[index]
                        val dx = drag.x / picture.width
                        val dy = drag.y / picture.height
                        list[index] = when (mode) {
                            1 -> {
                                val p = toShare(pointer.position)
                                Area(min(anchor.x, p.x), min(anchor.y, p.y), abs(p.x - anchor.x), abs(p.y - anchor.y))
                            }
                            2 -> a.copy(x = (a.x + dx).coerceIn(0f, 1f - a.w), y = (a.y + dy).coerceIn(0f, 1f - a.h))
                            3 -> a.copy(w = (a.w + dx).coerceIn(0.01f, 1f - a.x), h = (a.h + dy).coerceIn(0.01f, 1f - a.y))
                            else -> {
                                val nx = (a.x + dx).coerceIn(0f, a.x + a.w - 0.01f)
                                val ny = (a.y + dy).coerceIn(0f, a.y + a.h - 0.01f)
                                Area(nx, ny, a.w + a.x - nx, a.h + a.y - ny)
                            }
                        }
                        working = list
                        change(list, index)
                    },
                    onDragEnd = {
                        // A tap-sized scribble is not a box.
                        val list = working
                        if (mode == 1 && index in list.indices && (list[index].w < 0.01f || list[index].h < 0.01f)) change(list.dropLast(1), null)
                    },
                )
            },
    ) {
        boxes.forEachIndexed { i, a ->
            val r = screen(a)
            drawRect(fill, r.topLeft, r.size)
            drawRect(if (i == selected) Look.Accent else Color.White, r.topLeft, r.size, style = Stroke(if (i == selected) 3.dp.toPx() else 1.dp.toPx()))
            if (i == selected) {
                listOf(r.topLeft, r.bottomRight).forEach { drawCircle(Color.White, 7.dp.toPx(), it); drawCircle(Look.Accent, 5.dp.toPx(), it) }
            }
        }
    }
}

