package com.motalim.adawat.work

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update

/** What one run of a tool is doing, for the wheel screen and the notification. */
data class JobState(
    val id: String,
    val title: String,
    val status: String = "",
    val progress: Double? = null,
    val finished: Boolean = false,
    val error: String? = null,
    val message: String? = null,
    val link: String? = null,
    val saved: List<Saved> = emptyList(),
)

object Jobs {
    private val states = MutableStateFlow<Map<String, JobState>>(emptyMap())
    val all: StateFlow<Map<String, JobState>> = states

    fun put(state: JobState) = states.update { it + (state.id to state) }

    fun change(id: String, change: (JobState) -> JobState) = states.update { map ->
        map[id]?.let { map + (id to change(it)) } ?: map
    }

    fun forget(id: String) = states.update { it - id }
}
