package com.motalim.adawat.docs

import com.motalim.adawat.core.AdawatException
import com.motalim.adawat.core.OperationContext
import com.motalim.adawat.core.ext
import java.io.File
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/** A sheet as plain cells: text, or numbers kept as numbers. */
class Sheet(val name: String, val rows: List<List<Any?>>)

/** Spreadsheet conversions: Excel, OpenDocument, CSV and TSV in; Excel, OpenDocument, CSV, TSV, PDF and HTML out. */
object SheetOps {
    val targets = listOf("xlsx", "ods", "csv", "tsv", "pdf", "html")

    fun title(target: String) = target.uppercase()

    suspend fun convert(files: List<File>, target: String, context: OperationContext): List<File> = files.mapIndexed { index, file ->
        context.report("Converting ${index + 1} of ${files.size}")
        context.progress(index.toDouble() / files.size)
        val sheets = read(file)
        if (sheets.isEmpty()) throw AdawatException("${file.name} has no readable sheet.")
        val output = context.pathFor(file, target)
        write(sheets, target, output, file.nameWithoutExtension)
        output
    }

    fun write(sheets: List<Sheet>, target: String, output: File, title: String) {
        when (target) {
            "xlsx" -> writeXlsx(sheets, output)
            "ods" -> writeOds(sheets, output)
            // A byte-order mark, so Excel opens Arabic CSV as UTF-8.
            "csv", "tsv" -> output.writeText("\uFEFF" + delimited(sheets[0], if (target == "csv") ',' else '\t'))
            "html" -> output.writeText(DocText.html(asDoc(sheets, title)))
            "pdf" -> DocPdf.write(asDoc(sheets, title), output)
        }
    }

    /** The sheets as tables under their names, for HTML and PDF. */
    private fun asDoc(sheets: List<Sheet>, title: String) = Doc(title).apply {
        for (sheet in sheets) {
            if (sheets.size > 1) blocks += Heading(2, listOf(Span(sheet.name)))
            if (sheet.rows.isNotEmpty()) blocks += Table(sheet.rows.map { row -> row.map { listOf(Span(text(it))) } }, headerRow = false)
        }
    }

    private fun text(value: Any?): String = when (value) {
        null -> ""
        is Double -> if (value == Math.floor(value) && kotlin.math.abs(value) < 1e15) value.toLong().toString() else value.toString()
        else -> value.toString()
    }

    // Reading

    fun read(file: File): List<Sheet> = when (file.ext) {
        "xlsx" -> readXlsx(file)
        "ods" -> readOds(file)
        "csv" -> listOf(readDelimited(file, ','))
        "tsv" -> listOf(readDelimited(file, '\t'))
        "xls" -> throw AdawatException("Old Excel files (.xls) cannot be read on this phone. Save them as .xlsx first.")
        else -> throw AdawatException("${file.name} is not a spreadsheet.")
    }

    /** The column number (0-based) of a cell reference such as "BC12". */
    private fun column(reference: String): Int {
        var n = 0
        for (c in reference) { if (!c.isLetter()) break; n = n * 26 + (c.uppercaseChar() - 'A' + 1) }
        return n - 1
    }

    private val dateFormats = setOf(14, 15, 16, 17, 22) + (27..36) + (45..47) + (50..58)

