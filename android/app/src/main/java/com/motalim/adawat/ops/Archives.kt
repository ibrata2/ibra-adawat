package com.motalim.adawat.ops

import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import kotlinx.coroutines.ensureActive
import java.io.File
import java.util.zip.GZIPOutputStream
import kotlin.coroutines.coroutineContext

/** libarchive, built from source into libadawat-archive.so (see android/app/src/main/cpp). */
object ArchiveNative {
    init {
        System.loadLibrary("adawat-archive")
    }

    /** Null when it worked; otherwise libarchive's message, or "ENCRYPTED" or "EMPTY". */
    @JvmStatic external fun extract(archive: String, destination: String, single: String, password: String?): String?

    @JvmStatic external fun create(output: String, format: String, paths: Array<String>, names: Array<String>): String?
}

/** Packing into ZIP, TAR, TGZ and GZ, converting between them, and extracting nearly anything (RAR, 7-Zip and ISO included). */
object ArchiveOps {
    fun title(format: String) = format.uppercase()

    fun formatOf(file: File): String? = when (file.ext) { "zip" -> "zip"; "tar" -> "tar"; "tgz" -> "tgz"; "gz" -> "gz"; else -> null }

    private val suffixes = listOf(".tar.gz", ".tgz", ".tar.bz2", ".tbz", ".tar.xz", ".txz", ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab")

    private fun stem(file: File): String {
        val name = file.name
        return suffixes.firstOrNull { name.endsWith(it, ignoreCase = true) }?.let { name.dropLast(it.length) } ?: file.nameWithoutExtension
    }

    /** Every file and folder under [items], each with its name inside the archive. */
    private fun entries(items: List<File>): Pair<Array<String>, Array<String>> {
        val paths = mutableListOf<String>()
        val names = mutableListOf<String>()
        fun add(file: File, name: String) {
            paths += file.path
            names += if (file.isDirectory) "$name/" else name
            if (file.isDirectory) file.listFiles().orEmpty().sortedBy { it.name }.forEach { add(it, "$name/${it.name}") }
        }
        items.forEach { add(it, it.name) }
        return paths.toTypedArray() to names.toTypedArray()
    }

    private fun write(output: File, format: String, items: List<File>) {
        if (format == "gz") {
            items[0].inputStream().use { input -> GZIPOutputStream(output.outputStream().buffered()).use { input.copyTo(it) } }
            return
        }
        val (paths, names) = entries(items)
        ArchiveNative.create(output.path, format, paths, names)?.let {
            output.delete()
            throw AdawatException("The archive could not be written.")
        }
    }

    suspend fun pack(files: List<File>, format: String, context: OperationContext): List<File> {
        context.report(if (files.size == 1) "Packing 1 item" else "Packing ${files.size} items")
        val name = if (files.size == 1) files[0].name else "Archive"
        val output = if (format == "gz") context.named("${files[0].name}.gz") else context.named("$name.${if (format == "tgz") "tar.gz" else format}")
        write(output, format, files)
        return listOf(output)
    }

    private fun extractTo(file: File, folder: File) {
        folder.mkdirs()
        // The real path: /data/user/0 is itself a link, and links on the way are refused.
        val problem = ArchiveNative.extract(file.path, folder.canonicalPath, stem(file), null) ?: return
        android.util.Log.w("Adawat", "libarchive could not extract ${file.name}: $problem")
        folder.deleteRecursively()
        throw AdawatException(when (problem) {
            "EMPTY" -> "The source archive is empty."
            else -> "This archive is encrypted or uses a format Adawat cannot open."
        })
    }

    /** Each archive unpacked into a folder named after it. */
    suspend fun extract(files: List<File>, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        coroutineContext.ensureActive()
        context.report("Extracting ${file.name}")
        context.progress(index.toDouble() / files.size)
        var folder = context.named(stem(file))
        extractTo(file, folder)
        // An archive holding a single folder gives that folder, not a folder inside a folder.
        val inside = folder.listFiles().orEmpty()
        if (inside.size == 1 && inside[0].isDirectory) {
            val inner = context.named(inside[0].name)
            if (inside[0].renameTo(inner)) { folder.delete(); folder = inner }
        }
        folder
    }

    /** Repacks an archive in another format: extract to a scratch folder, pack again. */
    suspend fun convert(files: List<File>, format: String, context: OperationContext): List<File> = files.map { file ->
        coroutineContext.ensureActive()
        context.report("Converting ${file.name}")
        val scratch = File(context.out, ".unpacked-${System.nanoTime()}")
        try {
            extractTo(file, scratch)
            val output = context.pathFor(File(file.parentFile, stem(file) + ".x"), if (format == "tgz") "tar.gz" else format)
            write(output, format, scratch.listFiles().orEmpty().sortedBy { it.name })
            output
        } finally {
            scratch.deleteRecursively()
        }
    }
}
