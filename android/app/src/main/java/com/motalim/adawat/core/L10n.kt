package com.motalim.adawat.core

import android.content.Context
import org.json.JSONObject
import java.util.Locale

/** English is the source text everywhere; Arabic comes from the strings the Windows and Linux apps share. */
object L10n {
    private var strings: Map<String, String> = emptyMap()
    private var patterns: List<Pair<Regex, String>> = emptyList()

    var isArabic = false
        private set

    val isRightToLeft get() = isArabic

    fun load(context: Context) {
        isArabic = when (Settings.language(context)) {
            "ar" -> true
            "en" -> false
            else -> Locale.getDefault().language == "ar"
        }
        // Android's own text sits beside the shared files; entries with {1} are patterns.
        val android = read(context, "android.ar.json")
        // The shared translations win, so the same words read the same in every Adawat app.
        strings = android.filterKeys { !it.contains("{1}") } + read(context, "strings.ar.json")
        patterns = (android.filterKeys { it.contains("{1}") } + read(context, "patterns.ar.json")).map { (key, value) ->
            val pattern = key.split(Regex("""\{\d\}""")).joinToString("(.+?)") { Regex.escape(it) }
            Regex("^$pattern$", RegexOption.DOT_MATCHES_ALL) to value
        }.sortedByDescending { it.first.pattern.length }
    }

    private fun read(context: Context, name: String): Map<String, String> {
        val json = JSONObject(context.assets.open(name).bufferedReader().use { it.readText() })
        return json.keys().asSequence().associateWith { json.getString(it) }
    }

    fun t(english: String): String {
        if (!isArabic || english.isEmpty()) return english
        strings[english]?.let { return it }
        for (suffix in listOf("…", ":")) {
            if (english.endsWith(suffix)) strings[english.dropLast(1)]?.let { return it + suffix }
        }
        for ((pattern, template) in patterns) {
            val match = pattern.matchEntire(english) ?: continue
            var result = template
            for (group in match.groupValues.size - 1 downTo 1) result = result.replace("{$group}", t(match.groupValues[group]))
            return result
        }
        return english
    }

    /** Upper-cased for wheel labels in scripts that have capitals. */
    fun upper(text: String) = if (isArabic) text else text.uppercase(Locale.getDefault())
}
