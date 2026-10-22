package com.motalim.adawat.ops

import android.graphics.Bitmap
import android.graphics.Color
import java.io.ByteArrayOutputStream
import java.io.File

/** A still GIF: the picture reduced to 256 colours by median cut, with transparency kept as one colour. */
object Gif {
    fun write(bitmap: Bitmap, file: File) {
        val w = bitmap.width
        val h = bitmap.height
        require(w <= 65535 && h <= 65535)
        val pixels = IntArray(w * h).also { bitmap.getPixels(it, 0, w, 0, 0, w, h) }
        val transparent = pixels.any { Color.alpha(it) < 128 }
        val palette = palette(pixels, if (transparent) 255 else 256)
        val offset = if (transparent) 1 else 0
        // Nearest palette entry for every 15-bit colour, filled as colours are met.
        val lookup = IntArray(32768) { -1 }
        val indices = ByteArray(w * h)
        for (i in pixels.indices) {
            val p = pixels[i]
            if (transparent && Color.alpha(p) < 128) { indices[i] = 0; continue }
            val key = ((p shr 9) and 0x7C00) or ((p shr 6) and 0x3E0) or ((p shr 3) and 0x1F)
            var index = lookup[key]
            if (index < 0) { index = nearest(palette, p); lookup[key] = index }
            indices[i] = (index + offset).toByte()
        }
        val out = ByteArrayOutputStream()
        out.write("GIF89a".toByteArray(Charsets.US_ASCII))
        short(out, w); short(out, h)
        out.write(0xF7); out.write(0); out.write(0) // Global table of 256 colours.
        val table = IntArray(256)
        palette.forEachIndexed { i, c -> table[i + offset] = c }
        for (c in table) { out.write(Color.red(c)); out.write(Color.green(c)); out.write(Color.blue(c)) }
        if (transparent) out.write(byteArrayOf(0x21, 0xF9.toByte(), 4, 1, 0, 0, 0, 0))
        out.write(0x2C); short(out, 0); short(out, 0); short(out, w); short(out, h); out.write(0)
        out.write(8)
        lzw(indices, out)
        out.write(0)
        out.write(0x3B)
        file.writeBytes(out.toByteArray())
    }

    /**
     * An animated GIF that loops forever. [sample] gives up to eight frames to build one shared palette from; [frames]
     * then hands every frame in order to the function it is given. Ordered dithering keeps gradients smooth without the
     * shimmer of error diffusion.
     */
    suspend fun writeAnimated(file: File, w: Int, h: Int, count: Int, delayCentiseconds: Int,
                              sample: (Int) -> Bitmap?, frames: suspend (deliver: suspend (Bitmap) -> Unit) -> Unit) {
        val pixels = IntArray(w * h)
        val samples = IntArrayList()
        for (n in 0 until minOf(8, count)) {
            val bitmap = sample(n) ?: continue
            bitmap.getPixels(pixels, 0, w, 0, 0, minOf(w, bitmap.width), minOf(h, bitmap.height))
            for (i in pixels.indices step 7) samples.add(pixels[i] or 0xFF000000.toInt())
        }
        if (samples.size == 0) throw IllegalStateException("No frames could be read from this video.")
        val palette = palette(samples.toArray(), 256)
        val lookup = IntArray(32768) { -1 }
        val bayer = intArrayOf(0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5)
        file.outputStream().buffered().use { out ->
            val head = ByteArrayOutputStream()
            head.write("GIF89a".toByteArray(Charsets.US_ASCII))
            short(head, w); short(head, h)
            head.write(0xF7); head.write(0); head.write(0)
            for (i in 0 until 256) {
                val c = if (i < palette.size) palette[i] else 0
                head.write(Color.red(c)); head.write(Color.green(c)); head.write(Color.blue(c))
            }
            // Loop forever.
            head.write(byteArrayOf(0x21, 0xFF.toByte(), 11) + "NETSCAPE2.0".toByteArray(Charsets.US_ASCII) + byteArrayOf(3, 1, 0, 0, 0))
            out.write(head.toByteArray())
            val indices = ByteArray(w * h)
            frames { bitmap ->
                pixels.fill(0xFF000000.toInt())
                bitmap.getPixels(pixels, 0, w, 0, 0, minOf(w, bitmap.width), minOf(h, bitmap.height))
                for (y in 0 until h) for (x in 0 until w) {
                    val p = pixels[y * w + x]
                    val d = (bayer[(y and 3) * 4 + (x and 3)] - 8) * 2
                    val r = (Color.red(p) + d).coerceIn(0, 255)
                    val g = (Color.green(p) + d).coerceIn(0, 255)
                    val b = (Color.blue(p) + d).coerceIn(0, 255)
                    val key = ((r shr 3) shl 10) or ((g shr 3) shl 5) or (b shr 3)
                    var index = lookup[key]
                    if (index < 0) { index = nearest(palette, Color.rgb(r, g, b)); lookup[key] = index }
                    indices[y * w + x] = index.toByte()
                }
                val block = ByteArrayOutputStream()
                block.write(byteArrayOf(0x21, 0xF9.toByte(), 4, 4, (delayCentiseconds and 0xFF).toByte(), (delayCentiseconds shr 8).toByte(), 0, 0))
                block.write(0x2C); short(block, 0); short(block, 0); short(block, w); short(block, h); block.write(0)
                block.write(8)
                lzw(indices, block)
                block.write(0)
                out.write(block.toByteArray())
            }
            out.write(0x3B)
        }
    }

