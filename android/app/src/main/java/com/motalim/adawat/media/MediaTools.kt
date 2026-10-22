package com.motalim.adawat.media

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.net.Uri
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.util.UnstableApi
import androidx.media3.effect.BitmapOverlay
import androidx.media3.effect.Crop
import androidx.media3.effect.OverlayEffect
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.EditedMediaItemSequence
import androidx.media3.transformer.Effects
import com.google.common.collect.ImmutableList
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import com.motalim.adawat.ops.ImageFormat
import com.motalim.adawat.ops.ImageOps
import java.io.File
import java.util.Locale
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sin

/** The tools behind the video and audio editors: trim, speed, crop, split, frames, covering areas, and bleeps. */
@UnstableApi
object MediaTools {
    private fun videoExt(file: File) = if (file.ext == "webm") "webm" else "mp4"

    private fun clipped(file: File, fromUs: Long, toUs: Long, effects: Effects = Effects.EMPTY): EditedMediaItem {
        val item = MediaItem.Builder().setUri(Uri.fromFile(file))
            .setClippingConfiguration(MediaItem.ClippingConfiguration.Builder().setStartPositionMs(fromUs / 1000).setEndPositionMs(toUs / 1000).build())
            .build()
        return EditedMediaItem.Builder(item).setEffects(effects).build()
    }

