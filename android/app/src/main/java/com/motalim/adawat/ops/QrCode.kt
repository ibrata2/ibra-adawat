package com.motalim.adawat.ops

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import com.google.zxing.EncodeHintType
import com.google.zxing.WriterException
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import com.google.zxing.qrcode.encoder.Encoder
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import java.io.File

/** QR codes from text, links and contact cards. */
object QrCode {
    val fileExtensions = setOf("txt", "md", "markdown", "url", "vcf", "webloc")

    fun canEncode(files: List<File>) = files.size == 1 && files[0].ext in fileExtensions

    /** The text inside a text file, a Windows internet shortcut (.url), a Safari link (.webloc) or a contact card. */
    fun text(file: File): String? = runCatching {
        when (file.ext) {
            "url" -> file.readLines().firstOrNull { it.startsWith("URL=", ignoreCase = true) }?.substring(4)
            "webloc" -> Regex("<key>URL</key>\\s*<string>([^<]*)</string>").find(file.readText())?.groupValues?.get(1)
            else -> file.readText().trim()
        }
    }.getOrNull()

    fun matrix(text: String, level: ErrorCorrectionLevel = ErrorCorrectionLevel.M) = try {
        Encoder.encode(text, level, mapOf(EncodeHintType.CHARACTER_SET to "UTF-8")).matrix
    } catch (_: WriterException) {
        null
    }

    /** Square or round modules, in [foreground] on [background], with the standard four-module margin. */
    fun render(text: String, size: Int = 1024, level: ErrorCorrectionLevel = ErrorCorrectionLevel.M,
               foreground: Int = Color.BLACK, background: Int = Color.WHITE, dots: Boolean = false): Bitmap {
        val matrix = matrix(text, level) ?: throw AdawatException("This is too long for a QR code. Shorten it or lower the error correction.")
        val modules = matrix.width + 8
        val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.drawColor(background)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = foreground }
        val unit = size.toFloat() / modules
        for (y in 0 until matrix.height) for (x in 0 until matrix.width) {
            if (matrix.get(x, y).toInt() == 1) {
                // Edges are rounded to whole pixels so neighbouring modules meet without seams.
                val left = Math.round((x + 4) * unit).toFloat()
                val top = Math.round((y + 4) * unit).toFloat()
                val right = Math.round((x + 5) * unit).toFloat()
                val bottom = Math.round((y + 5) * unit).toFloat()
                // The three corner squares stay square so every reader finds them.
                val finder = (x < 7 && y < 7) || (x >= matrix.width - 7 && y < 7) || (x < 7 && y >= matrix.height - 7)
                if (dots && !finder) canvas.drawCircle((left + right) / 2, (top + bottom) / 2, (right - left) * 0.46f, paint)
                else canvas.drawRect(left, top, right, bottom, paint)
            }
        }
        return bitmap
    }

    /** Without the screen: a 1024 px PNG with the default style. */
    suspend fun quick(files: List<File>, context: OperationContext): List<File> {
        val file = files[0]
        val text = context.options["text"] ?: text(file)
        if (text.isNullOrEmpty()) throw AdawatException("No text found")
        val output = context.pathFor(file, "png", "QR")
        val level = ErrorCorrectionLevel.valueOf(context.text("level", "M"))
        ImageOps.write(render(text, context.int("size", 1024), level, context.int("foreground", Color.BLACK),
                              context.int("background", Color.WHITE), context.bool("dots", false)), ImageFormat.Png, output)
        return listOf(output)
    }
}