    private fun readXlsx(file: File): List<Sheet> = ZipFile(file).use { zip ->
        fun part(name: String) = zip.getEntry(name)?.let { e -> zip.getInputStream(e).use { Xml.parse(it) } }
        val workbook = part("xl/workbook.xml") ?: throw AdawatException("${file.name} is not a spreadsheet.")
        val rels = part("xl/_rels/workbook.xml.rels")?.descendants("Relationship")?.associate { (it.attr("Id") ?: "") to (it.attr("Target") ?: "") }.orEmpty()
        val shared = part("xl/sharedStrings.xml")?.descendants("si")?.map { si -> si.descendants("t").joinToString("") { it.innerText } }?.toList().orEmpty()
        // Which cell styles show dates, so serial numbers can be shown as dates.
        val styles = part("xl/styles.xml")
        val customDates = styles?.descendants("numFmt")?.filter { f -> f.attr("formatCode")?.let { code -> Regex("[dmy]", RegexOption.IGNORE_CASE).containsMatchIn(code.replace(Regex("\\[[^]]*]|\"[^\"]*\""), "")) } == true }
            ?.mapNotNull { it.attr("numFmtId")?.toIntOrNull() }?.toSet().orEmpty()
        val xfs = styles?.descendants("cellXfs")?.firstOrNull()?.elements("xf")?.map { it.attr("numFmtId")?.toIntOrNull() ?: 0 }.orEmpty()
        val day = SimpleDateFormat("yyyy-MM-dd", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }
        workbook.descendants("sheet").map { sheet ->
            val target = rels[sheet.attr("r:id") ?: ""] ?: ""
            val path = if (target.startsWith("/")) target.drop(1) else "xl/" + target
            val xml = part(path)
            val rows = mutableListOf<MutableList<Any?>>()
            xml?.descendants("row")?.forEach { row ->
                val index = (row.attr("r")?.toIntOrNull() ?: (rows.size + 1)) - 1
                while (rows.size <= index) rows += mutableListOf<Any?>()
                val cells = rows[index]
                for (cell in row.elements("c")) {
                    val col = cell.attr("r")?.let(::column) ?: cells.size
                    while (cells.size <= col) cells += null
                    val raw = cell.child("v")?.innerText
                    val style = cell.attr("s")?.toIntOrNull()?.let { xfs.getOrNull(it) } ?: 0
                    cells[col] = when (cell.attr("t")) {
                        "s" -> raw?.toIntOrNull()?.let { shared.getOrNull(it) }
                        "inlineStr" -> cell.descendants("t").joinToString("") { it.innerText }
                        "b" -> raw == "1"
                        "str", "e" -> raw
                        else -> raw?.toDoubleOrNull()?.let { number ->
                            if (style in dateFormats || style in customDates) day.format(java.util.Date(((number - 25569) * 86_400_000).toLong())) else number
                        }
                    }
                }
            }
            rows.forEach { while (it.isNotEmpty() && it.last() == null) it.removeAt(it.lastIndex) }
            while (rows.isNotEmpty() && rows.last().isEmpty()) rows.removeAt(rows.lastIndex)
            Sheet(sheet.attr("name") ?: "Sheet1", rows)
        }.toList()
    }

    private fun readOds(file: File): List<Sheet> = ZipFile(file).use { zip ->
        val content = zip.getEntry("content.xml")?.let { e -> zip.getInputStream(e).use { Xml.parse(it) } } ?: throw AdawatException("${file.name} is not a spreadsheet.")
        content.descendants("table:table").map { table ->
            val rows = mutableListOf<List<Any?>>()
            for (row in table.elements("table:table-row")) {
                val repeatRows = (row.attr("table:number-rows-repeated")?.toIntOrNull() ?: 1).coerceAtMost(1000)
                val cells = mutableListOf<Any?>()
                for (cell in row.children.filter { it.name == "table:table-cell" || it.name == "table:covered-table-cell" }) {
                    val repeat = (cell.attr("table:number-columns-repeated")?.toIntOrNull() ?: 1).coerceAtMost(1000)
                    val value: Any? = when (cell.attr("office:value-type")) {
                        "float", "percentage", "currency" -> cell.attr("office:value")?.toDoubleOrNull()
                        "boolean" -> cell.attr("office:boolean-value") == "true"
                        null -> null
                        else -> cell.elements("text:p").joinToString("\n") { it.innerText }
                    }
                    repeat(repeat) { cells += value }
                }
                while (cells.isNotEmpty() && cells.last() == null) cells.removeAt(cells.lastIndex)
                repeat(repeatRows) { rows += cells }
            }
            while (rows.isNotEmpty() && rows.last().isEmpty()) rows.removeAt(rows.lastIndex)
            Sheet(table.attr("table:name") ?: "Sheet1", rows)
        }.toList()
    }

