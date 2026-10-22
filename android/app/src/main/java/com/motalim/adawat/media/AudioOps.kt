package com.motalim.adawat.media

import android.media.MediaFormat
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import java.io.File
import kotlin.math.abs
import kotlin.math.log10
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

/** The formats the wheel offers for video and audio, as on the desktop, less the ones Android cannot write (MOV, MKV). */
class MediaFormatChoice private constructor(val title: String, val ext: String, val audioOnly: Boolean) {
    companion object {
        val Mp4 = MediaFormatChoice("MP4", "mp4", false)
        val WebM = MediaFormatChoice("WebM", "webm", false)
        val Gif = MediaFormatChoice("GIF", "gif", false)
        val Mp3 = MediaFormatChoice("MP3", "mp3", true)
        val M4a = MediaFormatChoice("M4A", "m4a", true)
        val Wav = MediaFormatChoice("WAV", "wav", true)
        val Flac = MediaFormatChoice("FLAC", "flac", true)
        val Aiff = MediaFormatChoice("AIFF", "aiff", true)
        val Ogg = MediaFormatChoice("OGG", "ogg", true)
        val Opus = MediaFormatChoice("Opus", "opus", true)

        val video = listOf(Mp4, WebM, Gif, Mp3, M4a)
        val audio = listOf(Mp3, M4a, Wav, Flac, Aiff, Ogg, Opus)
    }
}

/** Audio conversions and tools: decoded to PCM by the phone, then written by LAME or the phone's own encoders. */
object AudioOps {
    fun sinkFor(ext: String, file: File, mp3Quality: Int = 2, aacBitrate: Int = 256_000, opusBitrate: Int = 160_000, mp3Kbps: Int = 0): AudioSink = when (ext) {
        "mp3" -> Mp3Sink(file, mp3Quality, mp3Kbps)
        "m4a", "aac", "mp4" -> CodecSink(file, MediaFormat.MIMETYPE_AUDIO_AAC, aacBitrate, CodecSink.Container.Mp4)
        "wav" -> WavSink(file)
        "aiff", "aif" -> AiffSink(file)
        "flac" -> CodecSink(file, MediaFormat.MIMETYPE_AUDIO_FLAC, 0, CodecSink.Container.Flac)
        "ogg", "opus", "oga" -> CodecSink(file, MediaFormat.MIMETYPE_AUDIO_OPUS, opusBitrate, CodecSink.Container.Ogg)
        else -> throw AdawatException("This phone cannot write this format.")
    }

    /** Decodes [source] and writes it through [sink], with optional gain, mono mix and time range. */
    suspend fun transcode(
        source: File,
        sink: AudioSink,
        progress: (Double) -> Unit = {},
        gain: Float = 1f,
        mono: Boolean = false,
        stereo: Boolean = false,
        fromUs: Long = 0,
        toUs: Long = Long.MAX_VALUE,
        speed: Float = 1f,
        /** Changes the samples in place, given the first frame's number (in the output's rate) and the shape. */
        filter: ((samples: ShortArray, firstFrame: Long, rate: Int, channels: Int) -> Unit)? = null,
    ) {
        var reshaper: Reshaper? = null
        var sonic: androidx.media3.common.audio.SonicAudioProcessor? = null
        var outRate = 44100
        var outChannels = 2
        var written = 0L
        var opened = false
        try {
            PcmDecoder.decode(source, fromUs, toUs, progress, onShape = { shape ->
                val wanted = if (mono) 1 else if (stereo) 2 else shape.channels
                val (rate, channels) = sink.accepts(shape.sampleRate, wanted)
                val count = if (mono) 1 else channels
                reshaper = Reshaper(shape.sampleRate, shape.channels, rate, count)
                outRate = rate; outChannels = count
                if (speed != 1f) sonic = androidx.media3.common.audio.SonicAudioProcessor().apply {
                    // Sonic changes the speed and keeps the pitch.
                    setSpeed(speed)
                    configure(androidx.media3.common.audio.AudioProcessor.AudioFormat(rate, count, android.media.AudioFormat.ENCODING_PCM_16BIT))
                    flush()
                }
                sink.open(rate, count)
                opened = true
            }) { pcm ->
                var chunk = reshaper!!.apply(pcm)
                sonic?.let { chunk = stretch(it, chunk, outChannels, end = false) }
                write(sink, chunk, gain, filter, written, outRate, outChannels)
                written += chunk.frames
            }
            if (!opened) throw AdawatException("This file has no audio track")
            sonic?.let { write(sink, stretch(it, Pcm(ShortArray(0), 0), outChannels, end = true), gain, filter, written, outRate, outChannels) }
            sink.close()
        } catch (e: Throwable) {
            sink.abandon()
            throw e
        }
    }

