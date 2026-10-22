package com.motalim.adawat.media

import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import com.motalim.adawat.core.AdawatException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.io.File
import java.nio.ByteOrder

/** 16-bit interleaved samples, one chunk at a time. */
class Pcm(val samples: ShortArray, val frames: Int)

/** Decodes the first audio track of any file Android can read into 16-bit PCM. */
object PcmDecoder {
    /** Rate and channel count the samples will come in, which can differ from the container's claim. */
    data class Shape(val sampleRate: Int, val channels: Int)

    suspend fun decode(
        file: File,
        fromUs: Long = 0,
        toUs: Long = Long.MAX_VALUE,
        progress: (Double) -> Unit = {},
        onShape: (Shape) -> Unit = {},
        each: suspend (Pcm) -> Unit,
    ) {
        val extractor = MediaExtractor()
        extractor.setDataSource(file.path)
        var codec: MediaCodec? = null
        try {
            val track = (0 until extractor.trackCount).firstOrNull {
                extractor.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith("audio/") == true
            } ?: throw AdawatException("This file has no audio track")
            extractor.selectTrack(track)
            if (fromUs > 0) extractor.seekTo(fromUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC)
            val format = extractor.getTrackFormat(track)
            val duration = if (format.containsKey(MediaFormat.KEY_DURATION)) format.getLong(MediaFormat.KEY_DURATION) else 0L
            val end = minOf(toUs, if (duration > 0) duration else Long.MAX_VALUE)
            val mime = format.getString(MediaFormat.KEY_MIME)!!
            val decoder = try { MediaCodec.createDecoderByType(mime) } catch (e: Exception) { throw AdawatException("This audio format cannot be read on this phone.") }
            codec = decoder
            format.setInteger(MediaFormat.KEY_PCM_ENCODING, AudioFormat.ENCODING_PCM_16BIT)
            decoder.configure(format, null, null, 0)
            decoder.start()
            var shape = Shape(format.getInteger(MediaFormat.KEY_SAMPLE_RATE), format.getInteger(MediaFormat.KEY_CHANNEL_COUNT))
            var shapeSent = false
            var floats = false
            val info = MediaCodec.BufferInfo()
            var inputDone = false
            while (true) {
                currentCoroutineContext().ensureActive()
                if (!inputDone) {
                    val index = decoder.dequeueInputBuffer(0)
                    if (index >= 0) {
                        val buffer = decoder.getInputBuffer(index)!!
                        val size = extractor.readSampleData(buffer, 0)
                        if (size < 0 || extractor.sampleTime > end) {
                            decoder.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                            inputDone = true
                        } else {
                            decoder.queueInputBuffer(index, 0, size, extractor.sampleTime, 0)
                            extractor.advance()
                        }
                    }
                }
                val out = decoder.dequeueOutputBuffer(info, if (inputDone) 10_000 else 1_000)
                when {
                    out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                        val f = decoder.outputFormat
                        shape = Shape(f.getInteger(MediaFormat.KEY_SAMPLE_RATE), f.getInteger(MediaFormat.KEY_CHANNEL_COUNT))
                        floats = f.containsKey(MediaFormat.KEY_PCM_ENCODING) && f.getInteger(MediaFormat.KEY_PCM_ENCODING) == AudioFormat.ENCODING_PCM_FLOAT
                    }
                    out >= 0 -> {
                        if (!shapeSent) { onShape(shape); shapeSent = true }
                        val buffer = decoder.getOutputBuffer(out)!!.order(ByteOrder.nativeOrder())
                        buffer.position(info.offset).limit(info.offset + info.size)
                        val samples = if (floats) {
                            val f = buffer.asFloatBuffer()
                            ShortArray(f.remaining()) { (f.get().coerceIn(-1f, 1f) * 32767f).toInt().toShort() }
                        } else {
                            val s = buffer.asShortBuffer()
                            ShortArray(s.remaining()).also { s.get(it) }
                        }
                        decoder.releaseOutputBuffer(out, false)
                        var chunk = samples
                        // Drop what lies before the start of a trimmed range.
                        if (info.presentationTimeUs < fromUs && chunk.isNotEmpty()) {
                            val skipFrames = ((fromUs - info.presentationTimeUs) * shape.sampleRate / 1_000_000).toInt()
                            val skip = minOf(chunk.size, skipFrames * shape.channels)
                            chunk = chunk.copyOfRange(skip, chunk.size)
                        }
                        // And what lies past the end of the range.
                        if (end != Long.MAX_VALUE && chunk.isNotEmpty()) {
                            val startUs = maxOf(info.presentationTimeUs, fromUs)
                            val keepFrames = ((end - startUs) * shape.sampleRate / 1_000_000).coerceAtLeast(0)
                            if (keepFrames < chunk.size / shape.channels) chunk = chunk.copyOfRange(0, (keepFrames * shape.channels).toInt())
                        }
                        if (chunk.isNotEmpty() && info.presentationTimeUs <= end) each(Pcm(chunk, chunk.size / shape.channels))
                        if (duration > 0) progress(((info.presentationTimeUs - fromUs).toDouble() / (minOf(end, duration) - fromUs)).coerceIn(0.0, 1.0))
                        if (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) break
                    }
                }
            }
            if (!shapeSent) onShape(shape)
        } finally {
            runCatching { codec?.stop() }
            codec?.release()
            extractor.release()
        }
    }
}
