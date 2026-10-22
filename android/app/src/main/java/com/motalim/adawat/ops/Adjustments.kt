package com.motalim.adawat.ops

import android.graphics.Bitmap
import com.motalim.adawat.core.OperationContext
import java.io.File
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt
import kotlin.random.Random

/** The photo editor's sliders, the same set and ranges as the desktop apps'. */
enum class Adjust(val title: String, val min: Float, val max: Float, val unit: String = "") {
    Exposure("Exposure", -3f, 3f, " EV"), Brightness("Brightness", -100f, 100f), Contrast("Contrast", -100f, 100f),
    Highlights("Highlights", -100f, 100f), Shadows("Shadows", -100f, 100f), Whites("Whites", -100f, 100f), Blacks("Blacks", -100f, 100f),
    Gamma("Gamma", -100f, 100f),
    Temperature("Temperature", -100f, 100f), Tint("Tint", -100f, 100f), Saturation("Saturation", -100f, 100f), Vibrance("Vibrance", -100f, 100f),
    Hue("Hue", -180f, 180f, "°"),
    Clarity("Clarity", -100f, 100f), Dehaze("Dehaze", -100f, 100f), Sharpness("Sharpness", 0f, 100f), NoiseReduction("Noise Reduction", 0f, 100f),
    Vignette("Vignette", -100f, 100f), Grain("Grain", 0f, 100f), Fade("Fade", 0f, 100f),
}

object Adjustments {
    val sections = listOf(
        "Light" to listOf(Adjust.Exposure, Adjust.Brightness, Adjust.Contrast, Adjust.Highlights, Adjust.Shadows, Adjust.Whites, Adjust.Blacks, Adjust.Gamma),
        "Color" to listOf(Adjust.Temperature, Adjust.Tint, Adjust.Saturation, Adjust.Vibrance, Adjust.Hue),
        "Detail" to listOf(Adjust.Clarity, Adjust.Dehaze, Adjust.Sharpness, Adjust.NoiseReduction),
        "Effects" to listOf(Adjust.Vignette, Adjust.Grain, Adjust.Fade),
    )

    fun encode(values: Map<Adjust, Float>) = values.filterValues { it != 0f }.entries.joinToString(";") { "${it.key.name}=${it.value}" }

    fun decode(text: String): Map<Adjust, Float> = text.split(';').filter { it.contains('=') }.associate { part ->
        val (k, v) = part.split('=')
        Adjust.valueOf(k) to v.toFloat()
    }