    /** Several ranges of one file into one output, in order. */
    suspend fun transcodeRanges(source: File, sink: AudioSink, ranges: List<Pair<Long, Long>>, progress: (Double) -> Unit) {
        // One sink stays open across the ranges: decode each and pass it on.
        val total = ranges.sumOf { it.second - it.first }.coerceAtLeast(1)
        var done = 0L
        var opened = false
        var reshaper: Reshaper? = null
        try {
            for ((from, to) in ranges) {
                PcmDecoder.decode(source, from, to, progress = { progress((done + it * (to - from)) / total) }, onShape = { shape ->
                    if (!opened) {
                        val (rate, channels) = sink.accepts(shape.sampleRate, shape.channels)
                        reshaper = Reshaper(shape.sampleRate, shape.channels, rate, channels)
                        sink.open(rate, channels)
                        opened = true
                    }
                }) { pcm -> reshaper!!.apply(pcm).let { if (it.frames > 0) sink.write(it) } }
                done += to - from
            }
            if (!opened) throw AdawatException("This file has no audio track")
            sink.close()
        } catch (e: Throwable) {
            sink.abandon()
            throw e
        }
    }

    private fun write(sink: AudioSink, chunk: Pcm, gain: Float, filter: ((ShortArray, Long, Int, Int) -> Unit)?, first: Long, rate: Int, channels: Int) {
        if (chunk.frames == 0) return
        val samples = chunk.samples
        if (gain != 1f) for (i in samples.indices) samples[i] = (samples[i] * gain).toInt().coerceIn(-32768, 32767).toShort()
        filter?.invoke(samples, first, rate, channels)
        sink.write(chunk)
    }

    private fun stretch(sonic: androidx.media3.common.audio.SonicAudioProcessor, pcm: Pcm, channels: Int, end: Boolean): Pcm {
        if (pcm.frames > 0) {
            val input = java.nio.ByteBuffer.allocateDirect(pcm.samples.size * 2).order(java.nio.ByteOrder.nativeOrder())
            input.asShortBuffer().put(pcm.samples)
            while (input.hasRemaining()) sonic.queueInput(input)
        }
        if (end) sonic.queueEndOfStream()
        val out = sonic.output.order(java.nio.ByteOrder.nativeOrder()).asShortBuffer()
        val samples = ShortArray(out.remaining()).also { out.get(it) }
        return Pcm(samples, samples.size / channels)
    }

    suspend fun convert(files: List<File>, format: MediaFormatChoice, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Converting ${index + 1} of ${files.size} to ${format.title}")
        val info = MediaInfo.of(file)
        if (!info.hasAudio) throw AdawatException("This file has no audio track")
        val output = context.pathFor(file, format.ext)
        // AAC already fits an M4A: copy it as it is.
        if (format == MediaFormatChoice.M4a && info.audioMime == MediaFormat.MIMETYPE_AUDIO_AAC) {
            Remux.copy(file, output, video = false, progress = context.progressFor(index, files.size))
        } else {
            transcode(file, sinkFor(format.ext, output), context.progressFor(index, files.size))
        }
        output
    }

