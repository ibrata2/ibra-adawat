package com.motalim.adawat.media

import android.media.MediaExtractor
import android.media.MediaFormat
import com.motalim.adawat.core.AdawatException
import java.io.File

/** What a video or audio file holds: its tracks, codecs, size and length. */
data class MediaInfo(
    val durationUs: Long,
    val videoTrack: Int?,
    val videoMime: String?,
    val width: Int,
    val height: Int,
    val rotation: Int,
    val frameRate: Float,
    val videoBitrate: Int?,
    val audioTrack: Int?,
    val audioMime: String?,
    val sampleRate: Int,
    val channels: Int,
    val audioBitrate: Int?,
) {
    val hasVideo get() = videoTrack != null
    val hasAudio get() = audioTrack != null

    companion object {
        fun of(file: File): MediaInfo {
            val extractor = MediaExtractor()
            try {
                extractor.setDataSource(file.path)
            } catch (e: Exception) {
                extractor.release()
                throw AdawatException("Could not read ${file.name}.")
            }
            try {
                var video: Int? = null
                var audio: Int? = null
                var duration = 0L
                for (i in 0 until extractor.trackCount) {
                    val format = extractor.getTrackFormat(i)
                    val mime = format.getString(MediaFormat.KEY_MIME) ?: continue
                    if (format.containsKey(MediaFormat.KEY_DURATION)) duration = maxOf(duration, format.getLong(MediaFormat.KEY_DURATION))
                    if (mime.startsWith("video/") && video == null) video = i
                    if (mime.startsWith("audio/") && audio == null) audio = i
                }
                val v = video?.let { extractor.getTrackFormat(it) }
                val a = audio?.let { extractor.getTrackFormat(it) }
                fun MediaFormat.int(key: String, default: Int = 0) = if (containsKey(key)) getInteger(key) else default
                return MediaInfo(
                    durationUs = duration,
                    videoTrack = video,
                    videoMime = v?.getString(MediaFormat.KEY_MIME),
                    width = v?.int(MediaFormat.KEY_WIDTH) ?: 0,
                    height = v?.int(MediaFormat.KEY_HEIGHT) ?: 0,
                    rotation = v?.int(MediaFormat.KEY_ROTATION) ?: 0,
                    frameRate = v?.let { if (it.containsKey(MediaFormat.KEY_FRAME_RATE)) runCatching { it.getInteger(MediaFormat.KEY_FRAME_RATE).toFloat() }.getOrElse { _ -> it.getFloat(MediaFormat.KEY_FRAME_RATE) } else 30f } ?: 0f,
                    videoBitrate = v?.takeIf { it.containsKey(MediaFormat.KEY_BIT_RATE) }?.getInteger(MediaFormat.KEY_BIT_RATE),
                    audioTrack = audio,
                    audioMime = a?.getString(MediaFormat.KEY_MIME),
                    sampleRate = a?.int(MediaFormat.KEY_SAMPLE_RATE) ?: 0,
                    channels = a?.int(MediaFormat.KEY_CHANNEL_COUNT) ?: 0,
                    audioBitrate = a?.takeIf { it.containsKey(MediaFormat.KEY_BIT_RATE) }?.getInteger(MediaFormat.KEY_BIT_RATE),
                )
            } finally {
                extractor.release()
            }
        }
    }
}
