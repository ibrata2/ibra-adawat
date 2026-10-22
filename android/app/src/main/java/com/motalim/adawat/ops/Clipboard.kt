package com.motalim.adawat.ops

import android.content.ClipData
import android.content.ClipboardManager
import android.os.Handler
import android.os.Looper
import com.motalim.adawat.AdawatApp

object Clipboard {
    fun copyText(text: String) {
        val app = AdawatApp.instance
        Handler(Looper.getMainLooper()).post {
            app.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("Adawat", text))
        }
    }
}
