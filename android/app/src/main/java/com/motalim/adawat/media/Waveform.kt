package com.motalim.adawat.media

import java.io.File
import kotlin.math.abs
import kotlin.math.max

/** A file's loudness as [count] peaks (0…1), for drawing a waveform. */
object Waveform {
    suspend fun peaks(file: File, count: Int): FloatArray {
        val info = MediaInfo.of(file)
        val peaks = FloatArray(count)
        if (!info.hasAudio || info.durationUs <= 0) return peaks
        var rate = 44100
        var channels = 2
        var frame = 0L
        PcmDecoder.decode(file, onShape = { rate = it.sampleRate; channels = it.channels }) { pcm ->
            val perBucket = max(1L, info.durationUs * rate / 1_000_000 / count)
            for (f in 0 until pcm.frames) {
                var loudest = 0
                for (c in 0 until channels) loudest = max(loudest, abs(pcm.samples[f * channels + c].toInt()))
                val bucket = ((frame / perBucket).toInt()).coerceAtMost(count - 1)
                val v = loudest / 32768f
                if (v > peaks[bucket]) peaks[bucket] = v
                frame++
            }
        }
        return peaks
    }
}