    // Loudness

    /** Peak and RMS level of the whole file, in dBFS, from a first pass over the samples. */
    data class Levels(val peakDb: Double, val rmsDb: Double)

    suspend fun levels(file: File, progress: (Double) -> Unit = {}): Levels {
        var peak = 0
        var sumSquares = 0.0
        var count = 0L
        // RMS over 400 ms blocks, ignoring near-silent ones, like a simple loudness gate.
        val blockSums = mutableListOf<Double>()
        var block = 0.0
        var blockCount = 0
        var rate = 44100
        var channels = 2
        PcmDecoder.decode(file, progress = progress, onShape = { rate = it.sampleRate; channels = it.channels }) { pcm ->
            val blockSize = rate * channels * 2 / 5
            for (s in pcm.samples) {
                val v = abs(s.toInt())
                if (v > peak) peak = v
                val d = s / 32768.0
                block += d * d
                blockCount++
                if (blockCount == blockSize) { blockSums += block / blockCount; block = 0.0; blockCount = 0 }
            }
        }
        if (blockCount > 0) blockSums += block / blockCount
        val gate = blockSums.filter { it > 1e-7 } // above −70 dBFS
        val meanSquare = if (gate.isEmpty()) 0.0 else gate.average()
        sumSquares = meanSquare; count = 1
        fun db(v: Double) = if (v <= 0) -120.0 else 20 * log10(v)
        return Levels(db(peak / 32768.0), db(sqrt(sumSquares / count)))
    }

    /** Brings speech and music to a steady level (about −16 LUFS) without letting peaks pass −1 dBFS. */
    fun normalizeGain(levels: Levels, target: Double = -16.0): Float {
        val wanted = target - (levels.rmsDb + 0.7) // RMS of program material sits close to its loudness
        val room = -1.0 - levels.peakDb
        val gainDb = min(wanted, room).coerceIn(-20.0, 30.0)
        return 10.0.pow(gainDb / 20).toFloat()
    }

    suspend fun normalize(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Measuring ${index + 1} of ${files.size}")
        val levels = levels(file) { context.progressFor(index, files.size)(it / 2) }
        context.report("Normalizing ${index + 1} of ${files.size}")
        val output = context.pathFor(file, outputExt(file), "normalized")
        transcode(file, sinkFor(output.extension, output), { context.progressFor(index, files.size)(0.5 + it / 2) },
                  gain = normalizeGain(levels, context.float("target", -16f).toDouble()))
        output
    }

    /** Mono by default; the channels screen can ask for stereo, which puts a mono recording on both sides. */
    suspend fun toMono(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Converting ${index + 1} of ${files.size}")
        val stereo = context.text("channels", "mono") == "stereo"
        val output = context.pathFor(file, outputExt(file), if (stereo) "stereo" else "mono")
        transcode(file, sinkFor(output.extension, output), context.progressFor(index, files.size), mono = !stereo, stereo = stereo)
        output
    }

    /** Removes silence at the start and end, below −50 dBFS. */
    suspend fun trimSilence(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Trimming ${index + 1} of ${files.size}")
        val threshold = (32768 * 10.0.pow(-50.0 / 20)).toInt()
        var rate = 44100
        var channels = 2
        var position = 0L
        var first = -1L
        var last = 0L
        PcmDecoder.decode(file, progress = { context.progressFor(index, files.size)(it / 2) }, onShape = { rate = it.sampleRate; channels = it.channels }) { pcm ->
            for (f in 0 until pcm.frames) {
                var loud = false
                for (c in 0 until channels) if (abs(pcm.samples[f * channels + c].toInt()) > threshold) { loud = true; break }
                if (loud) { if (first < 0) first = position; last = position }
                position++
            }
        }
        if (first < 0) throw AdawatException("This file is silent")
        // A little room either side, so words are not clipped.
        val pad = rate / 10
        val fromUs = max(0L, first - pad) * 1_000_000 / rate
        val toUs = (last + pad) * 1_000_000 / rate
        val output = context.pathFor(file, outputExt(file), "trimmed")
        transcode(file, sinkFor(output.extension, output), { context.progressFor(index, files.size)(0.5 + it / 2) }, fromUs = fromUs, toUs = toUs)
        output
    }

