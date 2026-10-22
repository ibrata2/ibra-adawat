package com.motalim.adawat.ui.editors

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.motalim.adawat.core.L10n
import com.motalim.adawat.ops.PhotoMetadata
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ui.iconText
import java.io.File

/** Metadata: every field grouped; the description fields can be edited, and any field removed. */
@Composable
fun MetadataEditor(files: List<File>, onRun: (Map<String, String>) -> Unit, onBack: () -> Unit) {
    val file = files[0]
    val fields = remember(file) { PhotoMetadata.read(file) }
    // Tag to new value, or to null when removed.
    val changes = remember { mutableStateMapOf<String, String?>() }
    EditorFrame("Metadata", "Save", onAction = {
        if (changes.isEmpty()) onBack() else onRun(mapOf("changes" to PhotoMetadata.encode(changes.toMap())))
    }, onBack = onBack, secondary = "Remove All" to { onRun(mapOf("removeAll" to "true")) }) {
        Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()).padding(16.dp)) {
            Text(file.name, color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodyMedium)
            val visible = fields.filter { !(changes.containsKey(it.tag) && changes[it.tag] == null) }
            if (visible.isEmpty()) Text(L10n.t("No metadata."), color = EditorText, modifier = Modifier.padding(top = 16.dp))
            visible.groupBy { it.group }.forEach { (group, items) ->
                PanelLabel(group)
                items.forEach { field ->
                    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(L10n.t(field.name), color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.width(104.dp))
                        if (field.editable) {
                            OutlinedTextField(
                                changes[field.tag] ?: field.value, { changes[field.tag] = it }, singleLine = field.tag != androidx.exifinterface.media.ExifInterface.TAG_USER_COMMENT,
                                modifier = Modifier.weight(1f), textStyle = MaterialTheme.typography.bodyMedium.copy(color = EditorText),
                                colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Look.Accent, unfocusedBorderColor = Color(0xFF3A3B40), cursorColor = Look.Accent),
                            )
                        } else {
                            Text("⁨${field.value}⁩", color = EditorText, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                        }
                        if (field.value.isNotEmpty() || changes[field.tag]?.isNotEmpty() == true) {
                            IconButton(onClick = { changes[field.tag] = null }) {
                                Text(iconText(''), fontFamily = Look.Icons, color = Color(0xFF9A9CA3), fontSize = 18.sp)
                            }
                        }
                    }
                }
            }
            Text(L10n.t("For photos, Remove All also clears hidden camera and processing data. The original stays unchanged."),
                 color = Color(0xFF9A9CA3), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 16.dp))
        }
    }
}
