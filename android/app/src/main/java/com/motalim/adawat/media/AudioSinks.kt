package com.motalim.adawat.media

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.MediaMuxer
import com.motalim.adawat.core.AdawatException
import java.io.File
import java.io.RandomAccessFile
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Somewhere decoded samples go: an encoder and a file. */
interface AudioSink {
    /** The rate and channel count this sink wants; samples are converted to it before [write]. */
    fun accepts(rate: Int, channels: Int): Pair<Int, Int> = rate to channels.coerceAtMost(2)
    fun open(rate: Int, channels: Int)
    fun write(pcm: Pcm)
    fun close()
    /** Removes a half-written file after a failure. */
    fun abandon()
}

/** 16-bit PCM in a RIFF WAVE file. */
class WavSink(private val file: File) : AudioSink {
    private lateinit var out: RandomAccessFile
    private var bytes = 0L
    private var rate = 0
    private var channels = 0

    override fun accepts(rate: Int, channels: Int) = rate to channels

    override fun open(rate: Int, channels: Int) {
        this.rate = rate; this.channels = channels
        out = RandomAccessFile(file, "rw").apply { setLength(0); write(ByteArray(44)) }
    }

    override fun write(pcm: Pcm) {
        val buffer = ByteBuffer.allocate(pcm.samples.size * 2).order(ByteOrder.LITTLE_ENDIAN)
        buffer.asShortBuffer().put(pcm.samples)
        out.write(buffer.array())
        bytes += buffer.capacity()
    }

    override fun close() {
        val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        header.put("RIFF".toByteArray()).putInt((36 + bytes).toInt()).put("WAVE".toByteArray())
        header.put("fmt ".toByteArray()).putInt(16).putShort(1).putShort(channels.toShort()).putInt(rate)
        header.putInt(rate * channels * 2).putShort((channels * 2).toShort()).putShort(16)
        header.put("data".toByteArray()).putInt(bytes.toInt())
        out.seek(0)
        out.write(header.array())
        out.close()
    }

    override fun abandon() { runCatching { out.close() }; file.delete() }
}

/** 16-bit big-endian PCM in an AIFF file. */
class AiffSink(private val file: File) : AudioSink {
    private lateinit var out: RandomAccessFile
    private var bytes = 0L
    private var rate = 0
    private var channels = 0

    override fun accepts(rate: Int, channels: Int) = rate to channels

    override fun open(rate: Int, channels: Int) {
        this.rate = rate; this.channels = channels
        out = RandomAccessFile(file, "rw").apply { setLength(0); write(ByteArray(54)) }
    }

    override fun write(pcm: Pcm) {
        val buffer = ByteBuffer.allocate(pcm.samples.size * 2).order(ByteOrder.BIG_ENDIAN)
        buffer.asShortBuffer().put(pcm.samples)
        out.write(buffer.array())
        bytes += buffer.capacity()
    }

    override fun close() {
        val frames = bytes / (channels * 2)
        val header = ByteBuffer.allocate(54).order(ByteOrder.BIG_ENDIAN)
        header.put("FORM".toByteArray()).putInt((46 + bytes).toInt()).put("AIFF".toByteArray())
        header.put("COMM".toByteArray()).putInt(18).putShort(channels.toShort()).putInt(frames.toInt()).putShort(16)
        header.put(extended(rate.toDouble()))
        header.put("SSND".toByteArray()).putInt((8 + bytes).toInt()).putInt(0).putInt(0)
        out.seek(0)
        out.write(header.array())
        out.close()
    }

    /** The sample rate as an 80-bit IEEE extended number, as AIFF wants it. */
    private fun extended(value: Double): ByteArray {
        val bits = java.lang.Double.doubleToLongBits(value)
        val exponent = ((bits shr 52) and 0x7FF).toInt() - 1023 + 16383
        val mantissa = (bits and 0xFFFFFFFFFFFFFL) or (1L shl 52)
        val bytes = ByteBuffer.allocate(10).order(ByteOrder.BIG_ENDIAN)
        bytes.putShort(exponent.toShort()).putLong(mantissa shl 11)
        return bytes.array()
    }

    override fun abandon() { runCatching { out.close() }; file.delete() }
}

