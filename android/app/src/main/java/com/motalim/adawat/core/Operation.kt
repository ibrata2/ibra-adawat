package com.motalim.adawat.core

import java.io.File

enum class WheelMode { Convert, Tools, Actions }

/** Files in, result files out, run off the main thread. Results are written to [OperationContext.out] and saved by the app. */
typealias OperationWork = suspend (files: List<File>, context: OperationContext) -> List<File>

/** One choice on the wheel: a conversion, a tool or an action. */
class Operation(
    val id: String,
    val title: String,
    /** A Material Icons Round code point, drawn in tool and action petals. */
    val icon: Char,
    val run: OperationWork,
    /** Format name drawn alone in the petal instead of an icon, such as "PNG". */
    val badge: String? = null,
    /** Shorter petal label for tools and actions, such as "METADATA". */
    val short: String? = null,
    /** The options screen this choice opens first, by name. */
    val window: String? = null,
    val producesFiles: Boolean = true,
) {
    val wheelLabel get() = badge ?: L10n.upper(L10n.t(short ?: title))
}

/** How an operation reports back: progress for the notification, and an optional closing message. */
class OperationContext(
    /** Where results are written before they are saved to the shared folders. */
    val out: File,
    /** Choices made on the tool's options screen, by name; empty for one-step runs, which use the defaults. */
    val options: Map<String, String> = emptyMap(),
    private val onReport: (String) -> Unit = {},
    private val onProgress: (Double) -> Unit = {},
) {
    var message: String? = null
        private set

    fun int(key: String, default: Int) = options[key]?.toIntOrNull() ?: default
    fun float(key: String, default: Float) = options[key]?.toFloatOrNull() ?: default
    fun bool(key: String, default: Boolean) = options[key]?.toBooleanStrictOrNull() ?: default
    fun text(key: String, default: String) = options[key] ?: default

    /** A web address the result names, such as a QR code's, offered as a button. */
    var link: String? = null

    fun report(text: String) = onReport(L10n.t(text))

    /** Fraction done (0…1) of the whole operation. */
    fun progress(fraction: Double) = onProgress(fraction.coerceIn(0.0, 1.0))

    /** Maps one file's own 0…1 progress onto the whole operation. */
    fun progressFor(index: Int, count: Int): (Double) -> Unit = { progress((index + it) / maxOf(count, 1)) }

    /** A message shown when the work finishes instead of the default one. */
    fun finish(text: String) { message = L10n.t(text) }

    /** A result path next to the others: the source name with a new extension, made unique. */
    fun pathFor(source: File, extension: String, suffix: String? = null): File {
        val base = source.name.substringBeforeLast('.').ifEmpty { source.name } + (suffix?.let { " $it" } ?: "")
        return unique(File(out, "$base.$extension"))
    }

    fun named(name: String): File = unique(File(out, name))

    private fun unique(file: File): File {
        if (!file.exists()) return file
        val stem = file.nameWithoutExtension
        val ext = file.extension.let { if (it.isEmpty()) "" else ".$it" }
        var n = 2
        while (true) {
            val candidate = File(file.parentFile, "$stem $n$ext")
            if (!candidate.exists()) return candidate
            n++
        }
    }
}

class AdawatException(message: String) : Exception(message) {
    override val message: String get() = L10n.t(super.message ?: "")
}
