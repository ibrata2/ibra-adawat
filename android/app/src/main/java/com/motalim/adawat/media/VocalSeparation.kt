package com.motalim.adawat.media

import ai.onnxruntime.OnnxTensor
import ai.onnxruntime.OrtEnvironment
import ai.onnxruntime.OrtSession
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.nio.FloatBuffer
import java.util.stream.IntStream
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * Splits a song into the singer's voice and the music with MDX-Net ("UVR-MDX-NET-Voc_FT" from Ultimate Vocal Remover, MIT),
 * the same model and steps as IBMC for Windows, run on the phone with ONNX Runtime. The model predicts the voice; the music
 * is the original minus the voice. The 66 MB model downloads once into the app's own storage.
 */
object VocalSeparation {
    enum class Part { Vocals, Music }

    private const val MODEL_URL = "https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/UVR-MDX-NET-Voc_FT.onnx"
    private const val RATE = 44100
    private const val N_FFT = 7680
    private const val HOP = 1024
    private const val DIM_F = 3072
    private const val DIM_T = 256
    private const val COMPENSATE = 1.021f
    private const val CHUNK = HOP * (DIM_T - 1)
    private const val TRIM = N_FFT / 2
    private const val GEN = CHUNK - 2 * TRIM
    private const val PLANE = DIM_F * DIM_T

    private val lock = Mutex()
    private var session: OrtSession? = null


    suspend fun vocals(files: List<File>, context: OperationContext): List<File> = run(files, Part.Vocals, context)

    suspend fun music(files: List<File>, context: OperationContext): List<File> = run(files, Part.Music, context)

    private suspend fun run(files: List<File>, part: Part, context: OperationContext): List<File> = lock.withLock {
        withContext(Dispatchers.Default) {
            val session = prepare(context)
            val outputs = mutableListOf<File>()
            for ((index, file) in files.withIndex()) {
                val step = context.progressFor(index, files.size)
                context.report(if (part == Part.Vocals) "Separating the voice ${index + 1} of ${files.size}" else "Removing the voice ${index + 1} of ${files.size}")
                val (left, right) = decode(file)
                if (left.size < RATE / 10) throw AdawatException("This file has no sound to separate")
                val (vocalL, vocalR) = separate(session, left, right) { step(it * 0.9) }
                if (part == Part.Music) for (i in vocalL.indices) {
                    vocalL[i] = left[i] - vocalL[i]
                    vocalR[i] = right[i] - vocalR[i]
                }
                context.report(if (part == Part.Vocals) "Saving the voice" else "Saving the music")
                val ext = if (file.ext in setOf("mp3", "m4a", "wav", "flac", "aiff")) file.ext else "m4a"
                val output = context.pathFor(file, ext, if (part == Part.Vocals) "vocals" else "music")
                save(output, vocalL, vocalR)
                step(1.0)
                outputs += output
            }
            context.finish(
                if (part == Part.Vocals) (if (files.size == 1) "The singer's voice is ready" else "Separated the voice from ${files.size} files")
                else (if (files.size == 1) "The music without the voice is ready" else "Removed the voice from ${files.size} files"))
            outputs
        }
    }

    // Model

    private fun prepare(context: OperationContext): OrtSession {
        session?.let { return it }
        val folder = File(com.motalim.adawat.AdawatApp.instance.filesDir, "models").apply { mkdirs() }
        val model = File(folder, "UVR-MDX-NET-Voc_FT.onnx")
        if (!model.exists()) {
            context.report("Downloading the voice separation model (one time only)")
            download(model) { context.progress(it) }
        }
        val options = OrtSession.SessionOptions().apply {
            setOptimizationLevel(OrtSession.SessionOptions.OptLevel.ALL_OPT)
            setIntraOpNumThreads(max(Runtime.getRuntime().availableProcessors(), 1))
        }
        return OrtEnvironment.getEnvironment().createSession(model.path, options).also { session = it }
    }