/** MP3 through LAME, VBR quality 2 unless told otherwise. */
class Mp3Sink(private val file: File, private val quality: Int = 2, private val kbps: Int = 0) : AudioSink {
    private var handle = 0L
    private var channels = 2
    private lateinit var out: RandomAccessFile
    private var buffer = ByteArray(1 shl 16)

    // LAME takes the MPEG rates only.
    override fun accepts(rate: Int, channels: Int) =
        (listOf(8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000).firstOrNull { it >= rate } ?: 48000) to channels.coerceIn(1, 2)

    override fun open(rate: Int, channels: Int) {
        this.channels = channels
        handle = Mp3Encoder.open(rate, channels, quality, kbps)
        if (handle == 0L) throw AdawatException("The MP3 encoder could not start.")
        out = RandomAccessFile(file, "rw").apply { setLength(0) }
    }

    override fun write(pcm: Pcm) {
        val needed = (1.25 * pcm.frames + 7200).toInt()
        if (buffer.size < needed) buffer = ByteArray(needed)
        val written = Mp3Encoder.encode(handle, pcm.samples, pcm.frames, channels, buffer)
        if (written < 0) throw AdawatException("The MP3 encoder failed.")
        out.write(buffer, 0, written)
    }

    override fun close() {
        val written = Mp3Encoder.flush(handle, buffer)
        if (written > 0) out.write(buffer, 0, written)
        // The Xing header at the start lets players show the right length for a VBR file.
        val tag = ByteArray(2880)
        val size = Mp3Encoder.header(handle, tag)
        if (size in 1..tag.size) { out.seek(0); out.write(tag, 0, size) }
        out.close()
        Mp3Encoder.close(handle)
        handle = 0
    }

    override fun abandon() {
        runCatching { out.close() }
        if (handle != 0L) Mp3Encoder.close(handle)
        file.delete()
    }
}

/** An Android encoder (AAC, Opus or FLAC) into MP4, Ogg or a bare FLAC stream. */
class CodecSink(private val file: File, private val mime: String, private val bitrate: Int, private val container: Container) : AudioSink {
    enum class Container { Mp4, Ogg, Flac }

    private lateinit var codec: MediaCodec
    private var muxer: MediaMuxer? = null
    private var raw: RandomAccessFile? = null
    private var track = -1
    private var rate = 0
    private var channels = 0
    private var framesQueued = 0L
    private val info = MediaCodec.BufferInfo()

    override fun accepts(rate: Int, channels: Int): Pair<Int, Int> = when (mime) {
        MediaFormat.MIMETYPE_AUDIO_OPUS -> 48000 to channels.coerceIn(1, 2)
        MediaFormat.MIMETYPE_AUDIO_AAC -> rate.coerceAtMost(48000) to channels.coerceIn(1, 2)
        else -> rate.coerceAtMost(48000) to channels.coerceIn(1, 2)
    }

