package com.motalim.adawat.media

import android.media.MediaCodecList
import android.media.MediaFormat
import androidx.media3.common.C
import androidx.media3.common.MimeTypes
import androidx.media3.common.util.UnstableApi
import androidx.media3.muxer.Muxer
import androidx.media3.muxer.SeekableMuxerOutput
import androidx.media3.muxer.WebmMuxer
import androidx.media3.transformer.Composition
import androidx.media3.transformer.DefaultAssetLoaderFactory
import androidx.media3.transformer.DefaultDecoderFactory
import androidx.media3.transformer.DefaultEncoderFactory
import androidx.media3.common.util.Clock
import androidx.media3.datasource.DataSourceBitmapLoader
import androidx.media3.exoplayer.mediacodec.MediaCodecSelector
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.transformer.ExportException
import androidx.media3.transformer.ExportResult
import androidx.media3.transformer.ProgressHolder
import androidx.media3.transformer.Transformer
import androidx.media3.transformer.VideoEncoderSettings
import com.google.common.collect.ImmutableList
import com.motalim.adawat.AdawatApp
import com.motalim.adawat.core.AdawatException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** Media3 Transformer, which uses the phone's hardware encoders, as one suspending call with progress. */
@UnstableApi
object Transcoder {
    /** HEVC when the phone has a hardware encoder for it (smaller at the same quality), H.264 otherwise. */
    val efficientVideo: String by lazy {
        val hardware = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.any { info ->
            info.isEncoder && info.isHardwareAccelerated && info.supportedTypes.any { it.equals(MediaFormat.MIMETYPE_VIDEO_HEVC, true) }
        }
        if (hardware) MimeTypes.VIDEO_H265 else MimeTypes.VIDEO_H264
    }

    /** Hardware decoders first; if one fails partway, the same job runs again on Android's software decoders. */
    suspend fun run(
        composition: Composition,
        output: File,
        videoMime: String? = MimeTypes.VIDEO_H264,
        audioMime: String? = MimeTypes.AUDIO_AAC,
        videoBitrate: Int? = null,
        webm: Boolean = false,
        progress: (Double) -> Unit = {},
    ): ExportResult = try {
        attempt(composition, output, videoMime, audioMime, videoBitrate, webm, software = false, progress)
    } catch (e: DecoderFailed) {
        attempt(composition, output, videoMime, audioMime, videoBitrate, webm, software = true, progress)
    }

    private class DecoderFailed(val exception: ExportException) : Exception(exception)

    /** Android's own software decoders (c2.android.*) ahead of the hardware ones. */
    private val softwareFirst = MediaCodecSelector { mime, secure, tunneling ->
        MediaCodecSelector.DEFAULT.getDecoderInfos(mime, secure, tunneling).sortedBy { if (it.softwareOnly) 0 else 1 }
    }

