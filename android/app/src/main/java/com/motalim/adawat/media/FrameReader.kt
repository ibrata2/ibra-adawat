package com.motalim.adawat.media

import android.graphics.Bitmap
import android.graphics.Matrix
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaExtractor
import android.media.MediaFormat
import com.motalim.adawat.core.AdawatException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.io.File
import kotlin.math.roundToInt

/**
 * Decodes a video's frames in order and hands over the ones nearest to the wanted times as small upright pictures.
 * Decoding in order is many times faster than seeking for each frame.
 */
object FrameReader {
    /**
     * Calls [each] with the frame nearest to each of [timesUs] (ascending), scaled to [width] × [height] after rotation.
     * Uses the hardware decoder, and Android's software decoder if the hardware one fails.
     */
    suspend fun read(file: File, timesUs: LongArray, width: Int, height: Int, each: suspend (index: Int, bitmap: Bitmap) -> Unit) {
        try {
            decode(file, timesUs, width, height, software = false, each)
        } catch (e: DecodeFailed) {
            decode(file, timesUs, width, height, software = true, each, startAt = e.delivered)
        }
    }

    private class DecodeFailed(val delivered: Int) : Exception()

    private suspend fun decode(file: File, timesUs: LongArray, width: Int, height: Int, software: Boolean,
                               each: suspend (Int, Bitmap) -> Unit, startAt: Int = 0) {
        if (timesUs.isEmpty() || startAt >= timesUs.size) return
        val extractor = MediaExtractor()
        extractor.setDataSource(file.path)
        var codec: MediaCodec? = null
        var next = startAt
        try {
            val track = (0 until extractor.trackCount).firstOrNull {
                extractor.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith("video/") == true
            } ?: throw AdawatException("This file has no video track")
            extractor.selectTrack(track)
            val format = extractor.getTrackFormat(track)
            val mime = format.getString(MediaFormat.KEY_MIME)!!
            val rotation = if (format.containsKey(MediaFormat.KEY_ROTATION)) format.getInteger(MediaFormat.KEY_ROTATION) else 0
            if (timesUs[startAt] > 0) extractor.seekTo(timesUs[startAt], MediaExtractor.SEEK_TO_PREVIOUS_SYNC)
            val name = decoderName(mime, software)
            val decoder = try { if (name != null) MediaCodec.createByCodecName(name) else MediaCodec.createDecoderByType(mime) }
                          catch (e: Exception) { throw AdawatException("This video cannot be read on this phone.") }
            codec = decoder
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible)
            try {
                decoder.configure(format, null, null, 0)
                decoder.start()
            } catch (e: Exception) {
                if (!software) throw DecodeFailed(next) else throw AdawatException("This video cannot be read on this phone.")
            }
            val info = MediaCodec.BufferInfo()
            var inputDone = false
            var previous: Pair<Long, Bitmap>? = null
            var lastOutput = System.currentTimeMillis()
            while (next < timesUs.size) {
                currentCoroutineContext().ensureActive()
                try {
                    if (!inputDone) {
                        val index = decoder.dequeueInputBuffer(0)
                        if (index >= 0) {
                            val size = extractor.readSampleData(decoder.getInputBuffer(index)!!, 0)
                            if (size < 0) {
                                decoder.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                                inputDone = true
                            } else {
                                decoder.queueInputBuffer(index, 0, size, extractor.sampleTime, 0)
                                extractor.advance()
                            }
                        }
                    }
                    val out = decoder.dequeueOutputBuffer(info, 5_000)
                    if (out < 0) {
                        // A decoder that takes input but gives nothing back is stuck; try the software one, or stop.
                        if (System.currentTimeMillis() - lastOutput > 5_000) {
                            if (!software) throw DecodeFailed(next) else break
                        }
                        continue
                    }
                    lastOutput = System.currentTimeMillis()
                    val ended = info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
                    val time = info.presentationTimeUs
                    // Only frames at or just past a wanted time are converted; the rest are dropped unseen.
                    if (info.size > 0 && time >= timesUs[next] - 20_000) {
                        val image = decoder.getOutputImage(out)
                        val bitmap = image?.let { Yuv.toBitmap(it, width, height, rotation) }
                        image?.close()
                        decoder.releaseOutputBuffer(out, false)
                        if (bitmap != null) {
                            while (next < timesUs.size && timesUs[next] <= time + 20_000) {
                                each(next, bitmap)
                                next++
                            }
                            previous?.second?.recycle()
                            previous = time to bitmap
                        }
                    } else {
                        decoder.releaseOutputBuffer(out, false)
                    }
                    if (ended) break
                } catch (e: IllegalStateException) {
                    if (!software) throw DecodeFailed(next) else throw AdawatException("This video cannot be read on this phone.")
                }
            }
            // Times past the last frame get the last frame.
            previous?.let { (_, last) -> while (next < timesUs.size) { each(next, last); next++ } }
        } finally {
            runCatching { codec?.stop() }
            codec?.release()
            extractor.release()
        }
    }

    private fun decoderName(mime: String, software: Boolean): String? {
        val decoders = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.filter { info ->
            !info.isEncoder && info.supportedTypes.any { it.equals(mime, true) }
        }
        return (if (software) decoders.firstOrNull { it.isSoftwareOnly } else decoders.firstOrNull { !it.isSoftwareOnly } ?: decoders.firstOrNull())?.name
    }
}

/** YUV 4:2:0 frames to pictures, sampling only the pixels the smaller picture needs. */
object Yuv {
    fun toBitmap(image: android.media.Image, width: Int, height: Int, rotation: Int): Bitmap {
        val crop = image.cropRect
        val turned = rotation % 180 != 0
        // Size before rotation.
        val w = if (turned) height else width
        val h = if (turned) width else height
        val y = image.planes[0]
        val u = image.planes[1]
        val v = image.planes[2]
        val yBuf = y.buffer
        val uBuf = u.buffer
        val vBuf = v.buffer
        val pixels = IntArray(w * h)
        val sx = crop.width().toFloat() / w
        val sy = crop.height().toFloat() / h
        for (row in 0 until h) {
            val srcY = crop.top + ((row + 0.5f) * sy).toInt().coerceAtMost(crop.height() - 1)
            val chromaRow = srcY / 2
            for (col in 0 until w) {
                val srcX = crop.left + ((col + 0.5f) * sx).toInt().coerceAtMost(crop.width() - 1)
                val luma = (yBuf.get(srcY * y.rowStride + srcX * y.pixelStride).toInt() and 0xFF) - 16
                val chromaCol = srcX / 2
                val cb = (uBuf.get(chromaRow * u.rowStride + chromaCol * u.pixelStride).toInt() and 0xFF) - 128
                val cr = (vBuf.get(chromaRow * v.rowStride + chromaCol * v.pixelStride).toInt() and 0xFF) - 128
                val c = 1.164f * luma
                val r = (c + 1.596f * cr).roundToInt().coerceIn(0, 255)
                val g = (c - 0.392f * cb - 0.813f * cr).roundToInt().coerceIn(0, 255)
                val b = (c + 2.017f * cb).roundToInt().coerceIn(0, 255)
                pixels[row * w + col] = (0xFF shl 24) or (r shl 16) or (g shl 8) or b
            }
        }
        val bitmap = Bitmap.createBitmap(pixels, w, h, Bitmap.Config.ARGB_8888)
        if (rotation % 360 == 0) return bitmap
        return Bitmap.createBitmap(bitmap, 0, 0, w, h, Matrix().apply { postRotate(rotation.toFloat()) }, true).also { bitmap.recycle() }
    }
}
