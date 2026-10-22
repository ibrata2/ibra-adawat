package com.motalim.adawat.ops

import androidx.exifinterface.media.ExifInterface
import com.motalim.adawat.core.OperationContext
import java.io.File
import java.net.URLDecoder
import java.net.URLEncoder

/** A photo's EXIF fields, grouped as on the desktop, some editable, any removable. */
object PhotoMetadata {
    data class Field(val tag: String, val group: String, val name: String, val value: String, val editable: Boolean)

    private val known = listOf(
        Triple("Camera", "Make", ExifInterface.TAG_MAKE), Triple("Camera", "Model", ExifInterface.TAG_MODEL),
        Triple("Camera", "Lens", ExifInterface.TAG_LENS_MODEL), Triple("Camera", "Software", ExifInterface.TAG_SOFTWARE),
        Triple("Exposure", "Exposure Time", ExifInterface.TAG_EXPOSURE_TIME), Triple("Exposure", "Aperture", ExifInterface.TAG_F_NUMBER),
        Triple("Exposure", "ISO", ExifInterface.TAG_PHOTOGRAPHIC_SENSITIVITY), Triple("Exposure", "Focal Length", ExifInterface.TAG_FOCAL_LENGTH),
        Triple("Exposure", "Flash", ExifInterface.TAG_FLASH),
        Triple("Date", "Date Taken", ExifInterface.TAG_DATETIME_ORIGINAL), Triple("Date", "Date Modified", ExifInterface.TAG_DATETIME),
        Triple("Description", "Description", ExifInterface.TAG_IMAGE_DESCRIPTION), Triple("Description", "Artist", ExifInterface.TAG_ARTIST),
        Triple("Description", "Copyright", ExifInterface.TAG_COPYRIGHT), Triple("Description", "Comment", ExifInterface.TAG_USER_COMMENT),
    )
    private val editableTags = setOf(ExifInterface.TAG_DATETIME_ORIGINAL, ExifInterface.TAG_IMAGE_DESCRIPTION, ExifInterface.TAG_ARTIST,
                                     ExifInterface.TAG_COPYRIGHT, ExifInterface.TAG_USER_COMMENT)

    /** Where Android can write EXIF back. */
    fun writable(file: File) = file.extension.lowercase() in setOf("jpg", "jpeg", "png", "webp")

    fun read(file: File): List<Field> {
        val exif = runCatching { ExifInterface(file) }.getOrNull() ?: return emptyList()
        val canWrite = writable(file)
        val fields = known.mapNotNull { (group, name, tag) ->
            exif.getAttribute(tag)?.takeIf { it.isNotBlank() }?.let { Field(tag, group, name, it.trim(), canWrite && tag in editableTags) }
        }.toMutableList()
        exif.latLong?.let { (lat, lon) ->
            fields += Field("location", "Location", "Coordinates", String.format(java.util.Locale.US, "%.5f, %.5f", lat, lon), false)
        }
        if (canWrite) {
            // Offer the description fields even when empty, so they can be filled in.
            for ((group, name, tag) in known) if (tag in editableTags && fields.none { it.tag == tag }) fields += Field(tag, group, name, "", true)
        }
        return fields.sortedBy { f -> listOf("Camera", "Exposure", "Date", "Location", "Description").indexOf(f.group) }
    }

    fun encode(changes: Map<String, String?>) = changes.entries.joinToString("&") { (k, v) -> "$k=" + (v?.let { URLEncoder.encode(it, "UTF-8") } ?: "\u0000") }

    private fun decode(text: String): Map<String, String?> = text.split('&').filter { it.contains('=') }.associate { part ->
        val key = part.substringBefore('=')
        val value = part.substringAfter('=')
        key to (if (value == "\u0000") null else URLDecoder.decode(value, "UTF-8"))
    }

    /** Edits and removals from the metadata screen, or everything removed with "removeAll". */
    suspend fun save(files: List<File>, context: OperationContext): List<File> {
        if (context.bool("removeAll", false) || !writable(files[0]) || !context.options.containsKey("changes")) return ImageOps.removeMetadata(files, context)
        val file = files[0]
        val output = context.pathFor(file, file.extension.lowercase(), "edited")
        file.copyTo(output)
        val exif = ExifInterface(output)
        for ((tag, value) in decode(context.text("changes", ""))) {
            if (tag == "location") {
                for (t in listOf(ExifInterface.TAG_GPS_LATITUDE, ExifInterface.TAG_GPS_LATITUDE_REF, ExifInterface.TAG_GPS_LONGITUDE,
                                 ExifInterface.TAG_GPS_LONGITUDE_REF, ExifInterface.TAG_GPS_ALTITUDE, ExifInterface.TAG_GPS_ALTITUDE_REF,
                                 ExifInterface.TAG_GPS_TIMESTAMP, ExifInterface.TAG_GPS_DATESTAMP)) exif.setAttribute(t, null)
            } else {
                exif.setAttribute(tag, value?.ifEmpty { null })
            }
        }
        exif.saveAttributes()
        context.finish("Metadata saved")
        return listOf(output)
    }
}
