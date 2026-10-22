package com.motalim.adawat.ops

import android.graphics.Bitmap
import android.graphics.BlurMaskFilter
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.RectF
import android.graphics.Shader
import androidx.exifinterface.media.ExifInterface
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.FileKind
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.kind
import kotlinx.coroutines.ensureActive
import java.io.ByteArrayOutputStream
import java.io.File
import kotlin.coroutines.coroutineContext
import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt

/** The one-step photo tools, with the same defaults as the desktop apps. */
object ImageTools {
    /** The format edited copies are saved in: the original's, unless it cannot be written well. */
    fun editedFormat(file: File): ImageFormat {
        val format = ImageFormat.of(file) ?: ImageFormat.Png
        return if (format == ImageFormat.Gif || format == ImageFormat.Ico || !format.canWrite) ImageFormat.Png else format
    }

    // Compress

    enum class Preset(val title: String, val quality: Int) { Light("Light", 85), Balanced("Balanced", 70), Strong("Strong", 50) }

    /** Lossy formats stay as they are; lossless ones become JPEG, or WebP when they carry transparency. */
    private fun compressedFormat(file: File, bitmap: Bitmap): ImageFormat {
        val format = ImageFormat.of(file)
        if (format != null && format.lossy && format.canWrite) return format
        return if (bitmap.hasAlpha() && hasTransparency(bitmap)) ImageFormat.WebP else ImageFormat.Jpeg
    }

    private fun hasTransparency(bitmap: Bitmap): Boolean {
        val step = max(1, min(bitmap.width, bitmap.height) / 64)
        for (y in 0 until bitmap.height step step) for (x in 0 until bitmap.width step step) if (Color.alpha(bitmap.getPixel(x, y)) < 255) return true
        return false
    }

    fun encode(bitmap: Bitmap, format: ImageFormat, quality: Int): ByteArray = when (format) {
        ImageFormat.Jpeg -> ByteArrayOutputStream().also { Encoders.compress(Images.flatten(bitmap), Bitmap.CompressFormat.JPEG, quality, it) }.toByteArray()
        ImageFormat.WebP -> ByteArrayOutputStream().also { Encoders.compress(bitmap, Bitmap.CompressFormat.WEBP_LOSSY, quality, it) }.toByteArray()
        ImageFormat.Heic -> {
            val temp = File.createTempFile("heic", ".heic")
            try { ImageOps.heic(bitmap, temp, quality); temp.readBytes() } finally { temp.delete() }
        }
        else -> ByteArrayOutputStream().also { Encoders.compress(bitmap, Bitmap.CompressFormat.PNG, 100, it) }.toByteArray()
    }

    suspend fun compress(files: List<File>, context: OperationContext,
                         preset: Preset = Preset.valueOf(context.text("preset", "Balanced")),
                         maxPixels: Int? = context.int("maxPixels", 0).takeIf { it > 0 }): List<File> {
        val outputs = mutableListOf<File>()
        var before = 0L
        var after = 0L
        var skipped = 0
        files.forEachIndexed { index, file ->
            coroutineContext.ensureActive()
            context.report("Compressing ${index + 1} of ${files.size}")
            context.progress(index.toDouble() / files.size)
            val original = file.length()
            val bitmap = Images.decode(file, maxSide = maxPixels ?: 0)
            val format = compressedFormat(file, bitmap)
            val data = encode(bitmap, format, preset.quality)
            // Re-encoding an already tight file can grow it; keep nothing rather than a bigger copy.
            if (maxPixels == null && data.size >= original) {
                skipped++
                return@forEachIndexed
            }
            val output = context.pathFor(file, format.ext, "compressed")
            output.writeBytes(data)
            ImageOps.copyMetadata(file, output)
            before += original
            after += output.length()
            outputs += output
        }
        if (outputs.isEmpty() && skipped > 0) throw AdawatException("Already as small as this preset gets. Try Strong or a target size.")
        context.finish(savings(outputs.size, before, after))
        return outputs
    }