    /**
     * Applies every non-zero slider to a copy of [source]. [scale] is the picture's size relative to the full photo,
     * so effects measured in pixels look the same in the preview as in the saved photo.
     */
    fun apply(source: Bitmap, values: Map<Adjust, Float>, scale: Float = 1f): Bitmap {
        fun v(key: Adjust) = values[key] ?: 0f
        val w = source.width
        val h = source.height
        val px = IntArray(w * h).also { source.getPixels(it, 0, w, 0, 0, w, h) }
        if (values.values.all { it == 0f }) return source.copy(Bitmap.Config.ARGB_8888, true)

        // One tone curve carries brightness, contrast, highlights, shadows, whites, blacks, gamma, fade and dehaze.
        val curve = curve(values)
        val exposure = 2.0.pow(v(Adjust.Exposure).toDouble()).toFloat()
        val t = v(Adjust.Temperature) / 100f
        val g = v(Adjust.Tint) / 100f
        // Exposure before the curve, white balance after it, in the desktop's order.
        val rMul = 1 + t * 0.16f
        val gMul = 1 - g * 0.12f
        val bMul = 1 - t * 0.16f
        val saturation = 1 + (v(Adjust.Saturation) + v(Adjust.Vibrance) * 0.6f + v(Adjust.Dehaze) * 0.15f) / 100f
        val hue = v(Adjust.Hue) / 180f * PI.toFloat()
        val cosH = cos(hue); val sinH = sin(hue)
        for (i in px.indices) {
            val p = px[i]
            val a = p ushr 24
            var r = curve[(((p shr 16) and 0xFF) * exposure).toInt().coerceIn(0, 255)] * rMul
            var gg = curve[(((p shr 8) and 0xFF) * exposure).toInt().coerceIn(0, 255)] * gMul
            var b = curve[((p and 0xFF) * exposure).toInt().coerceIn(0, 255)] * bMul
            if (saturation != 1f || hue != 0f) {
                // Rotate the colour around the grey axis (YIQ), then scale its distance from grey.
                val y = 0.299f * r + 0.587f * gg + 0.114f * b
                var iq = 0.596f * r - 0.274f * gg - 0.322f * b
                var q = 0.211f * r - 0.523f * gg + 0.312f * b
                if (hue != 0f) { val ni = iq * cosH - q * sinH; q = iq * sinH + q * cosH; iq = ni }
                iq *= saturation; q *= saturation
                r = y + 0.956f * iq + 0.621f * q
                gg = y - 0.272f * iq - 0.647f * q
                b = y - 1.106f * iq + 1.703f * q
            }
            px[i] = (a shl 24) or (r.roundToInt().coerceIn(0, 255) shl 16) or (gg.roundToInt().coerceIn(0, 255) shl 8) or b.roundToInt().coerceIn(0, 255)
        }
        val minSide = min(w, h)
        if (v(Adjust.NoiseReduction) > 0) {
            val radius = max(1, (v(Adjust.NoiseReduction) / 100f * 3 * scale).roundToInt())
            blend(px, blur(px, w, h, radius), 0.75f * v(Adjust.NoiseReduction) / 100f + 0.25f)
        }
        if (v(Adjust.Clarity) != 0f) {
            // Local contrast: an unsharp mask with a wide radius.
            val radius = max(2, (minSide * 0.02f).roundToInt())
            unsharp(px, blur(px, w, h, radius), v(Adjust.Clarity) / 100f * 0.9f)
        }
        if (v(Adjust.Sharpness) > 0) {
            val radius = max(1, (1.2f * scale).roundToInt())
            unsharp(px, blur(px, w, h, radius), 0.5f + v(Adjust.Sharpness) / 100f)
        }
        if (v(Adjust.Vignette) != 0f) vignette(px, w, h, v(Adjust.Vignette) / 100f)
        if (v(Adjust.Grain) > 0) grain(px, v(Adjust.Grain) / 100f)
        return Bitmap.createBitmap(px, w, h, Bitmap.Config.ARGB_8888).copy(Bitmap.Config.ARGB_8888, true)
    }

    /** 256 steps, built from the light sliders exactly as on the desktop. */
    private fun curve(values: Map<Adjust, Float>): IntArray {
        fun v(key: Adjust) = (values[key] ?: 0f).toDouble()
        val brightness = v(Adjust.Brightness) / 100 * 0.25 - v(Adjust.Dehaze) / 100 * 0.04
        val contrast = 1 + v(Adjust.Contrast) / 100 * 0.5 + v(Adjust.Dehaze) / 100 * 0.25
        val highlights = v(Adjust.Highlights) / 100
        val shadows = v(Adjust.Shadows) / 100
        val whites = v(Adjust.Whites) / 100 * 0.15
        val blacks = v(Adjust.Blacks) / 100 * 0.15 + v(Adjust.Fade) / 100 * 0.18
        val gamma = 2.0.pow(-v(Adjust.Gamma) / 100)
        return IntArray(256) { i ->
            val x = i / 255.0
            val floor = max(0.0, blacks)
            val ceiling = 1 + min(0.0, whites)
            val start = max(0.0, -blacks)
            val end = 1 - max(0.0, whites)
            var y = ((x - start) / max(1e-6, end - start)).coerceIn(0.0, 1.0) * (ceiling - floor) + floor
            y += shadows * 0.35 * (1 - y).pow(2) * y * 2
            y += highlights * 0.35 * y.pow(2) * (1 - y) * 2
            y = y.coerceIn(0.0, 1.0).pow(gamma)
            y = (y - 0.5) * contrast + 0.5 + brightness
            (y.coerceIn(0.0, 1.0) * 255).roundToInt()
        }
    }

