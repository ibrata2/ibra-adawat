package com.motalim.adawat.core

import java.io.File

enum class FileKind { Image, Pdf, Video, Audio, Document, Spreadsheet, Presentation, Subtitle, Archive, Folder, Other }

object FileKinds {
    val images = setOf("jpg", "jpeg", "jfif", "png", "heic", "heif", "webp", "avif", "tif", "tiff", "gif", "bmp", "ico", "svg", "dng",
                       "cr2", "cr3", "nef", "arw", "raf", "orf", "rw2", "psd", "tga", "jxl")
    val videos = setOf("mp4", "m4v", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "ts", "mts", "m2ts", "3gp", "ogv")
    val audios = setOf("mp3", "m4a", "aac", "wav", "flac", "aif", "aiff", "ogg", "oga", "opus", "wma", "alac", "caf", "amr")
    val documents = setOf("txt", "rtf", "doc", "docx", "odt", "html", "htm", "md", "markdown", "epub")
    val spreadsheets = setOf("xlsx", "xls", "csv", "tsv", "ods")
    val presentations = setOf("pptx", "ppt", "odp")
    val subtitles = setOf("srt", "vtt")
    val archives = setOf("zip", "tar", "gz", "tgz", "bz2", "xz", "rar", "7z", "tbz", "txz", "iso", "cab")

    fun of(file: File): FileKind {
        if (file.isDirectory) return FileKind.Folder
        val ext = ext(file)
        return when {
            ext in spreadsheets -> FileKind.Spreadsheet
            ext in presentations -> FileKind.Presentation
            ext in subtitles -> FileKind.Subtitle
            ext == "pdf" -> FileKind.Pdf
            ext in images -> FileKind.Image
            ext in videos -> FileKind.Video
            ext in audios -> FileKind.Audio
            ext in documents -> FileKind.Document
            ext in archives -> FileKind.Archive
            else -> FileKind.Other
        }
    }

    /** The lower-case extension without the dot ("tar.gz" counts as "tgz"). */
    fun ext(file: File): String {
        val name = file.name.lowercase()
        if (name.endsWith(".tar.gz")) return "tgz"
        return name.substringAfterLast('.', "")
    }
}

val File.kind get() = FileKinds.of(this)
val File.ext get() = FileKinds.ext(this)

fun List<File>.kinds(): Set<FileKind> = map { it.kind }.toSet()

/** The single kind shared by every file, if there is one. */
fun List<File>.commonKind(): FileKind? = kinds().singleOrNull()
