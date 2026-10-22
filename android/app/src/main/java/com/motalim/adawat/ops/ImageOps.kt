package com.motalim.adawat.ops

import android.graphics.Bitmap
import android.graphics.Paint
import android.graphics.pdf.PdfDocument
import androidx.exifinterface.media.ExifInterface
import androidx.heifwriter.HeifWriter
import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.yield
import java.io.DataInputStream
import java.io.File
import java.io.RandomAccessFile
import kotlin.coroutines.coroutineContext

object ImageOps {
    suspend fun convert(files: List<File>, format: ImageFormat, context: OperationContext): List<File> =
        files.mapIndexed { index, file ->
            coroutineContext.ensureActive()
            context.report("Converting ${index + 1} of ${files.size}")
            val bitmap = Images.decode(file)
            val target = context.pathFor(file, format.ext)
            write(bitmap, format, target)
            copyMetadata(file, target)
            bitmap.recycle()
            context.progress((index + 1.0) / files.size)
            target
        }

    fun write(bitmap: Bitmap, format: ImageFormat, target: File, quality: Int = 90) {
        when (format) {
            ImageFormat.Png -> target.outputStream().buffered().use { Encoders.compress(bitmap, Bitmap.CompressFormat.PNG, 100, it) }
            ImageFormat.Jpeg -> target.outputStream().buffered().use { Encoders.compress(Images.flatten(bitmap), Bitmap.CompressFormat.JPEG, quality, it) }
            ImageFormat.WebP -> target.outputStream().buffered().use { Encoders.compress(bitmap, Bitmap.CompressFormat.WEBP_LOSSY, quality, it) }
            ImageFormat.Heic -> heic(bitmap, target, quality)
            ImageFormat.Tiff -> Encoders.tiff(bitmap, target)
            ImageFormat.Bmp -> Encoders.bmp(bitmap, target)
            ImageFormat.Ico -> Encoders.ico(bitmap, target)
            ImageFormat.Gif -> Gif.write(bitmap, target)
            else -> throw AdawatException("${format.title} is not supported on this phone.")
        }
    }

    fun heic(bitmap: Bitmap, target: File, quality: Int = 90) {
        // HEVC encoders want even sizes.
        val even = if (bitmap.width % 2 == 0 && bitmap.height % 2 == 0) bitmap
                   else Bitmap.createBitmap(bitmap, 0, 0, bitmap.width and 1.inv().coerceAtLeast(2), bitmap.height and 1.inv().coerceAtLeast(2))
        HeifWriter.Builder(target.path, even.width, even.height, HeifWriter.INPUT_MODE_BITMAP).setQuality(quality).build().use { writer ->
            writer.start()
            writer.addBitmap(even)
            writer.stop(30_000)
        }
    }

    private val keptTags = listOf(
        ExifInterface.TAG_DATETIME, ExifInterface.TAG_DATETIME_ORIGINAL, ExifInterface.TAG_DATETIME_DIGITIZED,
        ExifInterface.TAG_OFFSET_TIME, ExifInterface.TAG_OFFSET_TIME_ORIGINAL, ExifInterface.TAG_OFFSET_TIME_DIGITIZED,
        ExifInterface.TAG_SUBSEC_TIME_ORIGINAL, ExifInterface.TAG_MAKE, ExifInterface.TAG_MODEL, ExifInterface.TAG_LENS_MAKE,
        ExifInterface.TAG_LENS_MODEL, ExifInterface.TAG_EXPOSURE_TIME, ExifInterface.TAG_F_NUMBER, ExifInterface.TAG_PHOTOGRAPHIC_SENSITIVITY,
        ExifInterface.TAG_FOCAL_LENGTH, ExifInterface.TAG_FOCAL_LENGTH_IN_35MM_FILM, ExifInterface.TAG_FLASH, ExifInterface.TAG_WHITE_BALANCE,
        ExifInterface.TAG_GPS_LATITUDE, ExifInterface.TAG_GPS_LATITUDE_REF, ExifInterface.TAG_GPS_LONGITUDE, ExifInterface.TAG_GPS_LONGITUDE_REF,
        ExifInterface.TAG_GPS_ALTITUDE, ExifInterface.TAG_GPS_ALTITUDE_REF, ExifInterface.TAG_GPS_TIMESTAMP, ExifInterface.TAG_GPS_DATESTAMP,
        ExifInterface.TAG_ARTIST, ExifInterface.TAG_COPYRIGHT, ExifInterface.TAG_IMAGE_DESCRIPTION, ExifInterface.TAG_USER_COMMENT,
    )

