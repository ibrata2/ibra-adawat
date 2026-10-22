package com.motalim.adawat.ui.editors

import android.graphics.Bitmap
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ops.Images
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/** A rectangle as shares (0…1) of a picture's width and height, from the top left. */
data class Area(val x: Float, val y: Float, val w: Float, val h: Float) {
    fun encode() = "$x,$y,$w,$h"

    companion object {
        val Whole = Area(0f, 0f, 1f, 1f)
        fun decode(text: String) = text.split(',').map { it.toFloat() }.let { Area(it[0], it[1], it[2], it[3]) }
    }
}

/** The first picture, decoded small enough to draw quickly. */
@Composable
fun rememberPreview(file: File, maxSide: Int = 1600): Bitmap? {
    val bitmap by produceState<Bitmap?>(null, file) { value = withContext(Dispatchers.IO) { runCatching { Images.decode(file, maxSide) }.getOrNull() } }
    return bitmap
}

/**
 * Draws [bitmap] fitted into the space, and gives [overlay] the rectangle the picture takes on screen,
 * so editors can draw and take touches in picture coordinates.
 */
@Composable
fun FittedPicture(bitmap: Bitmap?, modifier: Modifier = Modifier, overlay: @Composable (Rect) -> Unit = {}) {
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        if (bitmap == null) {
            CircularProgressIndicator(color = Look.Accent)
            return@BoxWithConstraints
        }
        val density = LocalDensity.current
        val boxW = with(density) { maxWidth.toPx() }
        val boxH = with(density) { maxHeight.toPx() }
        val scale = min(boxW / bitmap.width, boxH / bitmap.height)
        val w = bitmap.width * scale
        val h = bitmap.height * scale
        val rect = Rect(Offset((boxW - w) / 2, (boxH - h) / 2), Size(w, h))
        Box(Modifier.fillMaxSize()) {
            Image(bitmap.asImageBitmap(), null, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize())
            overlay(rect)
        }
    }
}

/**
 * A crop frame over a picture: drag inside to move it, drag a corner or edge to resize. [ratio] (width over height in
 * pixels) locks the shape; [pixelRatio] is the picture's own width over height, to turn pixel ratios into shares.
 */
@Composable
fun CropFrame(area: Area, onChange: (Area) -> Unit, picture: Rect, ratio: Float?, pixelRatio: Float, dim: Boolean = true) {
    val current by rememberUpdatedState(area)
    val change by rememberUpdatedState(onChange)
    val lock by rememberUpdatedState(ratio)
    val density = LocalDensity.current
    val grab = with(density) { 28.dp.toPx() }
    Canvas(
        Modifier.fillMaxSize().systemGestureExclusion().pointerInput(picture) {
            // Which part was grabbed: corners and edges by flags, or the middle to move.
            var left = false; var right = false; var top = false; var bottom = false; var move = false
            detectDragGestures(
                onDragStart = { at ->
                    val a = current
                    val l = picture.left + a.x * picture.width
                    val r = picture.left + (a.x + a.w) * picture.width
                    val t = picture.top + a.y * picture.height
                    val b = picture.top + (a.y + a.h) * picture.height
                    left = abs(at.x - l) < grab && at.y in (t - grab)..(b + grab)
                    right = abs(at.x - r) < grab && at.y in (t - grab)..(b + grab)
                    top = abs(at.y - t) < grab && at.x in (l - grab)..(r + grab)
                    bottom = abs(at.y - b) < grab && at.x in (l - grab)..(r + grab)
                    move = !(left || right || top || bottom) && at.x in l..r && at.y in t..b
                },
                onDrag = { pointer, drag ->
                    pointer.consume()
                    val dx = drag.x / picture.width
                    val dy = drag.y / picture.height
                    var (x, y, w, h) = current
                    val min = 0.04f
                    if (move) {
                        x = (x + dx).coerceIn(0f, 1f - w); y = (y + dy).coerceIn(0f, 1f - h)
                    } else {
                        if (left) { val nx = (x + dx).coerceIn(0f, x + w - min); w += x - nx; x = nx }
                        if (right) w = (w + dx).coerceIn(min, 1f - x)
                        if (top) { val ny = (y + dy).coerceIn(0f, y + h - min); h += y - ny; y = ny }
                        if (bottom) h = (h + dy).coerceIn(min, 1f - y)
                        lock?.let { r ->
                            // Keep the shape: height follows width (in shares, corrected for the picture's own shape).
                            val shareRatio = r / pixelRatio
                            val wantH = w / shareRatio
                            if (wantH <= 1f - y) h = wantH else { h = 1f - y; w = h * shareRatio }
                            if (top && !bottom) y = (y + (current.h - h)).coerceAtLeast(0f)
                        }
                    }
                    change(Area(x, y, w, h))
                },
            )
        },
    ) {
        val a = current
        val l = picture.left + a.x * picture.width
        val t = picture.top + a.y * picture.height
        val w = a.w * picture.width
        val h = a.h * picture.height
        if (dim) {
            val shade = Color(0x99000000)
            drawRect(shade, picture.topLeft, Size(picture.width, t - picture.top))
            drawRect(shade, Offset(picture.left, t + h), Size(picture.width, picture.bottom - t - h))
            drawRect(shade, Offset(picture.left, t), Size(l - picture.left, h))
            drawRect(shade, Offset(l + w, t), Size(picture.right - l - w, h))
        }
        drawRect(Color.White, Offset(l, t), Size(w, h), style = Stroke(2.dp.toPx()))
        // Thirds, to help placing.
        for (i in 1..2) {
            drawLine(Color(0x66FFFFFF), Offset(l + w * i / 3, t), Offset(l + w * i / 3, t + h), 1f)
            drawLine(Color(0x66FFFFFF), Offset(l, t + h * i / 3), Offset(l + w, t + h * i / 3), 1f)
        }
        val corner = 18.dp.toPx()
        val thick = 4.dp.toPx()
        listOf(Offset(l, t) to Offset(1f, 1f), Offset(l + w, t) to Offset(-1f, 1f), Offset(l, t + h) to Offset(1f, -1f), Offset(l + w, t + h) to Offset(-1f, -1f)).forEach { (p, d) ->
            drawLine(Color.White, p, Offset(p.x + corner * d.x, p.y), thick)
            drawLine(Color.White, p, Offset(p.x, p.y + corner * d.y), thick)
        }
    }
}

/** A row of choices on the dark editor panels. */
@Composable
fun <T> DarkChoices(items: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit) {
    Row(
        Modifier.clip(RoundedCornerShape(50)).background(Color(0xFF2A2B30)).padding(3.dp).horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        items.forEach { (value, title) ->
            val on = value == selected
            Text(
                title, color = if (on) Color.White else EditorText, style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.clip(RoundedCornerShape(50)).background(if (on) Look.Accent else Color.Transparent)
                    .clickable { onSelect(value) }.padding(horizontal = 14.dp, vertical = 8.dp),
            )
        }
    }
}