    private fun readDelimited(file: File, comma: Char): Sheet {
        val text = DocumentOps.readText(file)
        // Spreadsheets saved in Europe often separate with semicolons.
        val separator = if (comma == ',' && text.count { it == ';' } > text.count { it == ',' } * 2) ';' else comma
        val rows = mutableListOf<List<Any?>>()
        var row = mutableListOf<Any?>()
        val field = StringBuilder()
        var quoted = false
        var i = 0
        while (i < text.length) {
            val c = text[i]
            if (quoted) {
                if (c == '"' && i + 1 < text.length && text[i + 1] == '"') { field.append('"'); i++ }
                else if (c == '"') quoted = false
                else field.append(c)
            } else when {
                c == '"' && field.isEmpty() -> quoted = true
                c == separator -> { row += typed(field.toString()); field.clear() }
                c == '\n' || c == '\r' -> {
                    if (c == '\r' && i + 1 < text.length && text[i + 1] == '\n') i++
                    row += typed(field.toString()); field.clear(); rows += row; row = mutableListOf()
                }
                else -> field.append(c)
            }
            i++
        }
        if (field.isNotEmpty() || row.isNotEmpty()) { row += typed(field.toString()); rows += row }
        return Sheet(file.nameWithoutExtension, rows)
    }

    private fun typed(text: String): Any? {
        if (text.isEmpty()) return null
        if ((text.length < 16 && !text.startsWith('0')) || text == "0" || text.startsWith("0.")) text.toDoubleOrNull()?.let { return it }
        return text
    }

    // Writing

    private fun ZipOutputStream.put(name: String, text: String, stored: Boolean = false) {
        val data = text.toByteArray()
        val entry = ZipEntry(name)
        if (stored) { entry.method = ZipEntry.STORED; entry.size = data.size.toLong(); entry.crc = CRC32().apply { update(data) }.value }
        putNextEntry(entry); write(data); closeEntry()
    }

    private fun reference(row: Int, col: Int): String {
        var n = col + 1
        val letters = StringBuilder()
        while (n > 0) { val r = (n - 1) % 26; letters.insert(0, 'A' + r); n = (n - 1) / 26 }
        return "$letters${row + 1}"
    }

    private fun writeXlsx(sheets: List<Sheet>, output: File) {
        val names = mutableListOf<String>()
        for (sheet in sheets) {
            var name = sheet.name.filter { it !in "[]*?/\\:" }.ifEmpty { "Sheet${names.size + 1}" }.take(31)
            while (name in names) name = name.take(28) + names.size
            names += name
        }
        ZipOutputStream(output.outputStream().buffered()).use { zip ->
            zip.put("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                sheets.indices.joinToString("") { "<Override PartName=\"/xl/worksheets/sheet${it + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" } +
                "</Types>")
            zip.put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>")
            zip.put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                names.mapIndexed { i, n -> "<sheet name=\"${Xml.escape(n)}\" sheetId=\"${i + 1}\" r:id=\"rId${i + 1}\"/>" }.joinToString("") + "</sheets></workbook>")
            zip.put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                sheets.indices.joinToString("") { "<Relationship Id=\"rId${it + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet${it + 1}.xml\"/>" } +
                "<Relationship Id=\"rId${sheets.size + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>")
            zip.put("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs></styleSheet>")
            sheets.forEachIndexed { i, sheet ->
                val rtl = sheet.rows.flatten().filterIsInstance<String>().any(Bidi::isRightToLeft)
                val widest = (0 until (sheet.rows.maxOfOrNull { it.size } ?: 0)).map { c -> sheet.rows.take(200).maxOfOrNull { text(it.getOrNull(c)).length } ?: 0 }
                val cols = if (widest.isEmpty()) "" else "<cols>" + widest.mapIndexed { c, w -> "<col min=\"${c + 1}\" max=\"${c + 1}\" width=\"${(w + 2).coerceIn(6, 60)}\" customWidth=\"1\"/>" }.joinToString("") + "</cols>"
                val data = StringBuilder()
                sheet.rows.forEachIndexed { r, row ->
                    data.append("<row r=\"${r + 1}\">")
                    row.forEachIndexed { c, value ->
                        val ref = reference(r, c)
                        when (value) {
                            null -> Unit
                            is Double -> data.append("<c r=\"$ref\"><v>${if (value == Math.floor(value) && kotlin.math.abs(value) < 1e15) value.toLong() else value}</v></c>")
                            is Int, is Long -> data.append("<c r=\"$ref\"><v>$value</v></c>")
                            is Boolean -> data.append("<c r=\"$ref\" t=\"b\"><v>${if (value) 1 else 0}</v></c>")
                            else -> data.append("<c r=\"$ref\" t=\"inlineStr\"><is><t xml:space=\"preserve\">${Xml.escape(value.toString())}</t></is></c>")
                        }
                    }
                    data.append("</row>")
                }
                zip.put("xl/worksheets/sheet${i + 1}.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<sheetViews><sheetView workbookViewId=\"0\"${if (rtl) " rightToLeft=\"1\"" else ""}/></sheetViews>$cols<sheetData>$data</sheetData></worksheet>")
            }
        }
    }

    private fun writeOds(sheets: List<Sheet>, output: File) {
        val tables = sheets.joinToString("") { sheet ->
            "<table:table table:name=\"${Xml.escape(sheet.name)}\">" + sheet.rows.joinToString("") { row ->
                "<table:table-row>" + row.joinToString("") { value ->
                    when (value) {
                        is Double -> "<table:table-cell office:value-type=\"float\" office:value=\"$value\"><text:p>${text(value)}</text:p></table:table-cell>"
                        null -> "<table:table-cell/>"
                        else -> "<table:table-cell office:value-type=\"string\"><text:p>${Xml.escape(value.toString())}</text:p></table:table-cell>"
                    }
                } + "</table:table-row>"
            } + "</table:table>"
        }
        ZipOutputStream(output.outputStream().buffered()).use { zip ->
            zip.put("mimetype", "application/vnd.oasis.opendocument.spreadsheet", stored = true)
            zip.put("content.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
                "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" office:version=\"1.3\">" +
                "<office:body><office:spreadsheet>$tables</office:spreadsheet></office:body></office:document-content>")
            zip.put("META-INF/manifest.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.3\">" +
                "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.spreadsheet\"/>" +
                "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>")
        }
    }

    private fun delimited(sheet: Sheet, separator: Char) = buildString {
        for (row in sheet.rows) {
            append(row.joinToString(separator.toString()) { value ->
                val s = text(value)
                if (s.any { it == separator || it == '"' || it == '\n' || it == '\r' }) "\"${s.replace("\"", "\"\"")}\"" else s
            })
            append("\r\n")
        }
    }
}

