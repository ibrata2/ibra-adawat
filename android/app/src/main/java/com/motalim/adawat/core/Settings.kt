package com.motalim.adawat.core

import android.content.Context

object Settings {
    private fun prefs(context: Context) = context.getSharedPreferences("settings", Context.MODE_PRIVATE)

    /** "system", "ar" or "en". */
    fun language(context: Context): String = prefs(context).getString("language", "system") ?: "system"

    fun setLanguage(context: Context, value: String) = prefs(context).edit().putString("language", value).apply()

    var sounds: Boolean = true
}
