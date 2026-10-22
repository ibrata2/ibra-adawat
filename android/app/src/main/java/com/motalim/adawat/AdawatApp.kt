package com.motalim.adawat

import android.app.Application
import com.motalim.adawat.core.L10n
import com.motalim.adawat.work.Inputs
import kotlin.concurrent.thread

class AdawatApp : Application() {
    companion object {
        /** For tools that reach the clipboard or their bundled data; set before anything else runs. */
        lateinit var instance: AdawatApp
            private set
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
        L10n.load(this)
        thread(name = "cleanup", isDaemon = true) { Inputs.cleanUp(this) }
    }
}