    private fun savings(count: Int, before: Long, after: Long): String {
        val label = "$count image${if (count == 1) "" else "s"}"
        if (before <= 0) return "Compressed $label"
        val saved = 100 - (after.toDouble() / before * 100).roundToInt()
        return if (saved > 0) "Compressed $label · $saved% smaller" else "Compressed $label · already as small as it gets"
    }

    // Crop and rotate

    /** Crops every picture to the same rectangle, given as shares (0…1) of width and height from the top left. */
    suspend fun crop(files: List<File>, rect: RectF, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        coroutineContext.ensureActive()
        context.report("Cropping ${index + 1} of ${files.size}")
        val bitmap = Images.decode(file)
        val x = (rect.left * bitmap.width).roundToInt().coerceIn(0, bitmap.width - 1)
        val y = (rect.top * bitmap.height).roundToInt().coerceIn(0, bitmap.height - 1)
        val w = (rect.width() * bitmap.width).roundToInt().coerceIn(1, bitmap.width - x)
        val h = (rect.height() * bitmap.height).roundToInt().coerceIn(1, bitmap.height - y)
        val format = editedFormat(file)
        val output = context.pathFor(file, format.ext, "cropped")
        ImageOps.write(Bitmap.createBitmap(bitmap, x, y, w, h), format, output, quality = 92)
        ImageOps.copyMetadata(file, output)
        context.progress((index + 1.0) / files.size)
        output
    }

    /** The largest centred square, for one-step runs without the crop screen. */
    suspend fun squareCrop(files: List<File>, context: OperationContext): List<File> = files.flatMap { file ->
        val (w, h) = Images.size(file)
        val side = min(w, h).toFloat()
        val left = (w - side) / 2 / w
        val top = (h - side) / 2 / h
        crop(listOf(file), RectF(left, top, left + side / w, top + side / h), context)
    }

    /** A quarter turn clockwise. JPEGs only change their orientation tag, so no quality is lost. */
    suspend fun rotate(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        coroutineContext.ensureActive()
        val format = ImageFormat.of(file)
        val output: File
        if (format == ImageFormat.Jpeg) {
            output = context.pathFor(file, file.extension.lowercase(), "rotated")
            file.copyTo(output)
            ExifInterface(output).apply {
                // A missing tag reads as "undefined", which rotate() leaves alone.
                if (getAttributeInt(ExifInterface.TAG_ORIENTATION, 0) == ExifInterface.ORIENTATION_UNDEFINED) {
                    setAttribute(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL.toString())
                }
                rotate(90)
                saveAttributes()
            }
        } else {
            val target = format?.takeIf { it.canWrite && it != ImageFormat.Gif } ?: ImageFormat.Png
            output = context.pathFor(file, target.ext, "rotated")
            ImageOps.write(Images.rotate(Images.decode(file), 90f), target, output, quality = 95)
        }
        context.progress((index + 1.0) / files.size)
        output
    }

    /** The crop screen's rectangle, or the largest centred square without it. */
    suspend fun cropTool(files: List<File>, context: OperationContext): List<File> {
        val area = context.options["area"]?.split(',')?.map { it.toFloat() } ?: return squareCrop(files, context)
        return crop(files, RectF(area[0], area[1], area[0] + area[2], area[1] + area[3]), context)
    }

    // Redact