/** SRT and WebVTT subtitles, either way, or as plain text with the timings removed. */
object SubtitleOps {
    val targets = listOf("srt", "vtt", "txt")

    fun title(target: String) = target.uppercase()

    private class Cue(val start: Double, val end: Double, val text: String)

    private val timing = Regex("""(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})\s*-->\s*(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})""")

    suspend fun convert(files: List<File>, target: String, context: OperationContext): List<File> = files.map { file ->
        val cues = parse(DocumentOps.readText(file))
        if (cues.isEmpty()) throw AdawatException("No valid subtitle cues were found in ${file.name}.")
        val output = context.pathFor(file, target)
        output.writeText(when (target) {
            "srt" -> cues.mapIndexed { i, cue -> "${i + 1}\n${stamp(cue.start, ',')} --> ${stamp(cue.end, ',')}\n${cue.text.replace(Regex("<[^>]+>"), "")}\n\n" }.joinToString("")
            "vtt" -> "WEBVTT\n\n" + cues.joinToString("") { "${stamp(it.start, '.')} --> ${stamp(it.end, '.')}\n${it.text}\n\n" }
            else -> cues.joinToString("\n") { it.text } + "\n"
        })
        output
    }

    private fun parse(text: String): List<Cue> = text.replace("\r\n", "\n").replace('\r', '\n').split(Regex("\\n\\s*\\n")).mapNotNull { block ->
        val lines = block.split('\n')
        val at = lines.indexOfFirst { timing.containsMatchIn(it) }
        if (at < 0) return@mapNotNull null
        val m = timing.find(lines[at])!!.groupValues
        fun time(g: Int) = (m[g].toIntOrNull() ?: 0) * 3600.0 + m[g + 1].toInt() * 60 + m[g + 2].toInt() + m[g + 3].padEnd(3, '0').toInt() / 1000.0
        val body = lines.drop(at + 1).joinToString("\n").trim()
        if (body.isEmpty()) null else Cue(time(1), time(5), body)
    }

    private fun stamp(seconds: Double, separator: Char): String {
        val ms = Math.round(seconds * 1000)
        return String.format(Locale.US, "%02d:%02d:%02d%c%03d", ms / 3_600_000, ms / 60_000 % 60, ms / 1000 % 60, separator, ms % 1000)
    }
}