    /** Carries the camera details across a conversion. Pixels are already upright, so the orientation becomes normal. */
    fun copyMetadata(source: File, target: File) {
        if (target.extension.lowercase() !in setOf("jpg", "png", "webp")) return
        runCatching {
            val from = ExifInterface(source)
            val values = keptTags.mapNotNull { tag -> from.getAttribute(tag)?.let { tag to it } }
            if (values.isEmpty()) return
            val to = ExifInterface(target)
            values.forEach { (tag, value) -> to.setAttribute(tag, value) }
            to.setAttribute(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL.toString())
            to.saveAttributes()
        }
    }

    /** One PDF with a page per picture, each page the picture's own size. */
    suspend fun makePdf(files: List<File>, context: OperationContext): List<File> {
        val document = PdfDocument()
        try {
            files.forEachIndexed { index, file ->
                coroutineContext.ensureActive()
                context.report("Adding page ${index + 1} of ${files.size}")
                val bitmap = Images.decode(file, maxSide = 3000)
                // 150 dpi keeps phone photos at a sensible paper size.
                val scale = 72f / 150f
                val width = (bitmap.width * scale).toInt().coerceAtLeast(1)
                val height = (bitmap.height * scale).toInt().coerceAtLeast(1)
                val page = document.startPage(PdfDocument.PageInfo.Builder(width, height, index + 1).create())
                page.canvas.drawBitmap(bitmap, null, android.graphics.RectF(0f, 0f, width.toFloat(), height.toFloat()),
                                       Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG))
                document.finishPage(page)
                bitmap.recycle()
                context.progress((index + 1.0) / files.size)
                yield()
            }
            val target = if (files.size == 1) context.pathFor(files[0], "pdf") else context.named("Images.pdf")
            target.outputStream().buffered().use { document.writeTo(it) }
            return listOf(target)
        } finally {
            document.close()
        }
    }

    /** Removes camera, place and editing details while keeping the picture untouched where the format allows it. */
    suspend fun removeMetadata(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        coroutineContext.ensureActive()
        val target = context.pathFor(file, file.extension.lowercase().ifEmpty { "png" }, "clean")
        when (ImageFormat.of(file)) {
            ImageFormat.Jpeg -> Metadata.stripJpeg(file, target)
            ImageFormat.Png -> Metadata.stripPng(file, target)
            ImageFormat.WebP -> Metadata.stripWebp(file, target)
            else -> {
                val format = ImageFormat.of(file)?.takeIf { it.canWrite } ?: ImageFormat.Png
                val clean = if (format.ext == target.extension) target else context.pathFor(file, format.ext, "clean")
                write(Images.decode(file), format, clean)
                return@mapIndexed clean.also { context.progress((index + 1.0) / files.size) }
            }
        }
        context.progress((index + 1.0) / files.size)
        target
    }
}