    /** A fast blur: three box passes approximate a Gaussian. */
    fun blur(px: IntArray, w: Int, h: Int, radius: Int): IntArray {
        var a = px.copyOf()
        val b = IntArray(px.size)
        repeat(3) {
            boxPass(a, b, w, h, radius, horizontal = true)
            boxPass(b, a, w, h, radius, horizontal = false)
        }
        return a
    }

    private fun boxPass(src: IntArray, dst: IntArray, w: Int, h: Int, r: Int, horizontal: Boolean) {
        val lines = if (horizontal) h else w
        val length = if (horizontal) w else h
        val window = 2 * r + 1
        for (line in 0 until lines) {
            fun at(k: Int) = if (horizontal) line * w + k.coerceIn(0, length - 1) else k.coerceIn(0, length - 1) * w + line
            var sr = 0; var sg = 0; var sb = 0; var sa = 0
            for (k in -r..r) { val p = src[at(k)]; sa += p ushr 24; sr += (p shr 16) and 0xFF; sg += (p shr 8) and 0xFF; sb += p and 0xFF }
            for (k in 0 until length) {
                val target = if (horizontal) line * w + k else k * w + line
                dst[target] = ((sa / window) shl 24) or ((sr / window) shl 16) or ((sg / window) shl 8) or (sb / window)
                val out = src[at(k - r)]; val inn = src[at(k + r + 1)]
                sa += (inn ushr 24) - (out ushr 24)
                sr += ((inn shr 16) and 0xFF) - ((out shr 16) and 0xFF)
                sg += ((inn shr 8) and 0xFF) - ((out shr 8) and 0xFF)
                sb += (inn and 0xFF) - (out and 0xFF)
            }
        }
    }

    private fun unsharp(px: IntArray, blurred: IntArray, amount: Float) {
        for (i in px.indices) {
            val p = px[i]; val q = blurred[i]
            fun ch(shift: Int): Int {
                val o = (p shr shift) and 0xFF
                return (o + (o - ((q shr shift) and 0xFF)) * amount).roundToInt().coerceIn(0, 255)
            }
            px[i] = (p and 0xFF000000.toInt()) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
        }
    }

    private fun blend(px: IntArray, other: IntArray, share: Float) {
        for (i in px.indices) {
            val p = px[i]; val q = other[i]
            fun ch(shift: Int) = (((p shr shift) and 0xFF) * (1 - share) + ((q shr shift) and 0xFF) * share).roundToInt()
            px[i] = (p and 0xFF000000.toInt()) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
        }
    }

    /** Darkens (or, below zero, lightens) towards the corners. */
    private fun vignette(px: IntArray, w: Int, h: Int, amount: Float) {
        val cx = w / 2f; val cy = h / 2f
        val reach = sqrt(cx * cx + cy * cy)
        for (y in 0 until h) for (x in 0 until w) {
            val d = sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / reach
            val falloff = ((d - 0.35f) / 0.65f).coerceIn(0f, 1f).let { it * it }
            val i = y * w + x
            val p = px[i]
            fun ch(shift: Int): Int {
                val c = (p shr shift) and 0xFF
                return if (amount > 0) (c * (1 - amount * falloff)).roundToInt() else (c + (255 - c) * -amount * falloff).roundToInt()
            }
            px[i] = (p and 0xFF000000.toInt()) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
        }
    }

    private fun grain(px: IntArray, amount: Float) {
        val random = Random(7)
        for (i in px.indices) {
            val n = ((random.nextFloat() + random.nextFloat() + random.nextFloat()) / 3f - 0.5f) * 90f * amount
            val p = px[i]
            fun ch(shift: Int) = (((p shr shift) and 0xFF) + n).roundToInt().coerceIn(0, 255)
            px[i] = (p and 0xFF000000.toInt()) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
        }
    }

    suspend fun save(files: List<File>, context: OperationContext): List<File> {
        context.report("Saving edited copy")
        val values = decode(context.text("adjust", ""))
        val file = files[0]
        val format = ImageTools.editedFormat(file)
        val output = context.pathFor(file, format.ext, "edited")
        val edited = apply(Images.decode(file), values)
        ImageOps.write(edited, format, output, quality = 92)
        ImageOps.copyMetadata(file, output)
        return listOf(output)
    }

}
