package com.motalim.adawat.media

import android.media.MediaFormat
import android.media.MediaMetadataRetriever
import android.net.Uri
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.audio.ChannelMixingAudioProcessor
import androidx.media3.common.audio.ChannelMixingMatrix
import androidx.media3.common.util.UnstableApi
import androidx.media3.effect.Presentation
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.EditedMediaItemSequence
import androidx.media3.transformer.Effects
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import com.motalim.adawat.ops.Gif
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.io.File
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/** Video conversions and one-step tools: copied where the codecs allow, encoded by the phone's hardware where they do not. */
@UnstableApi
object VideoOps {
    private val mp4Video = setOf(MediaFormat.MIMETYPE_VIDEO_AVC, MediaFormat.MIMETYPE_VIDEO_HEVC, MediaFormat.MIMETYPE_VIDEO_AV1)
    private val webmVideo = setOf(MediaFormat.MIMETYPE_VIDEO_VP8, MediaFormat.MIMETYPE_VIDEO_VP9)
    private val webmAudio = setOf(MediaFormat.MIMETYPE_AUDIO_OPUS, MediaFormat.MIMETYPE_AUDIO_VORBIS)

    private fun item(file: File, effects: Effects = Effects.EMPTY, removeAudio: Boolean = false) =
        EditedMediaItem.Builder(MediaItem.fromUri(Uri.fromFile(file))).setEffects(effects).setRemoveAudio(removeAudio).build()

    private fun single(item: EditedMediaItem) = Composition.Builder(EditedMediaItemSequence.Builder(item).build()).build()

    suspend fun convert(files: List<File>, format: MediaFormatChoice, context: OperationContext): List<File> {
        if (format.audioOnly) return AudioOps.convert(files, format, context)
        return files.mapIndexed { index, file ->
            context.report("Converting ${index + 1} of ${files.size} to ${format.title}")
            val info = MediaInfo.of(file)
            if (!info.hasVideo) throw AdawatException("This file has no video track")
            val progress = context.progressFor(index, files.size)
            val output = context.pathFor(file, format.ext)
            when (format) {
                MediaFormatChoice.Gif -> VideoGif.write(file, info, output, progress)
                MediaFormatChoice.WebM ->
                    if (info.videoMime in webmVideo && (!info.hasAudio || info.audioMime in webmAudio)) Remux.copy(file, output, webm = true, progress = progress)
                    else Transcoder.run(single(item(file)), output, MimeTypes.VIDEO_VP9, MimeTypes.AUDIO_OPUS, webm = true, progress = progress)
                else ->
                    if (info.videoMime in mp4Video && (!info.hasAudio || info.audioMime == MediaFormat.MIMETYPE_AUDIO_AAC)) Remux.copy(file, output, progress = progress)
                    else Transcoder.run(single(item(file)), output, progress = progress)
            }
            output
        }
    }

    enum class Preset(val share: Double) { Light(0.65), Balanced(0.45), Strong(0.3) }

    /** A share of the original bitrate, encoded as HEVC where the phone has a hardware encoder for it. */
    suspend fun compress(files: List<File>, context: OperationContext, preset: Preset = Preset.valueOf(context.text("preset", "Balanced"))): List<File> {
        var before = 0L
        var after = 0L
        val outputs = files.mapIndexed { index, file ->
            context.report("Compressing ${index + 1} of ${files.size}")
            val info = MediaInfo.of(file)
            if (!info.hasVideo || info.durationUs <= 0) throw AdawatException("This file has no video track")
            val overall = file.length() * 8.0 / (info.durationUs / 1e6)
            val video = (info.videoBitrate?.toDouble() ?: (overall - 128_000)).coerceAtLeast(300_000.0)
            val bitrate = max(250_000, (video * preset.share).roundToInt())
            val output = context.pathFor(file, "mp4", "compressed")
            Transcoder.run(single(item(file)), output, Transcoder.efficientVideo, MimeTypes.AUDIO_AAC, videoBitrate = bitrate, progress = context.progressFor(index, files.size))
            before += file.length()
            after += output.length()
            output
        }
        val saved = 100 - (after.toDouble() / before * 100).roundToInt()
        val label = "${files.size} video${if (files.size == 1) "" else "s"}"
        context.finish(if (saved > 0) "Compressed $label · $saved% smaller" else "Compressed $label · already as small as it gets")
        return outputs
    }