    /** Covers areas with solid boxes or a heavy pixelated blur; the copy keeps no camera or place details either. */
    suspend fun redact(files: List<File>, context: OperationContext): List<File> {
        context.report("Saving redacted copy")
        val file = files[0]
        val boxes = context.text("boxes", "").split('|').filter { it.isNotBlank() }.map { b -> b.split(',').map { it.toFloat() } }
        val blur = context.bool("blur", false)
        val photo = Images.decode(file).copy(Bitmap.Config.ARGB_8888, true)
        val canvas = Canvas(photo)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        for ((x, y, w, h) in boxes) {
            val rect = android.graphics.Rect((x * photo.width).roundToInt(), (y * photo.height).roundToInt(),
                                             ((x + w) * photo.width).roundToInt(), ((y + h) * photo.height).roundToInt())
            if (rect.width() < 1 || rect.height() < 1) continue
            if (blur) {
                // Pixelate first so blurred text cannot be sharpened back.
                val part = Bitmap.createBitmap(photo, rect.left, rect.top, rect.width(), rect.height())
                val tiny = Bitmap.createScaledBitmap(part, max(2, part.width / 16), max(2, part.height / 16), true)
                val back = Bitmap.createScaledBitmap(tiny, part.width, part.height, true)
                val px = IntArray(back.width * back.height).also { back.getPixels(it, 0, back.width, 0, 0, back.width, back.height) }
                val soft = Adjustments.blur(px, back.width, back.height, max(2, min(back.width, back.height) / 12))
                canvas.drawBitmap(Bitmap.createBitmap(soft, back.width, back.height, Bitmap.Config.ARGB_8888), rect.left.toFloat(), rect.top.toFloat(), null)
            } else {
                paint.color = Color.BLACK
                canvas.drawRect(rect, paint)
            }
        }
        val format = editedFormat(file)
        val output = context.pathFor(file, format.ext, "redacted")
        ImageOps.write(photo, format, output, quality = 92)
        return listOf(output)
    }

    // Background

    /** Two-stop diagonal gradients, warm to cool, the same as the desktop apps. */
    val gradients = listOf(
        0xF57354 to 0xFAC754, 0xFA9E8C to 0x9973EB, 0xFFD666 to 0xF26B9E, 0xFAD9E0 to 0xD1CCFA, 0xCC9EEB to 0xFAB8CC,
        0x21546B to 0xF2995C, 0x59A6FA to 0x99E6F2, 0x73D9D1 to 0x4080EB, 0xEBF0FF to 0xADBDFA,
    )

    data class FrameOptions(val gradient: Int = 0, val solid: Int? = null, val blurred: Boolean = false, val padding: Float = 160f,
                            val corners: Float = 24f, val shadow: Float = 24f, val ratio: Float? = null)

