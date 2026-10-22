package com.motalim.adawat.work

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import com.motalim.adawat.core.FileKind
import com.motalim.adawat.core.kind
import java.io.File

/** A saved result: where it lives in the shared folders, and its name. */
data class Saved(val uri: Uri, val name: String, val mime: String, val folder: String)

/** Results go to an Adawat folder in Pictures, Movies, Music or Download, where every gallery and file app finds them. */
object Outputs {
    fun mime(file: File): String =
        MimeTypeMap.getSingleton().getMimeTypeFromExtension(file.extension.lowercase())
            ?: when (file.extension.lowercase()) {
                "heic", "heif" -> "image/heic"
                "avif" -> "image/avif"
                "md", "markdown" -> "text/markdown"
                else -> "application/octet-stream"
            }

    /**
     * Saves a result; a folder of results (pages, an unpacked archive) becomes a folder of the same name inside Adawat,
     * with its own folders kept. A folder of only pictures, videos or music goes with them; any other folder goes to Download.
     */
    fun saveAll(context: Context, file: File): List<Saved> {
        if (!file.isDirectory) return listOf(save(context, file))
        val all = file.walkTopDown().filter { it.isFile }.sortedWith(compareBy({ it.path.length }, { it.path })).toList()
        val kinds = all.map { it.kind }.toSet()
        val shared = kinds.singleOrNull()?.takeIf { it == FileKind.Image || it == FileKind.Video || it == FileKind.Audio } ?: FileKind.Other
        // A folder of the same name from before gets a number, rather than the two being mixed.
        val top = uniqueFolder(context, root(shared), file.name)
        return all.map { child ->
            val relative = top + child.parentFile!!.relativeTo(file).path.let { if (it.isEmpty()) "" else "/$it" }
            save(context, child, relative, shared)
        }
    }

    private fun root(kind: FileKind) = when (kind) {
        FileKind.Image -> Environment.DIRECTORY_PICTURES
        FileKind.Video -> Environment.DIRECTORY_MOVIES
        FileKind.Audio -> Environment.DIRECTORY_MUSIC
        else -> Environment.DIRECTORY_DOWNLOADS
    }

    private fun uniqueFolder(context: Context, root: String, name: String): String {
        val files = MediaStore.Files.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
        fun taken(candidate: String) = context.contentResolver.query(
            files, arrayOf(MediaStore.MediaColumns._ID), "${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?", arrayOf("$root/Adawat/$candidate/%"), null,
        )?.use { it.count > 0 } ?: false
        if (!taken(name)) return name
        var n = 2
        while (taken("$name $n")) n++
        return "$name $n"
    }

    fun save(context: Context, file: File, subfolder: String? = null, kind: FileKind = file.kind): Saved {
        val mime = mime(file)
        val (collection, folder) = when (kind) {
            FileKind.Image -> MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to Environment.DIRECTORY_PICTURES
            FileKind.Video -> MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to Environment.DIRECTORY_MOVIES
            FileKind.Audio -> MediaStore.Audio.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to Environment.DIRECTORY_MUSIC
            else -> MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to Environment.DIRECTORY_DOWNLOADS
        }
        val relative = "$folder/Adawat" + (subfolder?.let { "/$it" } ?: "")
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, file.name)
            put(MediaStore.MediaColumns.MIME_TYPE, mime)
            put(MediaStore.MediaColumns.RELATIVE_PATH, relative)
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val resolver = context.contentResolver
        val uri = resolver.insert(collection, values) ?: throw IllegalStateException("Could not save ${file.name}.")
        try {
            resolver.openOutputStream(uri)!!.use { out -> file.inputStream().use { it.copyTo(out) } }
            resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
        } catch (e: Exception) {
            resolver.delete(uri, null, null)
            throw e
        }
        // The shared folder may have renamed it ("name (1).jpg").
        val name = resolver.query(uri, arrayOf(MediaStore.MediaColumns.DISPLAY_NAME), null, null, null)?.use {
            if (it.moveToFirst()) it.getString(0) else null
        } ?: file.name
        return Saved(uri, name, mime, relative)
    }
}