    // Compress and clean

    enum class Preset(val share: Double) { Light(0.65), Balanced(0.45), Strong(0.3) }

    /** A share of the original bitrate, between 64 and 320 kbit/s; MP3 and Opus stay themselves, everything else becomes M4A. */
    suspend fun compress(files: List<File>, context: OperationContext, preset: Preset = Preset.valueOf(context.text("preset", "Balanced"))): List<File> {
        var before = 0L
        var after = 0L
        val outputs = files.mapIndexed { index, file ->
            context.report("Compressing ${index + 1} of ${files.size}")
            val info = MediaInfo.of(file)
            if (!info.hasAudio || info.durationUs <= 0) throw AdawatException("${file.name} has no audio track")
            val overall = file.length() * 8.0 / (info.durationUs / 1e6)
            val bitrate = (overall * preset.share).coerceIn(64_000.0, 320_000.0).toInt()
            val ext = when (file.ext) { "mp3" -> "mp3"; "opus", "ogg" -> file.ext; else -> "m4a" }
            val output = context.pathFor(file, ext, "compressed")
            transcode(file, sinkFor(ext, output, aacBitrate = bitrate, opusBitrate = bitrate, mp3Kbps = bitrate / 1000), context.progressFor(index, files.size))
            // Re-encoding an already small file can grow it; keep nothing rather than a bigger copy.
            if (output.length() >= file.length()) { output.delete(); return@mapIndexed null }
            before += file.length()
            after += output.length()
            output
        }.filterNotNull()
        if (outputs.isEmpty()) throw AdawatException("Already as small as this preset gets. Try Strong or a target size.")
        val saved = 100 - (after.toDouble() / before * 100).toInt()
        val label = "${outputs.size} file${if (outputs.size == 1) "" else "s"}"
        context.finish(if (saved > 0) "Compressed $label · $saved% smaller" else "Compressed $label · already as small as it gets")
        return outputs
    }

    /** Tags, cover art and chapters removed. MP4, MP3, FLAC and WAV keep their sound untouched; Ogg is written again. */
    suspend fun stripMetadata(files: List<File>, context: OperationContext): List<File> {
        val outputs = files.mapIndexed { index, file ->
            val progress = context.progressFor(index, files.size)
            when (file.ext) {
                "mp3" -> context.pathFor(file, "mp3", "clean").also { Tags.stripMp3(file, it) }
                "flac" -> context.pathFor(file, "flac", "clean").also { Tags.stripFlac(file, it) }
                "wav" -> context.pathFor(file, "wav", "clean").also { Tags.stripWav(file, it) }
                "m4a", "mp4", "m4v", "mov", "3gp" -> context.pathFor(file, if (file.ext == "m4a") "m4a" else "mp4", "clean").also { Remux.copy(file, it, progress = progress) }
                "webm" -> context.pathFor(file, "webm", "clean").also { Remux.copy(file, it, webm = true, progress = progress) }
                else -> {
                    val ext = outputExt(file)
                    context.pathFor(file, ext, "clean").also { transcode(file, sinkFor(ext, it), progress) }
                }
            }
        }
        context.finish(if (files.size == 1) "Metadata removed from 1 file" else "Metadata removed from ${files.size} files")
        return outputs
    }

    /** Keeps the format where Android can write it; otherwise M4A. */
    fun outputExt(file: File): String = when (file.ext) {
        "mp3", "m4a", "wav", "flac", "aiff", "ogg", "opus" -> file.ext
        "aif" -> "aiff"
        "oga" -> "ogg"
        else -> "m4a"
    }
}