    /**
     * A part of a video, cut exactly where asked through the phone's encoder. If that fails, the part is copied from the
     * key frame before the start instead, which needs no encoder and loses nothing.
     */
    suspend fun cut(file: File, fromUs: Long, toUs: Long, output: File, progress: (Double) -> Unit, context: OperationContext? = null) {
        val info = MediaInfo.of(file)
        if (!info.hasVideo) {
            AudioOps.transcode(file, AudioOps.sinkFor(output.extension, output), progress, fromUs = fromUs, toUs = toUs)
            return
        }
        val webm = output.extension == "webm"
        try {
            Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(clipped(file, fromUs, toUs)).build()).build(), output,
                           videoMime = if (webm) androidx.media3.common.MimeTypes.VIDEO_VP9 else androidx.media3.common.MimeTypes.VIDEO_H264,
                           audioMime = if (webm) androidx.media3.common.MimeTypes.AUDIO_OPUS else androidx.media3.common.MimeTypes.AUDIO_AAC,
                           webm = webm, progress = progress)
        } catch (e: AdawatException) {
            android.util.Log.w("Adawat", "Exact cut failed, copying from the key frame instead: ${e.message}")
            Remux.copy(file, output, webm = webm, fromUs = fromUs, toUs = toUs, progress = progress)
            context?.finish("Cut at the nearest key frame")
        }
    }

    suspend fun trim(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val info = MediaInfo.of(file)
        val from = context.options["start"]?.toLongOrNull() ?: 0L
        val to = context.options["end"]?.toLongOrNull() ?: info.durationUs
        context.report("Trimming")
        val output = context.pathFor(file, if (info.hasVideo) videoExt(file) else AudioOps.outputExt(file), "trimmed")
        cut(file, from, to, output, context::progress, context)
        return listOf(output)
    }

    /** Faster or slower, the sound keeping its pitch. */
    suspend fun speed(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val info = MediaInfo.of(file)
        val speed = context.float("speed", 2f).coerceIn(0.25f, 4f)
        val label = if (speed == speed.roundToInt().toFloat()) "${speed.roundToInt()}x" else String.format(Locale.US, "%.2gx", speed)
        context.report("Changing speed")
        if (!info.hasVideo) {
            val output = context.pathFor(file, AudioOps.outputExt(file), label)
            AudioOps.transcode(file, AudioOps.sinkFor(output.extension, output), context::progress, speed = speed)
            return listOf(output)
        }
        val output = context.pathFor(file, "mp4", label)
        val item = EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(file)))
            .setSpeed(object : androidx.media3.common.audio.SpeedProvider {
                override fun getSpeed(timeUs: Long) = speed
                override fun getNextSpeedChangeTimeUs(timeUs: Long) = C.TIME_UNSET
            }).build()
        Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(item).build()).build(), output, progress = context::progress)
        return listOf(output)
    }

    /** Crops the upright frame to a rectangle (shares 0…1); the sound is kept. */
    suspend fun crop(files: List<File>, context: OperationContext): List<File> {
        val (x, y, w, h) = context.text("area", "0,0,1,1").split(',').map { it.toFloat() }
        // Media3 measures from −1 to 1, with y going up.
        val crop = Crop(x * 2 - 1, (x + w) * 2 - 1, 1 - (y + h) * 2, 1 - y * 2)
        return files.mapIndexed { index, file ->
            context.report("Cropping ${index + 1} of ${files.size}")
            val output = context.pathFor(file, "mp4", "cropped")
            val item = EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(file))).setEffects(Effects(listOf(), listOf(crop))).build()
            Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(item).build()).build(), output, progress = context.progressFor(index, files.size))
            output
        }
    }

    /** Cuts at the chosen points; the kept clips go into a folder, or are joined back into one video. */
    suspend fun split(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val info = MediaInfo.of(file)
        val points = context.text("points", "").split(',').mapNotNull { it.toLongOrNull() }.sorted().filter { it in 50_000 until info.durationUs - 50_000 }
        val removed = context.text("removed", "").split(',').mapNotNull { it.toIntOrNull() }.toSet()
        val join = context.bool("join", false)
        val bounds = listOf(0L) + points + info.durationUs
        val segments = (0 until bounds.size - 1).filter { it !in removed }.map { bounds[it] to bounds[it + 1] }
        if (segments.isEmpty()) throw AdawatException("Keep at least one clip.")
        val ext = if (info.hasVideo) videoExt(file) else AudioOps.outputExt(file)
        if (join) {
            context.report("Joining ${segments.size} clips")
            val output = context.pathFor(file, ext, "edited")
            if (info.hasVideo) {
                val items = segments.map { (a, b) -> clipped(file, a, b) }
                Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(items).build()).build(), output, progress = context::progress)
            } else {
                // Audio: the kept parts written one after another, through the same encoder.
                val sink = AudioOps.sinkFor(ext, output)
                AudioOps.transcodeRanges(file, sink, segments, context::progress)
            }
            return listOf(output)
        }
        val folder = context.named("${file.nameWithoutExtension} clips").apply { mkdirs() }
        segments.forEachIndexed { order, (a, b) ->
            context.report("Saving clip ${order + 1} of ${segments.size}")
            cut(file, a, b, File(folder, String.format(Locale.US, "%s %02d.%s", file.nameWithoutExtension, order + 1, ext)), context.progressFor(order, segments.size))
        }
        context.finish(if (segments.size == 1) "Saved 1 clip" else "Saved ${segments.size} clips")
        return listOf(folder)
    }

    /** Full-size pictures at the chosen times; several go into a folder. */
    suspend fun frames(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val info = MediaInfo.of(file)
        val times = context.text("times", "").split(',').mapNotNull { it.toLongOrNull() }.sorted().distinct()
        if (times.isEmpty()) throw AdawatException("Add a frame to save.")
        val turned = info.rotation % 180 != 0
        val w = if (turned) info.height else info.width
        val h = if (turned) info.width else info.height
        val folder = if (times.size > 1) context.named("${file.nameWithoutExtension} frames").apply { mkdirs() } else null
        val outputs = mutableListOf<File>()
        FrameReader.read(file, times.toLongArray(), w, h) { index, bitmap ->
            context.progress(index.toDouble() / times.size)
            val seconds = String.format(Locale.US, "%.3fs", times[index] / 1e6)
            val output = if (folder != null) File(folder, String.format(Locale.US, "Frame %03d - %s.png", index + 1, seconds)) else context.pathFor(file, "png", seconds)
            ImageOps.write(bitmap, ImageFormat.Png, output)
            if (folder == null) outputs += output
        }
        return folder?.let { listOf(it) } ?: outputs
    }

    /** One covered area: a rectangle of the upright frame (shares), shown from [startUs] to [endUs]. */
    data class Redaction(val x: Float, val y: Float, val w: Float, val h: Float, val startUs: Long, val endUs: Long)

    fun decodeRedactions(text: String) = text.split('|').filter { it.isNotBlank() }.map { part ->
        val v = part.split(',')
        Redaction(v[0].toFloat(), v[1].toFloat(), v[2].toFloat(), v[3].toFloat(), v[4].toLong(), v[5].toLong())
    }

    /** Draws the boxes that are showing at each frame's time over the frame. */
    private class Boxes(private val boxes: List<Redaction>, private val color: Int) : BitmapOverlay() {
        private var width = 1
        private var height = 1
        private var cache: Pair<Set<Int>, Bitmap>? = null

        override fun configure(videoSize: androidx.media3.common.util.Size) {
            super.configure(videoSize)
            width = videoSize.width
            height = videoSize.height
        }

        override fun getBitmap(presentationTimeUs: Long): Bitmap {
            val active = boxes.indices.filter { presentationTimeUs in boxes[it].startUs..boxes[it].endUs }.toSet()
            cache?.let { (set, bitmap) -> if (set == active) return bitmap }
            val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)
            val paint = Paint().apply { this.color = this@Boxes.color }
            for (i in active) {
                val b = boxes[i]
                canvas.drawRect(b.x * width, b.y * height, (b.x + b.w) * width, (b.y + b.h) * height, paint)
            }
            cache = active to bitmap
            return bitmap
        }
    }

    /** Covers each area for its time range with a solid box; the copy keeps no tags or location. */
    suspend fun redact(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val boxes = decodeRedactions(context.text("boxes", ""))
        if (boxes.isEmpty()) throw AdawatException("Draw at least one redaction.")
        context.report("Saving redacted copy")
        val output = context.pathFor(file, "mp4", "redacted")
        val overlay = OverlayEffect(ImmutableList.of<androidx.media3.effect.TextureOverlay>(Boxes(boxes, Color.BLACK)))
        val item = EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(file))).setEffects(Effects(listOf(), listOf(overlay))).build()
        Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(item).build()).build(), output, progress = context::progress)
        return listOf(output)
    }

    /** Replaces each range with a 1 kHz tone (or silence) in a copy of the same format. */
    suspend fun bleep(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val ranges = context.text("ranges", "").split('|').filter { it.contains(',') }.map { r -> r.split(',').let { it[0].toLong() to it[1].toLong() } }
        if (ranges.isEmpty()) throw AdawatException("Mark at least one part to bleep.")
        val silent = context.text("sound", "tone") == "silence"
        context.report("Adding bleeps")
        val output = context.pathFor(file, AudioOps.outputExt(file), "bleeped")
        AudioOps.transcode(file, AudioOps.sinkFor(output.extension, output), context::progress, filter = { samples, first, rate, channels ->
            val frames = samples.size / channels
            for (f in 0 until frames) {
                val us = (first + f) * 1_000_000L / rate
                if (ranges.none { (a, b) -> us in a..b }) continue
                val value = if (silent) 0 else (sin(2 * Math.PI * 1000 * (first + f) / rate) * 9000).toInt()
                for (c in 0 until channels) samples[f * channels + c] = value.toShort()
            }
        })
        return listOf(output)
    }

}
