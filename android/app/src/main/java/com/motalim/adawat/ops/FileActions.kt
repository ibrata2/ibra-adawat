package com.motalim.adawat.ops

import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationWork

/** The actions wheel. Each one needs the screen, so the wheel performs them itself; these only name them. */
object FileActions {
    private val app: OperationWork = { _, _ -> throw AdawatException("This action needs the app.") }
    val Share = app
    val OpenWith = app
    val Copy = app
    val SaveTo = app
    val Print = app
    val Details = app
}