    private class IntArrayList {
        private var data = IntArray(1024)
        var size = 0
            private set

        fun add(value: Int) {
            if (size == data.size) data = data.copyOf(size * 2)
            data[size++] = value
        }

        fun toArray() = data.copyOf(size)
    }

    private fun short(out: ByteArrayOutputStream, value: Int) { out.write(value and 0xFF); out.write(value shr 8 and 0xFF) }

    private fun nearest(palette: IntArray, p: Int): Int {
        val r = Color.red(p); val g = Color.green(p); val b = Color.blue(p)
        var best = 0
        var bestDistance = Int.MAX_VALUE
        for (i in palette.indices) {
            val c = palette[i]
            val dr = Color.red(c) - r; val dg = Color.green(c) - g; val db = Color.blue(c) - b
            val d = dr * dr * 3 + dg * dg * 4 + db * db * 2
            if (d < bestDistance) { bestDistance = d; best = i }
        }
        return best
    }

    /** Median cut over a sample of the opaque pixels. */
    private fun palette(pixels: IntArray, size: Int): IntArray {
        val step = maxOf(1, pixels.size / 120_000)
        val sample = ArrayList<Int>()
        for (i in pixels.indices step step) if (Color.alpha(pixels[i]) >= 128) sample += pixels[i] and 0xFFFFFF
        if (sample.isEmpty()) return intArrayOf(0)
        val boxes = mutableListOf(sample.toIntArray())
        while (boxes.size < size) {
            val (index, channel) = boxes.withIndex().filter { it.value.size > 1 }.map { (i, box) -> i to widest(box) }
                .maxByOrNull { it.second.second }?.let { it.first to it.second.first } ?: break
            val box = boxes.removeAt(index)
            val shift = 16 - channel * 8
            val sorted = box.sortedBy { (it shr shift) and 0xFF }
            val half = sorted.size / 2
            boxes += sorted.subList(0, half).toIntArray()
            boxes += sorted.subList(half, sorted.size).toIntArray()
        }
        return boxes.map { box ->
            var r = 0L; var g = 0L; var b = 0L
            for (c in box) { r += c shr 16 and 0xFF; g += c shr 8 and 0xFF; b += c and 0xFF }
            Color.rgb((r / box.size).toInt(), (g / box.size).toInt(), (b / box.size).toInt())
        }.toIntArray()
    }

    /** The channel (0 red, 1 green, 2 blue) with the widest range, and that range. */
    private fun widest(box: IntArray): Pair<Int, Int> {
        val min = intArrayOf(255, 255, 255)
        val max = intArrayOf(0, 0, 0)
        for (c in box) for (ch in 0..2) {
            val v = (c shr (16 - ch * 8)) and 0xFF
            if (v < min[ch]) min[ch] = v
            if (v > max[ch]) max[ch] = v
        }
        val ranges = (0..2).map { max[it] - min[it] }
        val channel = ranges.indices.maxBy { ranges[it] }
        return channel to ranges[channel]
    }

    private fun lzw(indices: ByteArray, out: ByteArrayOutputStream) {
        val clear = 256
        val end = 257
        val block = ByteArray(255)
        var blockSize = 0
        var bitBuffer = 0
        var bitCount = 0
        var codeSize = 9
        var next = 258
        val table = HashMap<Int, Int>(8192)
        fun emit(code: Int) {
            bitBuffer = bitBuffer or (code shl bitCount)
            bitCount += codeSize
            while (bitCount >= 8) {
                block[blockSize++] = (bitBuffer and 0xFF).toByte()
                bitBuffer = bitBuffer ushr 8
                bitCount -= 8
                if (blockSize == 255) { out.write(255); out.write(block, 0, 255); blockSize = 0 }
            }
        }
        emit(clear)
        if (indices.isEmpty()) { emit(end) } else {
            var prefix = indices[0].toInt() and 0xFF
            for (i in 1 until indices.size) {
                val k = indices[i].toInt() and 0xFF
                val key = (prefix shl 8) or k
                val found = table[key]
                if (found != null) { prefix = found; continue }
                emit(prefix)
                if (next < 4096) {
                    table[key] = next++
                    if (next - 1 == (1 shl codeSize) && codeSize < 12) codeSize++
                } else {
                    emit(clear)
                    table.clear()
                    next = 258
                    codeSize = 9
                }
                prefix = k
            }
            emit(prefix)
            emit(end)
        }
        if (bitCount > 0) {
            block[blockSize++] = (bitBuffer and 0xFF).toByte()
            if (blockSize == 255) { out.write(255); out.write(block, 0, 255); blockSize = 0 }
        }
        if (blockSize > 0) { out.write(blockSize); out.write(block, 0, blockSize) }
    }
}