    /** A quarter turn clockwise by changing the display rotation, without re-encoding. */
    suspend fun rotate(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        val info = MediaInfo.of(file)
        val webm = file.ext == "webm"
        val output = context.pathFor(file, if (webm) "webm" else "mp4", "rotated")
        if (info.videoMime in mp4Video || (webm && info.videoMime in webmVideo)) {
            Remux.copy(file, output, rotation = info.rotation + 90, webm = webm, progress = context.progressFor(index, files.size))
        } else {
            val turn = androidx.media3.effect.ScaleAndRotateTransformation.Builder().setRotationDegrees(-90f).build()
            Transcoder.run(single(item(file, Effects(listOf(), listOf(turn)))), output, progress = context.progressFor(index, files.size))
        }
        output
    }

    suspend fun mute(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        val info = MediaInfo.of(file)
        val webm = file.ext == "webm"
        val output = context.pathFor(file, if (webm) "webm" else "mp4", "muted")
        if (info.videoMime in mp4Video || (webm && info.videoMime in webmVideo)) Remux.copy(file, output, audio = false, webm = webm, progress = context.progressFor(index, files.size))
        else Transcoder.run(single(item(file, removeAudio = true)), output, progress = context.progressFor(index, files.size))
        output
    }

    /** The sound brought to a steady level; the picture is copied as it is. */
    suspend fun normalize(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Measuring ${index + 1} of ${files.size}")
        val levels = AudioOps.levels(file) { context.progressFor(index, files.size)(it / 3) }
        val gain = AudioOps.normalizeGain(levels, context.float("target", -16f).toDouble())
        context.report("Normalizing ${index + 1} of ${files.size}")
        val mixer = ChannelMixingAudioProcessor()
        for (channels in 1..8) mixer.putChannelMixingMatrix(ChannelMixingMatrix.createForConstantGain(channels, channels).scaleBy(gain))
        val output = context.pathFor(file, "mp4", "normalized")
        Transcoder.run(single(item(file, Effects(listOf(mixer), listOf()))), output, videoMime = null,
                       progress = { context.progressFor(index, files.size)(1 / 3.0 + it * 2 / 3) })
        output
    }

    /** The clips one after another in one MP4, each fitted onto the first clip's frame. */
    suspend fun join(files: List<File>, context: OperationContext): List<File> {
        @Suppress("NAME_SHADOWING") val files = com.motalim.adawat.ops.PdfOps.ordered(files, context)
        context.report("Joining ${files.size} videos")
        val first = MediaInfo.of(files[0])
        val turned = first.rotation % 180 != 0
        val width = if (turned) first.height else first.width
        val height = if (turned) first.width else first.height
        val fit = Presentation.createForWidthAndHeight(width, height, Presentation.LAYOUT_SCALE_TO_FIT)
        val items = files.map { item(it, Effects(listOf(), listOf(fit))) }
        val output = context.named("${files[0].nameWithoutExtension} joined.mp4")
        Transcoder.run(Composition.Builder(EditedMediaItemSequence.Builder(items).build()).build(), output, progress = context::progress)
        context.finish("Joined ${files.size} videos")
        return listOf(output)
    }
}

/** A video as an animated GIF: 12 frames a second, at most 640 pixels wide, one shared palette with ordered dithering. */
object VideoGif {
    suspend fun write(file: File, info: MediaInfo, output: File, progress: (Double) -> Unit) {
        val retriever = MediaMetadataRetriever()
        try {
            retriever.setDataSource(file.path)
            val turned = info.rotation % 180 != 0
            val sourceW = if (turned) info.height else info.width
            val sourceH = if (turned) info.width else info.height
            val scale = min(1.0, 640.0 / sourceW)
            val w = max(2, (sourceW * scale).roundToInt() / 2 * 2)
            val h = max(2, (sourceH * scale).roundToInt() / 2 * 2)
            val durationS = info.durationUs / 1e6
            val count = max(1, (durationS * 12).toInt())
            val times = LongArray(count) { it * 1_000_000L / 12 }
            // Every GIF frame is held until the end, so they are encoded as they come and only the palette is chosen first.
            Gif.writeAnimated(output, w, h, count, delayCentiseconds = (100 / 12.0).roundToInt(),
                sample = { n -> retriever.getScaledFrameAtTime((n * info.durationUs / 8), MediaMetadataRetriever.OPTION_CLOSEST_SYNC, w, h) },
                frames = { deliver ->
                    FrameReader.read(file, times, w, h) { i, bitmap ->
                        progress(i.toDouble() / count)
                        deliver(bitmap)
                    }
                })
        } finally {
            retriever.release()
        }
    }
}