    fun frame(photo: Bitmap, options: FrameOptions, scale: Float = 1f): Bitmap {
        val padding = options.padding * scale
        var width = photo.width + padding * 2
        var height = photo.height + padding * 2
        options.ratio?.let { ratio -> if (width / height < ratio) width = height * ratio else height = width / ratio }
        val w = width.roundToInt()
        val h = height.roundToInt()
        if (w.toLong() * h > 150_000_000L) throw AdawatException("This frame would be too large. Use less padding or a closer output ratio.")
        val result = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(result)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        when {
            options.solid != null -> canvas.drawColor(options.solid or 0xFF000000.toInt())
            options.blurred -> {
                val small = Bitmap.createScaledBitmap(photo, max(1, photo.width / 16), max(1, photo.height / 16), true)
                val cover = max(w.toFloat() / small.width, h.toFloat() / small.height)
                val dw = small.width * cover
                val dh = small.height * cover
                canvas.drawBitmap(small, null, RectF((w - dw) / 2, (h - dh) / 2, (w + dw) / 2, (h + dh) / 2), paint)
                canvas.drawColor(0x14000000)
            }
            else -> {
                val (from, to) = gradients[options.gradient % gradients.size]
                // Diagonal: the first colour at the bottom left, the second at the top right.
                paint.shader = LinearGradient(0f, h.toFloat(), w.toFloat(), 0f, from or 0xFF000000.toInt(), to or 0xFF000000.toInt(), Shader.TileMode.CLAMP)
                canvas.drawRect(0f, 0f, w.toFloat(), h.toFloat(), paint)
                paint.shader = null
            }
        }
        val left = ((w - photo.width) / 2f).roundToInt().toFloat()
        val top = ((h - photo.height) / 2f).roundToInt().toFloat()
        val radius = min(options.corners * scale, min(photo.width, photo.height) / 2f)
        val roundedPhoto = rounded(photo, radius)
        if (options.shadow > 0) {
            val blur = options.shadow * scale
            val shadow = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = Color.argb(102, 0, 0, 0)
                maskFilter = BlurMaskFilter(blur, BlurMaskFilter.Blur.NORMAL)
            }
            // The shadow follows the picture's own shape, so transparent pictures cast theirs.
            canvas.drawBitmap(roundedPhoto.extractAlpha(), left, top + blur * 0.35f, shadow)
        }
        canvas.drawBitmap(roundedPhoto, left, top, paint)
        return result
    }

    fun rounded(photo: Bitmap, radius: Float): Bitmap {
        if (radius <= 0.5f) return photo
        val result = Bitmap.createBitmap(photo.width, photo.height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(result)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        canvas.drawRoundRect(RectF(0f, 0f, photo.width.toFloat(), photo.height.toFloat()), radius, radius, paint)
        paint.xfermode = PorterDuffXfermode(PorterDuff.Mode.SRC_IN)
        canvas.drawBitmap(photo, 0f, 0f, paint)
        return result
    }

    /** The background choices as the options screen sends them. */
    fun frameOptions(context: OperationContext) = FrameOptions(
        gradient = context.int("gradient", 0),
        solid = context.options["solid"]?.toIntOrNull(),
        blurred = context.bool("blurred", false),
        padding = context.float("padding", 160f),
        corners = context.float("corners", 24f),
        shadow = context.float("shadow", 24f),
        ratio = context.options["ratio"]?.toFloatOrNull(),
    )

    suspend fun addBackground(files: List<File>, context: OperationContext, options: FrameOptions = frameOptions(context)): List<File> =
        files.mapIndexed { index, file ->
            coroutineContext.ensureActive()
            context.report("Adding background ${index + 1} of ${files.size}")
            context.progress(index.toDouble() / files.size)
            val format = editedFormat(file)
            val output = context.pathFor(file, format.ext, "framed")
            ImageOps.write(frame(Images.decode(file, maxSide = 6000), options), format, output, quality = 92)
            output
        }

    // Collage

    data class Cell(val x: Float, val y: Float, val w: Float, val h: Float)

    enum class CollageStyle { Grid, Row, Column, Featured }

    data class CollageOptions(val style: CollageStyle = CollageStyle.Grid, val columns: Int = 2, val ratio: Float = 1f, val width: Int = 2048,
                              val fill: Boolean = true, val spacing: Float = 16f, val corners: Float = 0f, val background: Int = 0xFFFFFF)

    fun cells(count: Int, options: CollageOptions, width: Float, height: Float, spacing: Float): List<Cell> {
        if (count <= 0) return emptyList()
        fun grid(columns: Int, rows: Int, area: Cell, n: Int): List<Cell> {
            val w = (area.w - spacing * (columns + 1)) / columns
            val h = (area.h - spacing * (rows + 1)) / rows
            return List(n) { i -> Cell(area.x + spacing + i % columns * (w + spacing), area.y + spacing + i / columns * (h + spacing), w, h) }
        }
        val all = Cell(0f, 0f, width, height)
        return when (options.style) {
            CollageStyle.Grid -> {
                val columns = options.columns.coerceIn(1, count)
                grid(columns, ceil(count.toDouble() / columns).toInt(), all, count)
            }
            CollageStyle.Row -> grid(count, 1, all, count)
            CollageStyle.Column -> grid(1, count, all, count)
            CollageStyle.Featured -> if (count == 1) grid(1, 1, all, 1) else {
                // The first photo takes the top three fifths; the rest share a row underneath.
                val split = height * 0.6f
                grid(1, 1, Cell(0f, 0f, width, split + spacing / 2), 1) +
                    grid(count - 1, 1, Cell(0f, split - spacing / 2, width, height - split + spacing / 2), count - 1)
            }
        }
    }

    fun collage(photos: List<Bitmap>, options: CollageOptions, scale: Float = 1f): Bitmap {
        val width = (options.width * scale).roundToInt()
        val height = (width / options.ratio).roundToInt()
        val result = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(result)
        canvas.drawColor(options.background or 0xFF000000.toInt())
        val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
        val cells = cells(photos.size, options, width.toFloat(), height.toFloat(), options.spacing * scale)
        photos.zip(cells).forEach { (photo, cell) ->
            val target = RectF(cell.x, cell.y, cell.x + cell.w, cell.y + cell.h)
            val radius = min(options.corners * scale, min(cell.w, cell.h) / 2)
            canvas.save()
            if (radius > 0.5f) canvas.clipPath(Path().apply { addRoundRect(target, radius, radius, Path.Direction.CW) })
            val fit = if (options.fill) max(cell.w / photo.width, cell.h / photo.height) else min(cell.w / photo.width, cell.h / photo.height)
            val dw = photo.width * fit
            val dh = photo.height * fit
            canvas.clipRect(target)
            canvas.drawBitmap(photo, null, RectF(target.centerX() - dw / 2, target.centerY() - dh / 2, target.centerX() + dw / 2, target.centerY() + dh / 2), paint)
            canvas.restore()
        }
        return result
    }

    fun collageOptions(context: OperationContext) = CollageOptions(
        style = CollageStyle.valueOf(context.text("style", "Grid")),
        columns = context.int("columns", 2),
        ratio = context.float("ratio", 1f),
        spacing = context.float("spacing", 16f),
        corners = context.float("corners", 0f),
        background = context.int("background", 0xFFFFFF),
    )

    suspend fun makeCollage(files: List<File>, context: OperationContext, options: CollageOptions = collageOptions(context)): List<File> {
        context.report("Creating collage")
        val chosen = if (options.style == CollageStyle.Grid && !context.options.containsKey("columns")) options.copy(columns = if (files.size <= 4) 2 else 3) else options
        val photos = files.map { coroutineContext.ensureActive(); Images.decode(it, maxSide = chosen.width) }
        val output = context.named("Collage.jpg")
        ImageOps.write(collage(photos, chosen), ImageFormat.Jpeg, output, quality = 92)
        return listOf(output)
    }

    // Text and codes

    suspend fun extractText(files: List<File>, context: OperationContext): List<File> {
        val outputs = mutableListOf<File>()
        val all = mutableListOf<String>()
        files.forEachIndexed { index, file ->
            coroutineContext.ensureActive()
            context.report("Reading text ${index + 1} of ${files.size}")
            val text = when (file.kind) {
                FileKind.Pdf -> PdfOps.text(file, context)
                else -> Recognition.text(Images.decode(file, maxSide = 4000), context.progressFor(index, files.size))
            }.replace(Regex("[\u200E\u200F]"), "").trim() // Tesseract marks direction around each line.
            if (text.isEmpty()) return@forEachIndexed
            val output = context.pathFor(file, "txt")
            output.writeText(text)
            outputs += output
            all += text
        }
        if (all.isEmpty()) throw AdawatException("No text found")
        Clipboard.copyText(all.joinToString("\n\n"))
        context.finish("Text copied to clipboard")
        return outputs
    }

    suspend fun readCodes(files: List<File>, context: OperationContext): List<File> {
        val payloads = files.flatMap { coroutineContext.ensureActive(); Recognition.codes(Images.decode(it, maxSide = 2400)) }
        if (payloads.isEmpty()) throw AdawatException("No QR codes found")
        Clipboard.copyText(payloads.joinToString("\n"))
        val first = payloads[0]
        val preview = if (first.length > 60) first.take(60) + "…" else first
        context.finish("Copied: {1}".replace("{1}", preview))
        if (first.startsWith("http://", true) || first.startsWith("https://", true)) context.link = first
        return emptyList()
    }
}