    override fun open(rate: Int, channels: Int) {
        this.rate = rate; this.channels = channels
        val format = MediaFormat.createAudioFormat(mime, rate, channels)
        if (mime == MediaFormat.MIMETYPE_AUDIO_FLAC) format.setInteger(MediaFormat.KEY_FLAC_COMPRESSION_LEVEL, 5)
        else format.setInteger(MediaFormat.KEY_BIT_RATE, bitrate)
        if (mime == MediaFormat.MIMETYPE_AUDIO_AAC) format.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 1 shl 16)
        codec = try { MediaCodec.createEncoderByType(mime) } catch (e: Exception) { throw AdawatException("This phone cannot write this format.") }
        codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        codec.start()
        when (container) {
            Container.Mp4 -> muxer = MediaMuxer(file.path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4)
            Container.Ogg -> muxer = MediaMuxer(file.path, MediaMuxer.OutputFormat.MUXER_OUTPUT_OGG)
            Container.Flac -> raw = RandomAccessFile(file, "rw").apply { setLength(0) }
        }
    }

    override fun write(pcm: Pcm) {
        var offset = 0
        val bytes = ByteBuffer.allocate(pcm.samples.size * 2).order(ByteOrder.nativeOrder())
        bytes.asShortBuffer().put(pcm.samples)
        val data = bytes.array()
        while (offset < data.size) {
            val index = codec.dequeueInputBuffer(10_000)
            if (index >= 0) {
                val input = codec.getInputBuffer(index)!!
                val length = minOf(input.remaining() / (channels * 2) * (channels * 2), data.size - offset)
                input.put(data, offset, length)
                val time = framesQueued * 1_000_000L / rate
                codec.queueInputBuffer(index, 0, length, time, 0)
                framesQueued += length / (channels * 2)
                offset += length
            }
            drain(false)
        }
    }

    private fun drain(ending: Boolean) {
        while (true) {
            val index = codec.dequeueOutputBuffer(info, if (ending) 10_000 else 0)
            when {
                index == MediaCodec.INFO_TRY_AGAIN_LATER -> if (!ending) return
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> muxer?.let { m -> track = m.addTrack(codec.outputFormat); m.start() }
                index >= 0 -> {
                    val buffer = codec.getOutputBuffer(index)!!
                    val config = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                    if (info.size > 0) {
                        if (raw != null) {
                            // A FLAC stream is its "fLaC" header and metadata (the codec config) followed by the frames.
                            val bytes = ByteArray(info.size)
                            buffer.position(info.offset); buffer.get(bytes)
                            raw!!.write(bytes)
                        } else if (!config && track >= 0) {
                            muxer!!.writeSampleData(track, buffer, info)
                        }
                    }
                    codec.releaseOutputBuffer(index, false)
                    if (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
                }
            }
        }
    }

    override fun close() {
        while (true) {
            val index = codec.dequeueInputBuffer(10_000)
            if (index >= 0) {
                codec.queueInputBuffer(index, 0, 0, framesQueued * 1_000_000L / rate, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                break
            }
            drain(false)
        }
        drain(true)
        codec.stop(); codec.release()
        muxer?.apply { stop(); release() }
        raw?.let { file ->
            // The encoder leaves the sample count in STREAMINFO empty; fill it in so players show the length.
            if (file.length() >= 26) {
                file.seek(18)
                val word = ByteArray(8).also { file.readFully(it) }
                var value = ByteBuffer.wrap(word).long
                value = (value and 0xFFFFFFFFFL.inv()) or (framesQueued and 0xFFFFFFFFFL)
                file.seek(18)
                file.write(ByteBuffer.allocate(8).putLong(value).array())
            }
            file.close()
        }
    }

    override fun abandon() {
        runCatching { codec.stop() }; runCatching { codec.release() }
        runCatching { muxer?.release() }
        runCatching { raw?.close() }
        file.delete()
    }
}

/** Converts sample rate and channel count on the way to a sink. */
class Reshaper(private val fromRate: Int, private val fromChannels: Int, private val toRate: Int, private val toChannels: Int) {
    private var position = 0.0
    private var previous = ShortArray(toChannels)

    fun apply(pcm: Pcm): Pcm {
        val mixed = if (fromChannels == toChannels) pcm.samples else ShortArray(pcm.frames * toChannels) { i ->
            val frame = i / toChannels
            val channel = i % toChannels
            if (toChannels == 1) {
                var sum = 0
                for (c in 0 until fromChannels) sum += pcm.samples[frame * fromChannels + c]
                (sum / fromChannels).toShort()
            } else if (fromChannels == 1) pcm.samples[frame]
            else pcm.samples[frame * fromChannels + channel.coerceAtMost(fromChannels - 1)]
        }
        if (fromRate == toRate) return Pcm(mixed, mixed.size / toChannels)
        // Linear interpolation between the previous frame and this one, carrying the last frame across chunks.
        val step = fromRate.toDouble() / toRate
        val frames = mixed.size / toChannels
        if (frames == 0) return Pcm(ShortArray(0), 0)
        val count = maxOf(0, Math.ceil((frames - position) / step).toInt())
        val out = ShortArray(count * toChannels)
        var p = position
        for (n in 0 until count) {
            val base = p.toInt()
            val fraction = p - base
            for (c in 0 until toChannels) {
                val a = if (base == 0) previous[c].toInt() else mixed[(base - 1) * toChannels + c].toInt()
                val b = mixed[base * toChannels + c].toInt()
                out[n * toChannels + c] = (a + (b - a) * fraction).toInt().toShort()
            }
            p += step
        }
        position = p - frames
        for (c in 0 until toChannels) previous[c] = mixed[(frames - 1) * toChannels + c]
        return Pcm(out, count)
    }
}