    private suspend fun attempt(
        composition: Composition,
        output: File,
        videoMime: String?,
        audioMime: String?,
        videoBitrate: Int?,
        webm: Boolean,
        software: Boolean,
        progress: (Double) -> Unit,
    ): ExportResult = withContext(Dispatchers.Main) {
        val context = AdawatApp.instance
        val encoders = DefaultEncoderFactory.Builder(context).setEnableFallback(true)
        videoBitrate?.let { encoders.setRequestedVideoEncoderSettings(VideoEncoderSettings.Builder().setBitrate(it).build()) }
        val builder = Transformer.Builder(context).setEncoderFactory(encoders.build())
        if (software) {
            val decoders = DefaultDecoderFactory.Builder(context).setMediaCodecSelector(softwareFirst).setEnableDecoderFallback(true).build()
            builder.setAssetLoaderFactory(DefaultAssetLoaderFactory(context, decoders, Clock.DEFAULT, DefaultMediaSourceFactory(context), DataSourceBitmapLoader.Builder(context).build()))
        }
        videoMime?.let { builder.setVideoMimeType(it) }
        audioMime?.let { builder.setAudioMimeType(it) }
        if (webm) builder.setMuxerFactory(WebmFactory)
        suspendCancellableCoroutine { continuation ->
            // The progress loop is a child of this scope; it must stop before the result is handed back, or the call never returns.
            var poll: kotlinx.coroutines.Job? = null
            val transformer = builder.addListener(object : Transformer.Listener {
                override fun onCompleted(composition: Composition, exportResult: ExportResult) {
                    poll?.cancel()
                    if (continuation.isActive) continuation.resume(exportResult)
                }

                override fun onError(composition: Composition, exportResult: ExportResult, exportException: ExportException) {
                    poll?.cancel()
                    output.delete()
                    android.util.Log.w("Adawat", "Transformer failed (software decoders: $software)", exportException)
                    val decoding = exportException.errorCode in setOf(ExportException.ERROR_CODE_DECODING_FAILED, ExportException.ERROR_CODE_DECODER_INIT_FAILED)
                    if (continuation.isActive) continuation.resumeWithException(if (decoding && !software) DecoderFailed(exportException) else AdawatException(message(exportException)))
                }
            }).build()
            transformer.start(composition, output.path)
            val holder = ProgressHolder()
            poll = launch {
                // A watchdog: an encoder that stops producing frames would otherwise hold the job forever.
                var last = -1
                var since = System.currentTimeMillis()
                while (true) {
                    if (transformer.getProgress(holder) == Transformer.PROGRESS_STATE_AVAILABLE) {
                        progress(holder.progress / 100.0)
                        if (holder.progress != last) { last = holder.progress; since = System.currentTimeMillis() }
                    }
                    if (System.currentTimeMillis() - since > 90_000) {
                        transformer.cancel()
                        output.delete()
                        if (continuation.isActive) continuation.resumeWithException(AdawatException("The video stopped converting on this phone. Try another format."))
                        return@launch
                    }
                    delay(250)
                }
            }
            continuation.invokeOnCancellation {
                poll?.cancel()
                transformer.cancel()
                output.delete()
            }
        }
    }

    private fun message(e: ExportException) = when (e.errorCode) {
        ExportException.ERROR_CODE_DECODING_FORMAT_UNSUPPORTED, ExportException.ERROR_CODE_DECODER_INIT_FAILED -> "This video cannot be read on this phone."
        ExportException.ERROR_CODE_ENCODING_FORMAT_UNSUPPORTED, ExportException.ERROR_CODE_ENCODER_INIT_FAILED -> "This phone cannot write this format."
        ExportException.ERROR_CODE_IO_FILE_NOT_FOUND, ExportException.ERROR_CODE_IO_NO_PERMISSION -> "The file could not be read."
        else -> "The video could not be converted (${e.errorCodeName})."
    }

    /** WebM: VP9 or VP8 video with Opus or Vorbis sound, written by Media3's own muxer. */
    private object WebmFactory : Muxer.Factory {
        override fun create(path: String): Muxer {
            val webm = WebmMuxer.Builder(SeekableMuxerOutput.of(path)).build()
            // WebM has no place for the creation time and other entries Transformer adds, and the muxer throws on them.
            return object : Muxer by webm {
                override fun addMetadataEntry(metadataEntry: androidx.media3.common.Metadata.Entry) = Unit

                // The WebM muxer insists on a language for every track; encoded tracks often have none.
                override fun addTrack(format: androidx.media3.common.Format): Int =
                    webm.addTrack(if (format.language == null) format.buildUpon().setLanguage("und").build() else format)
            }
        }

        override fun getSupportedSampleMimeTypes(trackType: Int): ImmutableList<String> = when (trackType) {
            C.TRACK_TYPE_VIDEO -> ImmutableList.of(MimeTypes.VIDEO_VP9, MimeTypes.VIDEO_VP8)
            C.TRACK_TYPE_AUDIO -> ImmutableList.of(MimeTypes.AUDIO_OPUS, MimeTypes.AUDIO_VORBIS)
            else -> ImmutableList.of()
        }
    }
}
