package com.motalim.adawat.ui.editors

import android.graphics.Bitmap
import android.net.Uri
import androidx.annotation.OptIn
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.MediaItem
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView
import com.motalim.adawat.media.FrameReader
import com.motalim.adawat.media.MediaInfo
import com.motalim.adawat.media.Waveform
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ui.iconText
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.io.File
import java.util.Locale
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/** A player for an editor: ExoPlayer, its position, and play and pause. */
class MediaPlayer(val player: ExoPlayer, val info: MediaInfo) {
    var position by mutableLongStateOf(0L)
    var playing by mutableStateOf(false)
    val durationUs get() = info.durationUs

    fun seek(us: Long) {
        player.seekTo((us / 1000).coerceAtLeast(0))
        position = us
    }

    fun toggle() { if (player.isPlaying) player.pause() else player.play() }
}

@Composable
fun rememberMediaPlayer(file: File): MediaPlayer {
    val context = LocalContext.current
    val info = remember(file) { MediaInfo.of(file) }
    val holder = remember(file) {
        val exo = ExoPlayer.Builder(context).build().apply {
            setMediaItem(MediaItem.fromUri(Uri.fromFile(file)))
            prepare()
        }
        MediaPlayer(exo, info)
    }
    DisposableEffect(holder) {
        val listener = object : Player.Listener {
            override fun onIsPlayingChanged(isPlaying: Boolean) { holder.playing = isPlaying }
        }
        holder.player.addListener(listener)
        onDispose { holder.player.removeListener(listener); holder.player.release() }
    }
    LaunchedEffect(holder) {
        while (isActive) {
            if (holder.player.isPlaying) holder.position = holder.player.currentPosition * 1000
            delay(50)
        }
    }
    return holder
}

/** The video, fitted, with [overlay] given the picture's rectangle on screen. Audio files show nothing here. */
@OptIn(UnstableApi::class)
@Composable
fun VideoPicture(player: MediaPlayer, modifier: Modifier = Modifier, overlay: @Composable (Rect) -> Unit = {}) {
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        val density = LocalDensity.current
        val turned = player.info.rotation % 180 != 0
        val w = (if (turned) player.info.height else player.info.width).coerceAtLeast(1)
        val h = (if (turned) player.info.width else player.info.height).coerceAtLeast(1)
        val boxW = with(density) { maxWidth.toPx() }
        val boxH = with(density) { maxHeight.toPx() }
        val scale = min(boxW / w, boxH / h)
        val rect = Rect(Offset((boxW - w * scale) / 2, (boxH - h * scale) / 2), Size(w * scale, h * scale))
        Box(Modifier.fillMaxSize()) {
            AndroidView(factory = { context ->
                PlayerView(context).apply {
                    useController = false
                    resizeMode = AspectRatioFrameLayout.RESIZE_MODE_FIT
                    this.player = player.player
                    setShutterBackgroundColor(android.graphics.Color.TRANSPARENT)
                }
            }, modifier = Modifier.fillMaxSize())
            overlay(rect)
        }
    }
}

fun clock(us: Long, tenths: Boolean = true): String {
    val total = us / 100_000
    val minutes = total / 600
    val seconds = (total / 10) % 60
    return "⁦" + (if (tenths) String.format(Locale.US, "%d:%02d.%d", minutes, seconds, total % 10) else String.format(Locale.US, "%d:%02d", minutes, seconds)) + "⁩"
}

/** Play or pause, and the current time against the length. */
@Composable
fun Transport(player: MediaPlayer) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(vertical = 4.dp)) {
        Text(iconText(if (player.playing) '' else ''), fontFamily = Look.Icons, fontSize = 26.sp, color = Color.White,
             modifier = Modifier.size(40.dp).clip(CircleShape).background(Color(0xFF2A2B30)).clickable { player.toggle() }.padding(7.dp))
        Text("  " + clock(player.position) + "  /  " + clock(player.durationUs), color = EditorText, style = MaterialTheme.typography.bodyMedium)
    }
}

/** Little pictures along the video, or the waveform of an audio file, to draw a timeline over. */
@Composable
fun TimelineBackground(file: File, info: MediaInfo, modifier: Modifier = Modifier) {
    if (info.hasVideo) {
        val count = 8
        val frames by produceState(List<Bitmap?>(count) { null }, file) {
            val list = MutableList<Bitmap?>(count) { null }
            withContext(Dispatchers.Default) {
                val turned = info.rotation % 180 != 0
                val sw = if (turned) info.height else info.width
                val sh = if (turned) info.width else info.height
                val h = 72
                val w = max(2, (h * sw.toFloat() / sh.coerceAtLeast(1)).roundToInt())
                val times = LongArray(count) { (info.durationUs * (it + 0.5) / count).toLong() }
                runCatching {
                    FrameReader.read(file, times, w, h) { i, bitmap ->
                        list[i] = bitmap.copy(Bitmap.Config.ARGB_8888, false)
                        value = list.toList()
                    }
                }
            }
        }
        Row(modifier.clip(RoundedCornerShape(8.dp)).background(Color(0xFF2A2B30))) {
            frames.forEach { bitmap ->
                Box(Modifier.weight(1f).fillMaxSize()) {
                    bitmap?.let { androidx.compose.foundation.Image(it.asImageBitmap(), null, contentScale = androidx.compose.ui.layout.ContentScale.Crop, modifier = Modifier.fillMaxSize()) }
                }
            }
        }
    } else {
        val peaks by produceState(FloatArray(0), file) { value = withContext(Dispatchers.Default) { runCatching { Waveform.peaks(file, 160) }.getOrDefault(FloatArray(0)) } }
        Canvas(modifier.clip(RoundedCornerShape(8.dp)).background(Color(0xFF2A2B30))) {
            val step = size.width / max(1, peaks.size)
            peaks.forEachIndexed { i, p ->
                val h = max(2f, p * size.height * 0.9f)
                drawRect(Color(0xFF8E83F5), Offset(i * step + step * 0.15f, (size.height - h) / 2), Size(step * 0.7f, h))
            }
        }
    }
}