    private fun download(target: File, progress: (Double) -> Unit) {
        val part = File(target.path + ".part")
        try {
            var url = URL(MODEL_URL)
            var connection: HttpURLConnection
            var redirects = 0
            while (true) {
                connection = url.openConnection() as HttpURLConnection
                connection.instanceFollowRedirects = false
                connection.connectTimeout = 20_000
                connection.readTimeout = 60_000
                val code = connection.responseCode
                if (code in 300..399 && redirects < 6) {
                    url = URL(url, connection.getHeaderField("Location"))
                    connection.disconnect()
                    redirects++
                    continue
                }
                if (code != 200) throw AdawatException("Download failed. Check the internet connection.")
                break
            }
            val total = connection.contentLengthLong
            connection.inputStream.use { input ->
                part.outputStream().use { output ->
                    val buffer = ByteArray(1 shl 16)
                    var done = 0L
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        output.write(buffer, 0, read)
                        done += read
                        if (total > 0) progress(done.toDouble() / total)
                    }
                }
            }
            if (!part.renameTo(target)) throw AdawatException("Download failed. Check the internet connection.")
        } catch (e: java.io.IOException) {
            part.delete()
            throw AdawatException("Download failed. Check the internet connection.")
        }
    }

    // Separation

    private suspend fun separate(session: OrtSession, left: FloatArray, right: FloatArray, progress: (Double) -> Unit): Pair<FloatArray, FloatArray> {
        val n = left.size
        val pad = GEN + TRIM - n % GEN
        val total = TRIM + n + pad
        val mixL = FloatArray(total).also { System.arraycopy(left, 0, it, TRIM, n) }
        val mixR = FloatArray(total).also { System.arraycopy(right, 0, it, TRIM, n) }
        val outL = FloatArray(n)
        val outR = FloatArray(n)
        val window = DoubleArray(N_FFT) { 0.5 - 0.5 * cos(2 * PI * it / N_FFT) }
        val envelope = DoubleArray(CHUNK + N_FFT)
        for (t in 0 until DIM_T) for (i in 0 until N_FFT) envelope[t * HOP + i] += window[i] * window[i]
        val ffts = ThreadLocal.withInitial { Fft(N_FFT) }
        val chunks = (total - CHUNK) / GEN + 1
        val env = OrtEnvironment.getEnvironment()
        val inputName = session.inputNames.first()

        for (c in 0 until chunks) {
            currentCoroutineContext().ensureActive()
            val start = c * GEN
            val input = FloatArray(4 * PLANE)
            IntStream.range(0, DIM_T).parallel().forEach { t ->
                val fft = ffts.get()
                val re = fft.re; val im = fft.im
                val origin = t * HOP - N_FFT / 2
                for (i in 0 until N_FFT) {
                    // torch's centred "reflect" padding at the chunk edges.
                    var at = origin + i
                    if (at < 0) at = -at else if (at >= CHUNK) at = 2 * (CHUNK - 1) - at
                    re[i] = mixL[start + at] * window[i]
                    im[i] = mixR[start + at] * window[i]
                }
                fft.forward()
                // Two real signals in one transform: L = (Z[k] + conj Z[N-k]) / 2, R = (Z[k] - conj Z[N-k]) / 2i.
                for (k in 0 until DIM_F) {
                    val m = (N_FFT - k) % N_FFT
                    val zr = re[k]; val zi = im[k]; val mr = re[m]; val mi = -im[m]
                    val at = k * DIM_T + t
                    input[at] = ((zr + mr) * 0.5).toFloat()
                    input[PLANE + at] = ((zi + mi) * 0.5).toFloat()
                    input[2 * PLANE + at] = ((zi - mi) * 0.5).toFloat()
                    input[3 * PLANE + at] = (-(zr - mr) * 0.5).toFloat()
                }
            }

            val spec = OnnxTensor.createTensor(env, FloatBuffer.wrap(input), longArrayOf(1, 4, DIM_F.toLong(), DIM_T.toLong())).use { tensor ->
                session.run(mapOf(inputName to tensor)).use { results ->
                    val buffer = (results[0] as OnnxTensor).floatBuffer
                    FloatArray(buffer.remaining()).also { buffer.get(it) }
                }
            }

            val frames = arrayOfNulls<DoubleArray>(DIM_T)
            IntStream.range(0, DIM_T).parallel().forEach { t ->
                val fft = ffts.get()
                val re = fft.re; val im = fft.im
                re.fill(0.0); im.fill(0.0)
                for (k in 0 until DIM_F) {
                    val at = k * DIM_T + t
                    val lr = spec[at].toDouble(); val li = if (k == 0) 0.0 else spec[PLANE + at].toDouble()
                    val rr = spec[2 * PLANE + at].toDouble(); val ri = if (k == 0) 0.0 else spec[3 * PLANE + at].toDouble()
                    // L + iR, both Hermitian: the inverse's real part is L and its imaginary part R.
                    re[k] += lr - ri; im[k] += li + rr
                    if (k > 0) { re[N_FFT - k] += lr + ri; im[N_FFT - k] += -li + rr }
                }
                fft.inverse()
                frames[t] = DoubleArray(2 * N_FFT) { i -> if (i < N_FFT) re[i] * window[i] else im[i - N_FFT] * window[i - N_FFT] }
            }
            val waveL = DoubleArray(CHUNK + N_FFT)
            val waveR = DoubleArray(CHUNK + N_FFT)
            for (t in 0 until DIM_T) {
                val frame = frames[t]!!
                val offset = t * HOP
                for (i in 0 until N_FFT) {
                    waveL[offset + i] += frame[i]
                    waveR[offset + i] += frame[N_FFT + i]
                }
            }
            for (i in 0 until GEN) {
                val target = start + i
                if (target >= n) break
                val at = N_FFT / 2 + TRIM + i
                val norm = max(envelope[at], 1e-8)
                outL[target] = (waveL[at] / norm).toFloat() * COMPENSATE
                outR[target] = (waveR[at] / norm).toFloat() * COMPENSATE
            }
            progress((c + 1).toDouble() / chunks)
        }
        return outL to outR
    }

    // Sound in and out

    /** The whole sound as 44.1 kHz stereo floats. */
    private suspend fun decode(file: File): Pair<FloatArray, FloatArray> {
        var reshaper: Reshaper? = null
        var left = FloatArray(1 shl 20)
        var right = FloatArray(1 shl 20)
        var count = 0
        PcmDecoder.decode(file, onShape = { shape -> reshaper = Reshaper(shape.sampleRate, shape.channels, RATE, 2) }) { pcm ->
            val stereo = reshaper!!.apply(pcm)
            if (count + stereo.frames > left.size) {
                val size = max(left.size * 2, count + stereo.frames)
                left = left.copyOf(size); right = right.copyOf(size)
            }
            for (f in 0 until stereo.frames) {
                left[count + f] = stereo.samples[2 * f] / 32768f
                right[count + f] = stereo.samples[2 * f + 1] / 32768f
            }
            count += stereo.frames
        }
        return left.copyOf(count) to right.copyOf(count)
    }

    private fun save(output: File, left: FloatArray, right: FloatArray) {
        val sink = AudioOps.sinkFor(output.ext, output)
        try {
            val (rate, channels) = sink.accepts(RATE, 2)
            if (rate != RATE || channels != 2) throw AdawatException("This phone cannot write this format.")
            sink.open(RATE, 2)
            val block = 1 shl 14
            var at = 0
            while (at < left.size) {
                val frames = minOf(block, left.size - at)
                val samples = ShortArray(frames * 2)
                for (f in 0 until frames) {
                    samples[2 * f] = (left[at + f] * 32767f).roundToInt().coerceIn(-32768, 32767).toShort()
                    samples[2 * f + 1] = (right[at + f] * 32767f).roundToInt().coerceIn(-32768, 32767).toShort()
                }
                sink.write(Pcm(samples, frames))
                at += frames
            }
            sink.close()
        } catch (e: Exception) {
            sink.abandon()
            throw e
        }
    }

    /** Mixed-radix FFT (factors 4, 2, 3, 5) for the model's 7680-point frames, after KissFFT's layout. */
    private class Fft(private val n: Int) {
        val re = DoubleArray(n)
        val im = DoubleArray(n)
        private val workRe = DoubleArray(n)
        private val workIm = DoubleArray(n)
        private val twRe = DoubleArray(n) { cos(-2 * PI * it / n) }
        private val twIm = DoubleArray(n) { sin(-2 * PI * it / n) }
        private val factors: IntArray
        private val scratchRe = DoubleArray(8)
        private val scratchIm = DoubleArray(8)

        init {
            val list = mutableListOf<Int>()
            var rest = n
            for (p in intArrayOf(4, 2, 3, 5)) while (rest % p == 0) { list += p; rest /= p }
            require(rest == 1) { "FFT size must factor into 2, 3 and 5" }
            factors = list.toIntArray()
        }

        fun forward() {
            System.arraycopy(re, 0, workRe, 0, n)
            System.arraycopy(im, 0, workIm, 0, n)
            transform(0, 0, 1, 0)
        }

        /** Inverse transform, scaled by 1/N. */
        fun inverse() {
            for (i in 0 until n) { workRe[i] = re[i]; workIm[i] = -im[i] }
            transform(0, 0, 1, 0)
            val scale = 1.0 / n
            for (i in 0 until n) { re[i] *= scale; im[i] = -im[i] * scale }
        }

        private fun transform(outAt: Int, inAt: Int, stride: Int, level: Int) {
            val p = factors[level]
            val m = n / (stride * p)
            if (m == 1) {
                for (i in 0 until p) { re[outAt + i] = workRe[inAt + i * stride]; im[outAt + i] = workIm[inAt + i * stride] }
            } else {
                for (q in 0 until p) transform(outAt + q * m, inAt + q * stride, stride * p, level + 1)
            }
            for (u in 0 until m) {
                for (q in 0 until p) { scratchRe[q] = re[outAt + u + q * m]; scratchIm[q] = im[outAt + u + q * m] }
                for (q1 in 0 until p) {
                    val k = u + q1 * m
                    var sr = scratchRe[0]; var si = scratchIm[0]
                    var tw = 0
                    for (q in 1 until p) {
                        tw += stride * k
                        if (tw >= n) tw %= n
                        sr += scratchRe[q] * twRe[tw] - scratchIm[q] * twIm[tw]
                        si += scratchRe[q] * twIm[tw] + scratchIm[q] * twRe[tw]
                    }
                    re[outAt + k] = sr; im[outAt + k] = si
                }
            }
        }
    }
}