/** Lossless metadata removal by dropping the containers' metadata blocks. */
private object Metadata {
    fun stripJpeg(source: File, target: File) {
        val orientation = runCatching { ExifInterface(source).getAttributeInt(ExifInterface.TAG_ORIENTATION, 1) }.getOrDefault(1)
        DataInputStream(source.inputStream().buffered()).use { input ->
            target.outputStream().buffered().use { out ->
                if (input.readUnsignedShort() != 0xFFD8) throw AdawatException("Could not read ${source.name}.")
                out.write(0xFF); out.write(0xD8)
                while (true) {
                    var marker = input.readUnsignedByte()
                    if (marker != 0xFF) throw AdawatException("Could not read ${source.name}.")
                    do marker = input.readUnsignedByte() while (marker == 0xFF)
                    if (marker == 0xDA || marker == 0xD9) {
                        // Start of scan: the rest is picture data.
                        out.write(0xFF); out.write(marker)
                        input.copyTo(out)
                        break
                    }
                    val length = input.readUnsignedShort()
                    val body = ByteArray(length - 2).also { input.readFully(it) }
                    // APP1–APP15 hold EXIF, XMP, IPTC and maker data; COM holds comments. APP2 can hold the colour profile, which stays.
                    val isProfile = marker == 0xE2 && body.size > 11 && String(body, 0, 11, Charsets.US_ASCII) == "ICC_PROFILE"
                    if ((marker in 0xE1..0xEF && !isProfile) || marker == 0xFE) continue
                    out.write(0xFF); out.write(marker)
                    out.write(length shr 8); out.write(length and 0xFF)
                    out.write(body)
                }
            }
        }
        if (orientation in 2..8) {
            ExifInterface(target).apply { setAttribute(ExifInterface.TAG_ORIENTATION, orientation.toString()); saveAttributes() }
        }
    }

    fun stripPng(source: File, target: File) {
        val dropped = setOf("tEXt", "zTXt", "iTXt", "eXIf", "tIME")
        DataInputStream(source.inputStream().buffered()).use { input ->
            target.outputStream().buffered().use { out ->
                val signature = ByteArray(8).also { input.readFully(it) }
                out.write(signature)
                while (true) {
                    val length = runCatching { input.readInt() }.getOrNull() ?: break
                    val type = ByteArray(4).also { input.readFully(it) }
                    val rest = ByteArray(length + 4).also { input.readFully(it) }
                    if (String(type, Charsets.US_ASCII) in dropped) continue
                    out.write(byteArrayOf((length ushr 24).toByte(), (length ushr 16).toByte(), (length ushr 8).toByte(), length.toByte()))
                    out.write(type)
                    out.write(rest)
                    if (String(type, Charsets.US_ASCII) == "IEND") break
                }
            }
        }
    }

    fun stripWebp(source: File, target: File) {
        val bytes = source.readBytes()
        if (bytes.size < 12 || String(bytes, 0, 4, Charsets.US_ASCII) != "RIFF") throw AdawatException("Could not read ${source.name}.")
        val out = java.io.ByteArrayOutputStream()
        var position = 12
        while (position + 8 <= bytes.size) {
            val type = String(bytes, position, 4, Charsets.US_ASCII)
            val size = (bytes[position + 4].toInt() and 0xFF) or ((bytes[position + 5].toInt() and 0xFF) shl 8) or
                       ((bytes[position + 6].toInt() and 0xFF) shl 16) or ((bytes[position + 7].toInt() and 0xFF) shl 24)
            val total = 8 + size + (size and 1)
            if (type != "EXIF" && type != "XMP ") {
                val chunk = bytes.copyOfRange(position, minOf(position + total, bytes.size))
                // VP8X flags announce the metadata chunks; clear the EXIF and XMP bits.
                if (type == "VP8X" && chunk.size > 8) chunk[8] = (chunk[8].toInt() and 0x0C.inv() and 0xFF).toByte()
                out.write(chunk)
            }
            position += total
        }
        val body = out.toByteArray()
        RandomAccessFile(target, "rw").use { file ->
            file.setLength(0)
            val riffSize = body.size + 4
            file.write("RIFF".toByteArray(Charsets.US_ASCII))
            file.write(byteArrayOf(riffSize.toByte(), (riffSize shr 8).toByte(), (riffSize shr 16).toByte(), (riffSize shr 24).toByte()))
            file.write("WEBP".toByteArray(Charsets.US_ASCII))
            file.write(body)
        }
    }
}