/**
 * A timeline across the editor: tap or drag to move the playhead. [marks] draws an editor's own layer (ranges, cuts,
 * frames) in the same coordinates, given a function from time to x.
 */
@Composable
fun Timeline(
    file: File,
    player: MediaPlayer,
    modifier: Modifier = Modifier,
    onTap: ((Long) -> Unit)? = null,
    marks: DrawScope.(toX: (Long) -> Float) -> Unit = {},
) {
    val duration = player.durationUs.coerceAtLeast(1)
    val tap by rememberUpdatedState(onTap)
    // Handles near the screen's edge would otherwise start the system's back gesture.
    Box(modifier.fillMaxWidth().padding(horizontal = 32.dp).height(72.dp).systemGestureExclusion()) {
        TimelineBackground(file, player.info, Modifier.fillMaxSize())
        Canvas(Modifier.fillMaxSize()
            .pointerInput(duration) {
                detectTapGestures { at ->
                    val us = (at.x / size.width * duration).toLong().coerceIn(0, duration)
                    player.seek(us)
                    tap?.invoke(us)
                }
            }
            .pointerInput(duration) {
                detectDragGestures { change, _ ->
                    change.consume()
                    player.seek((change.position.x / size.width * duration).toLong().coerceIn(0, duration))
                }
            }) {
            val toX = { us: Long -> us.toFloat() / duration * size.width }
            marks(toX)
            val x = toX(player.position)
            drawLine(Color.White, Offset(x, -6f), Offset(x, size.height + 6f), 3.dp.toPx())
        }
    }
}

/**
 * A range on a timeline with two handles, for Trim and the like: drag a handle to move one end; the playhead follows
 * so the frame at that end shows.
 */
@Composable
fun RangeTimeline(file: File, player: MediaPlayer, start: Long, end: Long, onChange: (Long, Long) -> Unit) {
    val duration = player.durationUs.coerceAtLeast(1)
    val s by rememberUpdatedState(start)
    val e by rememberUpdatedState(end)
    val change by rememberUpdatedState(onChange)
    val grab = with(LocalDensity.current) { 28.dp.toPx() }
    Box(Modifier.fillMaxWidth().padding(horizontal = 32.dp).height(72.dp).systemGestureExclusion()) {
        TimelineBackground(file, player.info, Modifier.fillMaxSize())
        Canvas(Modifier.fillMaxSize().pointerInput(duration) {
            var which = 0
            detectDragGestures(
                onDragStart = { at ->
                    val sx = s.toFloat() / duration * size.width
                    val ex = e.toFloat() / duration * size.width
                    which = when {
                        abs(at.x - sx) < grab && abs(at.x - sx) <= abs(at.x - ex) -> 1
                        abs(at.x - ex) < grab -> 2
                        else -> 0
                    }
                },
                onDrag = { pointer, _ ->
                    pointer.consume()
                    val us = (pointer.position.x / size.width * duration).toLong().coerceIn(0, duration)
                    val gap = 100_000L
                    when (which) {
                        1 -> { val v = min(us, e - gap); change(v, e); player.seek(v) }
                        2 -> { val v = max(us, s + gap); change(s, v); player.seek(v) }
                        else -> player.seek(us)
                    }
                },
            )
        }) {
            val sx = start.toFloat() / duration * size.width
            val ex = end.toFloat() / duration * size.width
            val shade = Color(0xB3000000)
            drawRect(shade, Offset.Zero, Size(sx, size.height))
            drawRect(shade, Offset(ex, 0f), Size(size.width - ex, size.height))
            drawRect(Look.Mint, Offset(sx, 0f), Size(ex - sx, 3.dp.toPx()))
            drawRect(Look.Mint, Offset(sx, size.height - 3.dp.toPx()), Size(ex - sx, 3.dp.toPx()))
            listOf(sx, ex).forEach { x ->
                drawRoundRect(Look.Mint, Offset(x - 6.dp.toPx(), -4f), Size(12.dp.toPx(), size.height + 8f),
                              androidx.compose.ui.geometry.CornerRadius(4.dp.toPx()))
            }
            val px = player.position.toFloat() / duration * size.width
            drawLine(Color.White, Offset(px, -6f), Offset(px, size.height + 6f), 2.dp.toPx())
        }
    }
}

@Composable
fun MediaStage(file: File, player: MediaPlayer, modifier: Modifier = Modifier, overlay: @Composable (Rect) -> Unit = {}) {
    if (player.info.hasVideo) VideoPicture(player, modifier, overlay)
    else Box(modifier, contentAlignment = Alignment.Center) {
        Text(iconText(''), fontFamily = Look.Icons, fontSize = 72.sp, color = Color(0xFF3A3B40))
        Text(file.name, color = EditorText, modifier = Modifier.align(Alignment.BottomCenter).padding(12.dp))
    }
}
