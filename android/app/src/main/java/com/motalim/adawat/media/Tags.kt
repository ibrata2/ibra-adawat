package com.motalim.adawat.media

import com.motalim.adawat.core.AdawatException
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Removes tags from audio files by dropping their tag blocks; the sound is copied byte for byte. */
object Tags {
    /** ID3v2 at the start, ID3v1 and APE at the end. */
    fun stripMp3(source: File, target: File) {
        val bytes = source.readBytes()
        var start = 0
        while (start + 10 <= bytes.size && bytes[start] == 'I'.code.toByte() && bytes[start + 1] == 'D'.code.toByte() && bytes[start + 2] == '3'.code.toByte()) {
            val size = ((bytes[start + 6].toInt() and 0x7F) shl 21) or ((bytes[start + 7].toInt() and 0x7F) shl 14) or
                       ((bytes[start + 8].toInt() and 0x7F) shl 7) or (bytes[start + 9].toInt() and 0x7F)
            val footer = if (bytes[start + 5].toInt() and 0x10 != 0) 10 else 0
            start += 10 + size + footer
        }
        var end = bytes.size
        if (end - 128 >= start && String(bytes, end - 128, 3, Charsets.ISO_8859_1) == "TAG") end -= 128
        if (end - 32 >= start && String(bytes, end - 32, 8, Charsets.ISO_8859_1) == "APETAGEX") {
            val size = ByteBuffer.wrap(bytes, end - 20, 4).order(ByteOrder.LITTLE_ENDIAN).int
            val header = if (ByteBuffer.wrap(bytes, end - 12, 4).order(ByteOrder.LITTLE_ENDIAN).int and (1 shl 31) != 0) 32 else 0
            end = maxOf(start, end - size - header)
        }
        target.outputStream().use { it.write(bytes, start, end - start) }
    }

    /** Keeps STREAMINFO and SEEKTABLE; drops comments, pictures and the rest. */
    fun stripFlac(source: File, target: File) {
        val bytes = source.readBytes()
        if (bytes.size < 8 || String(bytes, 0, 4, Charsets.ISO_8859_1) != "fLaC") throw AdawatException("Could not read ${source.name}.")
        val kept = mutableListOf<ByteArray>()
        var position = 4
        while (true) {
            val header = bytes[position].toInt() and 0xFF
            val last = header and 0x80 != 0
            val type = header and 0x7F
            val length = ((bytes[position + 1].toInt() and 0xFF) shl 16) or ((bytes[position + 2].toInt() and 0xFF) shl 8) or (bytes[position + 3].toInt() and 0xFF)
            if (type == 0 || type == 3) kept += bytes.copyOfRange(position, position + 4 + length)
            position += 4 + length
            if (last) break
        }
        target.outputStream().buffered().use { out ->
            out.write("fLaC".toByteArray(Charsets.ISO_8859_1))
            kept.forEachIndexed { i, block ->
                block[0] = ((block[0].toInt() and 0x7F) or (if (i == kept.lastIndex) 0x80 else 0)).toByte()
                out.write(block)
            }
            out.write(bytes, position, bytes.size - position)
        }
    }

    /** Keeps the format and sound chunks; drops LIST, id3 and the rest. */
    fun stripWav(source: File, target: File) {
        val bytes = source.readBytes()
        if (bytes.size < 12 || String(bytes, 0, 4, Charsets.ISO_8859_1) != "RIFF") throw AdawatException("Could not read ${source.name}.")
        val chunks = mutableListOf<ByteArray>()
        var position = 12
        while (position + 8 <= bytes.size) {
            val id = String(bytes, position, 4, Charsets.ISO_8859_1)
            val size = ByteBuffer.wrap(bytes, position + 4, 4).order(ByteOrder.LITTLE_ENDIAN).int.toLong() and 0xFFFFFFFFL
            val total = (8 + size + (size and 1)).toInt().coerceAtMost(bytes.size - position)
            if (id == "fmt " || id == "data" || id == "fact") chunks += bytes.copyOfRange(position, position + total)
            position += total
        }
        val body = chunks.sumOf { it.size }
        target.outputStream().buffered().use { out ->
            out.write(ByteBuffer.allocate(12).order(ByteOrder.LITTLE_ENDIAN).put("RIFF".toByteArray()).putInt(4 + body).put("WAVE".toByteArray()).array())
            chunks.forEach { out.write(it) }
        }
    }
}
