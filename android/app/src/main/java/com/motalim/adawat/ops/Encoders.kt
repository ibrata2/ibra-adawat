package com.motalim.adawat.ops

import android.graphics.Bitmap
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Small writers for the formats Android's Bitmap cannot write itself. */
object Encoders {
    /** Uncompressed 8-bit RGBA TIFF, baseline and readable everywhere. */
    fun tiff(bitmap: Bitmap, file: File) {
        val w = bitmap.width
        val h = bitmap.height
        val pixels = IntArray(w * h).also { bitmap.getPixels(it, 0, w, 0, 0, w, h) }
        val entries = 12
        val ifdOffset = 8
        val ifdSize = 2 + entries * 12 + 4
        val bitsOffset = ifdOffset + ifdSize
        val resolutionOffset = bitsOffset + 8
        val dataOffset = resolutionOffset + 16
        val dataSize = w * h * 4
        val buffer = ByteBuffer.allocate(dataOffset + dataSize).order(ByteOrder.LITTLE_ENDIAN)
        buffer.put('I'.code.toByte()).put('I'.code.toByte()).putShort(42).putInt(ifdOffset)
        buffer.putShort(entries.toShort())
        fun entry(tag: Int, type: Int, count: Int, value: Int) {
            buffer.putShort(tag.toShort()).putShort(type.toShort()).putInt(count)
            if (type == 3 && count == 1) buffer.putShort(value.toShort()).putShort(0) else buffer.putInt(value)
        }
        entry(256, 4, 1, w)                  // ImageWidth
        entry(257, 4, 1, h)                  // ImageLength
        entry(258, 3, 4, bitsOffset)         // BitsPerSample 8,8,8,8
        entry(259, 3, 1, 1)                  // No compression
        entry(262, 3, 1, 2)                  // RGB
        entry(273, 4, 1, dataOffset)         // StripOffsets
        entry(277, 3, 1, 4)                  // SamplesPerPixel
        entry(278, 4, 1, h)                  // RowsPerStrip
        entry(279, 4, 1, dataSize)           // StripByteCounts
        entry(282, 5, 1, resolutionOffset)   // XResolution
        entry(283, 5, 1, resolutionOffset + 8)
        entry(338, 3, 1, 2)                  // ExtraSamples: unassociated alpha
        buffer.putInt(0)
        repeat(4) { buffer.putShort(8) }
        buffer.putInt(72).putInt(1).putInt(72).putInt(1)
        for (p in pixels) {
            buffer.put((p shr 16).toByte()).put((p shr 8).toByte()).put(p.toByte()).put((p ushr 24).toByte())
        }
        file.writeBytes(buffer.array())
    }

    /** 32-bit BMP with alpha (BITMAPV4HEADER), bottom-up. */
    fun bmp(bitmap: Bitmap, file: File) {
        val w = bitmap.width
        val h = bitmap.height
        val pixels = IntArray(w * h).also { bitmap.getPixels(it, 0, w, 0, 0, w, h) }
        val header = 14 + 108
        val size = header + w * h * 4
        val buffer = ByteBuffer.allocate(size).order(ByteOrder.LITTLE_ENDIAN)
        buffer.put('B'.code.toByte()).put('M'.code.toByte()).putInt(size).putInt(0).putInt(header)
        buffer.putInt(108).putInt(w).putInt(h).putShort(1).putShort(32).putInt(3).putInt(w * h * 4)
        buffer.putInt(2835).putInt(2835).putInt(0).putInt(0)
        buffer.putInt(0x00FF0000).putInt(0x0000FF00).putInt(0x000000FF).putInt(0xFF000000.toInt())
        buffer.putInt(0x73524742) // 'sRGB'
        repeat(12) { buffer.putInt(0) }
        for (y in h - 1 downTo 0) for (x in 0 until w) buffer.putInt(pixels[y * w + x])
        file.writeBytes(buffer.array())
    }

    /** An icon with PNG images at the usual sizes, up to the picture's own size. */
    fun ico(bitmap: Bitmap, file: File) {
        val square = Images.fitSquare(bitmap, 256)
        val sizes = listOf(256, 128, 64, 48, 32, 16).filter { it <= square.width }.ifEmpty { listOf(square.width) }
        val images = sizes.map { size ->
            val scaled = if (size == square.width) square else Bitmap.createScaledBitmap(square, size, size, true)
            ByteArrayOutputStream().also { scaled.compress(Bitmap.CompressFormat.PNG, 100, it) }.toByteArray() to size
        }
        file.outputStream().buffered().use { out ->
            val head = ByteBuffer.allocate(6 + images.size * 16).order(ByteOrder.LITTLE_ENDIAN)
            head.putShort(0).putShort(1).putShort(images.size.toShort())
            var offset = 6 + images.size * 16
            for ((bytes, size) in images) {
                head.put((size % 256).toByte()).put((size % 256).toByte()).put(0).put(0)
                head.putShort(1).putShort(32).putInt(bytes.size).putInt(offset)
                offset += bytes.size
            }
            out.write(head.array())
            images.forEach { out.write(it.first) }
        }
    }

    fun compress(bitmap: Bitmap, format: Bitmap.CompressFormat, quality: Int, out: OutputStream) {
        if (!bitmap.compress(format, quality, out)) throw IllegalStateException("The picture could not be written.")
    }
}
