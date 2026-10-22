package com.motalim.adawat.media

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.net.Uri
import androidx.media3.common.MediaItem
import androidx.media3.common.util.UnstableApi
import androidx.media3.effect.BitmapOverlay
import androidx.media3.effect.OverlayEffect
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.EditedMediaItemSequence
import androidx.media3.transformer.Effects
import com.google.common.collect.ImmutableList
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.ops.ImageFormat
import com.motalim.adawat.ops.ImageOps
import com.motalim.adawat.ops.Images
import java.io.File
import kotlin.math.abs
import kotlin.math.min
import kotlin.math.sqrt

/** Audio as an MP4: an animated waveform in a colour, or a still picture, on the desktop's shapes. */
@UnstableApi
object Visualizer {
    enum class Shape(val title: String, val width: Int, val height: Int) {
        Landscape("Landscape · 16:9", 1920, 1080), Portrait("Portrait · 9:16", 1080, 1920), Square("Square · 1:1", 1080, 1080)
    }

    private const val RATE = 8000
    private const val FPS = 30

    /** The waveform for the moment at [timeUs]: a centre line of the samples that play during that frame. */
    fun drawWave(canvas: Canvas, samples: ShortArray, timeUs: Long, width: Int, height: Int, color: Int) {
        val waveW = (width - width / 8) / 2 * 2
        val waveH = height / 3 / 2 * 2
        val left = (width - waveW) / 2f
        val middle = height / 2f
        val start = (timeUs * RATE / 1_000_000).toInt()
        val count = RATE / FPS
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color; strokeWidth = 2f }
        for (x in 0 until waveW step 2) {
            val i = start + x * count / waveW
            if (i !in samples.indices) continue
            // Square-root scale, so quiet passages still move.
            val v = sqrt(abs(samples[i] / 32768f)) * waveH / 2
            canvas.drawLine(left + x, middle - v, left + x, middle + v, paint)
        }
    }

    private class Wave(private val samples: ShortArray, private val color: Int) : BitmapOverlay() {
        private var bitmap: Bitmap? = null

        override fun getBitmap(presentationTimeUs: Long): Bitmap {
            val size = (bitmap ?: Bitmap.createBitmap(videoWidth, videoHeight, Bitmap.Config.ARGB_8888)).also { bitmap = it }
            size.eraseColor(Color.TRANSPARENT)
            drawWave(Canvas(size), samples, presentationTimeUs, size.width, size.height, color)
            return size
        }

        var videoWidth = 1
        var videoHeight = 1

        override fun configure(videoSize: androidx.media3.common.util.Size) {
            super.configure(videoSize)
            videoWidth = videoSize.width; videoHeight = videoSize.height
        }
    }

    /** The audio at a low rate in one channel, enough to draw a waveform. */
    suspend fun samples(file: File): ShortArray {
        val out = java.io.ByteArrayOutputStream()
        var reshaper: Reshaper? = null
        PcmDecoder.decode(file, onShape = { reshaper = Reshaper(it.sampleRate, it.channels, RATE, 1) }) { pcm ->
            val mono = reshaper!!.apply(pcm)
            val bytes = java.nio.ByteBuffer.allocate(mono.samples.size * 2).order(java.nio.ByteOrder.LITTLE_ENDIAN)
            bytes.asShortBuffer().put(mono.samples)
            out.write(bytes.array())
        }
        val buffer = java.nio.ByteBuffer.wrap(out.toByteArray()).order(java.nio.ByteOrder.LITTLE_ENDIAN).asShortBuffer()
        return ShortArray(buffer.remaining()).also { buffer.get(it) }
    }

    suspend fun make(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val info = MediaInfo.of(file)
        if (!info.hasAudio || info.durationUs <= 0) throw AdawatException("This file has no playable audio.")
        val shape = Shape.valueOf(context.text("shape", "Landscape"))
        val color = context.int("color", 0xFF5B47E6.toInt())
        val image = context.options["image"]?.let(::File)?.takeIf { it.exists() }
        context.report("Creating visualization")
        // The picture behind: black, or the chosen image fitted onto black.
        val background = Bitmap.createBitmap(shape.width, shape.height, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.BLACK) }
        if (image != null) {
            val photo = Images.decode(image, maxOf(shape.width, shape.height))
            val scale = min(shape.width.toFloat() / photo.width, shape.height.toFloat() / photo.height)
            val w = photo.width * scale; val h = photo.height * scale
            Canvas(background).drawBitmap(photo, null, RectF((shape.width - w) / 2, (shape.height - h) / 2, (shape.width + w) / 2, (shape.height + h) / 2),
                                          Paint(Paint.FILTER_BITMAP_FLAG))
        }
        val still = context.named("background.png")
        ImageOps.write(background, ImageFormat.Png, still)
        val effects = if (image == null) Effects(listOf(), listOf(OverlayEffect(ImmutableList.of<androidx.media3.effect.TextureOverlay>(Wave(samples(file), color)))))
                      else Effects.EMPTY
        val picture = EditedMediaItem.Builder(MediaItem.Builder().setUri(Uri.fromFile(still)).setImageDurationMs(info.durationUs / 1000).build())
            .setFrameRate(FPS).setEffects(effects).build()
        val sound = EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(file))).setRemoveVideo(true).build()
        val composition = Composition.Builder(EditedMediaItemSequence.Builder(picture).build(), EditedMediaItemSequence.Builder(sound).build()).build()
        val output = context.pathFor(file, "mp4", "visualizer")
        Transcoder.run(composition, output, progress = context::progress)
        still.delete()
        return listOf(output)
    }
}
