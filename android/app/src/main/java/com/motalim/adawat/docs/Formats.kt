package com.motalim.adawat.docs

import com.motalim.adawat.core.AdawatException
import org.commonmark.ext.gfm.tables.TablesExtension
import org.commonmark.parser.Parser
import org.commonmark.renderer.html.HtmlRenderer
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/** Markdown through commonmark with GitHub tables, then the HTML reader, as the desktop does with Markdig. */
object MarkdownFormat {
    private val extensions = listOf(TablesExtension.create())
    private val parser = Parser.builder().extensions(extensions).build()
    private val renderer = HtmlRenderer.builder().extensions(extensions).build()

    fun read(markdown: String, title: String) = HtmlReader.read(renderer.render(parser.parse(markdown)), title)
}

private fun ZipOutputStream.put(name: String, text: String, stored: Boolean = false) = put(name, text.toByteArray(), stored)

private fun ZipOutputStream.put(name: String, data: ByteArray, stored: Boolean = false) {
    val entry = ZipEntry(name)
    if (stored) {
        entry.method = ZipEntry.STORED
        entry.size = data.size.toLong()
        entry.crc = CRC32().apply { update(data) }.value
    }
    putNextEntry(entry)
    write(data)
    closeEntry()
}

/** EPUB 3 books: chapters are read in spine order; written books get a navigation page and embedded pictures. */
object Epub {
    private fun normalize(path: String): String {
        val parts = mutableListOf<String>()
        for (part in path.replace('\\', '/').split('/')) {
            when (part) { ".." -> if (parts.isNotEmpty()) parts.removeAt(parts.lastIndex); ".", "" -> Unit; else -> parts += part }
        }
        return parts.joinToString("/")
    }

    fun read(file: File): Doc = ZipFile(file).use { zip ->
        fun text(name: String) = zip.getEntry(name)?.let { e -> zip.getInputStream(e).use { String(it.readBytes()) } }
        val container = text("META-INF/container.xml")?.let { Xml.parse(it.byteInputStream()) } ?: throw AdawatException("The EPUB does not identify a readable package document.")
        val opfPath = container.descendants().firstOrNull { it.localName == "rootfile" }?.attr("full-path") ?: throw AdawatException("The EPUB does not identify a readable package document.")
        val opf = Xml.parse((text(opfPath) ?: throw AdawatException("The EPUB does not identify a readable package document.")).byteInputStream())
        val folder = if (opfPath.contains('/')) opfPath.substringBeforeLast('/') + "/" else ""
        val manifest = opf.descendants().filter { it.localName == "item" }.associate { (it.attr("id") ?: "") to (it.attr("href") ?: "") }
        val title = opf.descendants().firstOrNull { it.localName == "title" }?.innerText?.takeIf { it.isNotBlank() } ?: file.nameWithoutExtension
        val doc = Doc(title)
        val spine = opf.descendants().filter { it.localName == "itemref" }.map { it.attr("idref") ?: "" }.toList()
        for (id in spine) {
            val href = manifest[id] ?: continue
            val chapterPath = normalize(folder + href)
            val html = text(chapterPath) ?: continue
            val chapterFolder = if (chapterPath.contains('/')) chapterPath.substringBeforeLast('/') + "/" else ""
            if (doc.blocks.isNotEmpty()) doc.blocks += PageBreak
            HtmlReader.read(html, title, { src ->
                val entry = zip.getEntry(normalize(chapterFolder + src)) ?: return@read null
                val ext = src.substringAfterLast('.').lowercase()
                zip.getInputStream(entry).use { it.readBytes() } to (if (ext == "png" || ext == "gif") ext else "jpg")
            }, doc)
        }
        if (doc.blocks.isEmpty()) throw AdawatException("The EPUB has no readable chapters.")
        doc
    }

    /** XHTML closes its empty tags. */
    private fun xhtml(html: String) = html.replace("<br>", "<br/>").replace(Regex("<img([^>]*?)(?<!/)>"), "<img$1/>")

    fun write(doc: Doc, file: File) {
        // Each page break starts a new chapter; pictures are stored as files beside the chapters.
        val chapters = mutableListOf(Doc(doc.title))
        for (block in doc.blocks) if (block == PageBreak) chapters += Doc(doc.title) else chapters.last().blocks += block
        chapters.removeAll { it.blocks.isEmpty() }
        if (chapters.isEmpty()) chapters += Doc(doc.title)
        val rtl = Bidi.isRightToLeft(doc.plainText)
        val language = if (rtl) "ar" else "en"
        val dir = if (rtl) "rtl" else "ltr"
        val images = mutableListOf<Pair<String, Picture>>()
        ZipOutputStream(file.outputStream().buffered()).use { zip ->
            // The mimetype entry comes first and uncompressed, as readers require.
            zip.put("mimetype", "application/epub+zip", stored = true)
            zip.put("META-INF/container.xml", """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
</container>""")
            zip.put("OEBPS/style.css", DocText.css(false).replace("max-width: 46em; margin: 2em auto; padding: 0 1em;", "margin: 0 5%;"))
            chapters.forEachIndexed { index, chapter ->
                val body = StringBuilder()
                DocText.body(chapter, body) { picture ->
                    val name = "images/image${images.size + 1}.${if (picture.extension == "png" || picture.extension == "gif") picture.extension else "jpg"}"
                    images += name to picture
                    name
                }
                val heading = chapter.blocks.filterIsInstance<Heading>().firstOrNull()?.text ?: "${doc.title} ${index + 1}"
                zip.put("OEBPS/chapter${index + 1}.xhtml", """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="$language" lang="$language" dir="$dir">
<head><meta charset="utf-8"/><title>${DocText.escape(heading)}</title><link rel="stylesheet" href="style.css"/></head>
<body>
${xhtml(body.toString())}
</body>
</html>""")
            }
            for ((name, picture) in images) zip.put("OEBPS/$name", picture.data, stored = true)
            val nav = chapters.mapIndexed { i, c ->
                "<li><a href=\"chapter${i + 1}.xhtml\">${DocText.escape(c.blocks.filterIsInstance<Heading>().firstOrNull()?.text ?: "${doc.title} ${i + 1}")}</a></li>"
            }.joinToString("\n")
            zip.put("OEBPS/nav.xhtml", """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="$language" lang="$language" dir="$dir">
<head><meta charset="utf-8"/><title>${DocText.escape(doc.title)}</title></head>
<body><nav epub:type="toc" id="toc"><h1>${DocText.escape(doc.title)}</h1><ol>
$nav
</ol></nav></body>
</html>""")
            val items = StringBuilder("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n")
            chapters.indices.forEach { items.append("<item id=\"c${it + 1}\" href=\"chapter${it + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>\n") }
            images.forEachIndexed { i, (name, _) ->
                val type = if (name.endsWith(".png")) "image/png" else if (name.endsWith(".gif")) "image/gif" else "image/jpeg"
                items.append("<item id=\"img${i + 1}\" href=\"$name\" media-type=\"$type\"/>\n")
            }
            val spine = chapters.indices.joinToString("") { "<itemref idref=\"c${it + 1}\"/>" }
            val modified = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())
            zip.put("OEBPS/content.opf", """<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid" xml:lang="$language"${if (rtl) " dir=\"rtl\"" else ""}>
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:uuid:${UUID.randomUUID()}</dc:identifier>
    <dc:title>${DocText.escape(doc.title)}</dc:title>
    <dc:language>$language</dc:language>
    <meta property="dcterms:modified">$modified</meta>
  </metadata>
  <manifest>
$items  </manifest>
  <spine${if (rtl) " page-progression-direction=\"rtl\"" else ""}>$spine</spine>
</package>""")
        }
    }
}

/** OpenDocument text (LibreOffice): paragraphs, headings, lists and tables both ways. */
object Odt {
    fun read(file: File): Doc = ZipFile(file).use { zip ->
        val content = zip.getEntry("content.xml")?.let { e -> zip.getInputStream(e).use { Xml.parse(it) } } ?: throw AdawatException("${file.name} could not be read as a document")
        val doc = Doc(file.nameWithoutExtension)
        val styles = content.descendants("style:style").toList()
        val bold = styles.filter { s -> s.descendants().any { it.attr("fo:font-weight") == "bold" } }.mapNotNull { it.attr("style:name") }.toSet()
        val italic = styles.filter { s -> s.descendants().any { it.attr("fo:font-style") == "italic" } }.mapNotNull { it.attr("style:name") }.toSet()
        fun spans(element: XNode): List<Span> {
            val spans = mutableListOf<Span>()
            fun walk(node: XNode, b: Boolean, i: Boolean) {
                when {
                    node.isText -> spans += Span(node.text, b, i)
                    node.name == "text:s" -> spans += Span(" ".repeat(node.attr("text:c")?.toIntOrNull() ?: 1))
                    node.name == "text:tab" -> spans += Span("\t")
                    node.name == "text:line-break" -> spans += Span("\n")
                    else -> {
                        val style = node.attr("text:style-name")
                        node.children.forEach { walk(it, b || style in bold, i || style in italic) }
                    }
                }
            }
            element.children.forEach { walk(it, false, false) }
            return spans
        }
        val body = content.descendants("office:text").firstOrNull() ?: return@use doc
        fun block(e: XNode, depth: Int, ordered: Boolean) {
            when (e.name) {
                "text:h" -> doc.blocks += Heading(e.attr("text:outline-level")?.toIntOrNull() ?: 1, spans(e))
                "text:p" -> spans(e).takeIf { s -> s.any { it.text.isNotBlank() } }?.let { doc.blocks += Paragraph(it) }
                "text:list" -> for (item in e.elements("text:list-item")) for (child in item.children.filter { !it.isText }) {
                    if (child.name == "text:list") block(child, depth + 1, ordered) else doc.blocks += ListItem(spans(child), ordered, depth)
                }
                "table:table" -> {
                    val rows = e.descendants("table:table-row").map { row -> row.elements("table:table-cell").map { cell -> cell.children.filter { !it.isText }.flatMap(::spans) } }.toList()
                    if (rows.isNotEmpty()) doc.blocks += Table(rows)
                }
                else -> e.children.filter { !it.isText }.forEach { block(it, depth, ordered) }
            }
        }
        body.children.filter { !it.isText }.forEach { block(it, 0, false) }
        doc
    }

    fun write(doc: Doc, file: File) {
        val rtl = Bidi.isRightToLeft(doc.plainText)
        val text = StringBuilder()
        fun inline(spans: List<Span>) = spans.joinToString("") { span ->
            val style = when { span.bold && span.italic -> "BI"; span.bold -> "B"; span.italic -> "I"; else -> null }
            span.text.split('\n').joinToString("<text:line-break/>") { part ->
                if (style == null) Xml.escape(part) else "<text:span text:style-name=\"$style\">${Xml.escape(part)}</text:span>"
            }
        }
        var listOrdered: Boolean? = null
        fun closeList() { if (listOrdered != null) text.append("</text:list>"); listOrdered = null }
        val paragraphStyle = if (rtl) "RTL" else "Standard"
        for (block in doc.blocks) {
            if (block is ListItem) {
                if (listOrdered != block.ordered) {
                    closeList()
                    text.append("<text:list text:style-name=\"${if (block.ordered) "Numbers" else "Bullets"}\">")
                    listOrdered = block.ordered
                }
                text.append("<text:list-item><text:p text:style-name=\"$paragraphStyle\">${inline(block.spans)}</text:p></text:list-item>")
                continue
            }
            closeList()
            when (block) {
                is Heading -> { val l = block.level.coerceIn(1, 6); text.append("<text:h text:style-name=\"Heading_20_$l\" text:outline-level=\"$l\">${inline(block.spans)}</text:h>") }
                is Paragraph -> text.append("<text:p text:style-name=\"$paragraphStyle\">${inline(block.spans)}</text:p>")
                is Quote -> text.append("<text:p text:style-name=\"Quotations\">${inline(block.spans)}</text:p>")
                is CodeBlock -> block.code.trimEnd().split('\n').forEach { text.append("<text:p text:style-name=\"Code\">${Xml.escape(it.trimEnd('\r'))}</text:p>") }
                is Table -> {
                    val columns = block.rows.maxOf { it.size }
                    text.append("<table:table><table:table-column table:number-columns-repeated=\"$columns\"/>")
                    for (row in block.rows) text.append("<table:table-row>" + row.joinToString("") { "<table:table-cell><text:p>${inline(it)}</text:p></table:table-cell>" } + "</table:table-row>")
                    text.append("</table:table>")
                }
                PageBreak -> text.append("<text:p text:style-name=\"Break\"/>")
                else -> Unit
            }
        }
        closeList()
        val content = """<?xml version="1.0" encoding="UTF-8"?>
<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" office:version="1.3">
<office:automatic-styles>
<style:style style:name="B" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style>
<style:style style:name="I" style:family="text"><style:text-properties fo:font-style="italic"/></style:style>
<style:style style:name="BI" style:family="text"><style:text-properties fo:font-weight="bold" fo:font-style="italic"/></style:style>
<style:style style:name="RTL" style:family="paragraph" style:parent-style-name="Standard"><style:paragraph-properties style:writing-mode="rl-tb" fo:text-align="end"/></style:style>
<style:style style:name="Break" style:family="paragraph"><style:paragraph-properties fo:break-after="page"/></style:style>
</office:automatic-styles>
<office:body><office:text>$text</office:text></office:body>
</office:document-content>"""
        ZipOutputStream(file.outputStream().buffered()).use { zip ->
            zip.put("mimetype", "application/vnd.oasis.opendocument.text", stored = true)
            zip.put("content.xml", content)
            zip.put("META-INF/manifest.xml", """<?xml version="1.0" encoding="UTF-8"?>
<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
 <manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>
 <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
</manifest:manifest>""")
        }
    }
}

/**
 * Rich Text, read and written on the phone (the desktop apps hand it to Word or LibreOffice): paragraphs with bold,
 * italic and underline, Unicode and code-page text, right-to-left paragraphs, and tables row by row.
 */
object Rtf {
    fun read(file: File): Doc {
        val source = String(file.readBytes(), Charsets.ISO_8859_1)
        val doc = Doc(file.nameWithoutExtension)
        data class State(var bold: Boolean = false, var italic: Boolean = false, var underline: Boolean = false, var skip: Boolean = false, var unicodeSkip: Int = 1,
                         var size: Int = 24)
        val stack = ArrayDeque<State>().apply { addLast(State()) }
        var codePage = "windows-1252"
        val spans = mutableListOf<Span>()
        var largest = 0
        val pendingBytes = java.io.ByteArrayOutputStream()
        var skipChars = 0
        val text = StringBuilder()
        fun flushBytes() {
            if (pendingBytes.size() > 0) { text.append(String(pendingBytes.toByteArray(), charset(codePage))); pendingBytes.reset() }
        }
        fun flushRun() {
            flushBytes()
            if (text.isNotEmpty()) {
                val s = stack.last()
                spans += Span(text.toString(), s.bold, s.italic, s.underline)
                if (text.isNotBlank()) largest = maxOf(largest, s.size)
                text.clear()
            }
        }
        fun paragraph() {
            flushRun()
            if (spans.any { it.text.isNotBlank() }) {
                // Larger type in a short paragraph reads as a heading (sizes are in half points).
                val short = spans.sumOf { it.text.length } < 160
                doc.blocks += when {
                    short && largest >= 36 -> Heading(1, spans.map { it.copy(bold = false) })
                    short && largest >= 30 -> Heading(2, spans.map { it.copy(bold = false) })
                    short && largest >= 27 -> Heading(3, spans.map { it.copy(bold = false) })
                    else -> Paragraph(spans.toList())
                }
            }
            spans.clear()
            largest = 0
        }
        val ignored = setOf("fonttbl", "colortbl", "stylesheet", "info", "pict", "header", "footer", "headerl", "headerr", "footerl", "footerr", "xmlnstbl",
                            "listtable", "listoverridetable", "themedata", "colorschememapping", "datastore", "latentstyles", "generator", "rsidtbl", "pgdsctbl")
        var i = 0
        while (i < source.length) {
            val c = source[i]
            val state = stack.last()
            when (c) {
                '{' -> { flushRun(); stack.addLast(state.copy()); i++ }
                '}' -> { flushRun(); if (stack.size > 1) stack.removeLast(); i++ }
                '\\' -> {
                    i++
                    if (i >= source.length) break
                    val next = source[i]
                    when {
                        next == '\'' -> {
                            val hex = source.substring(i + 1, minOf(i + 3, source.length))
                            i += 3
                            if (state.skip) continue
                            if (skipChars > 0) { skipChars--; continue }
                            hex.toIntOrNull(16)?.let { pendingBytes.write(it) }
                        }
                        next == '*' -> { state.skip = true; i++ }
                        next.isLetter() -> {
                            val start = i
                            while (i < source.length && source[i].isLetter()) i++
                            val word = source.substring(start, i)
                            val numberStart = i
                            if (i < source.length && (source[i] == '-' || source[i].isDigit())) { i++; while (i < source.length && source[i].isDigit()) i++ }
                            val number = source.substring(numberStart, i).toIntOrNull()
                            if (i < source.length && source[i] == ' ') i++
                            if (word in ignored) { state.skip = true; continue }
                            if (state.skip) continue
                            when (word) {
                                "ansicpg" -> number?.let { codePage = "windows-$it" }
                                "b" -> { flushRun(); state.bold = number != 0 }
                                "i" -> { flushRun(); state.italic = number != 0 }
                                "ul" -> { flushRun(); state.underline = number != 0 }
                                "ulnone" -> { flushRun(); state.underline = false }
                                "plain" -> { flushRun(); state.bold = false; state.italic = false; state.underline = false; state.size = 24 }
                                "fs" -> { flushRun(); state.size = number ?: 24 }
                                "par", "row" -> paragraph()
                                "line" -> { flushBytes(); text.append('\n') }
                                "tab", "cell" -> { flushBytes(); text.append('\t') }
                                "page" -> { paragraph(); doc.blocks += PageBreak }
                                "uc" -> state.unicodeSkip = number ?: 1
                                "u" -> {
                                    flushBytes()
                                    number?.let { text.append((if (it < 0) it + 65536 else it).toChar()) }
                                    skipChars = state.unicodeSkip
                                }
                                "emdash" -> text.append('—')
                                "endash" -> text.append('–')
                                "bullet" -> text.append('•')
                                "lquote" -> text.append('‘')
                                "rquote" -> text.append('’')
                                "ldblquote" -> text.append('“')
                                "rdblquote" -> text.append('”')
                            }
                        }
                        else -> {
                            if (!state.skip) when (next) {
                                '\\', '{', '}' -> { flushBytes(); text.append(next) }
                                '~' -> text.append('\u00A0')
                                '\n', '\r' -> paragraph()
                            }
                            i++
                        }
                    }
                }
                '\r', '\n' -> i++
                else -> {
                    if (!state.skip) {
                        if (skipChars > 0) skipChars-- else { flushBytes(); text.append(c) }
                    }
                    i++
                }
            }
        }
        paragraph()
        return doc
    }

    fun write(doc: Doc, file: File) {
        val out = StringBuilder("{\\rtf1\\ansi\\ansicpg1252\\deff0\\uc1{\\fonttbl{\\f0\\fswiss Arial;}{\\f1\\fmodern Consolas;}}\n")
        fun escape(text: String) = buildString {
            for (ch in text) when {
                ch == '\\' || ch == '{' || ch == '}' -> append('\\').append(ch)
                ch == '\n' -> append("\\line ")
                ch == '\t' -> append("\\tab ")
                ch.code < 128 -> append(ch)
                else -> append("\\u").append(ch.code.toShort().toInt()).append('?')
            }
        }
        fun inline(spans: List<Span>) = spans.joinToString("") { s ->
            val on = (if (s.bold) "\\b " else "") + (if (s.italic) "\\i " else "") + (if (s.underline) "\\ul " else "") + (if (s.code) "\\f1 " else "")
            if (on.isEmpty()) escape(s.text) else "{$on${escape(s.text)}}"
        }
        fun para(spans: List<Span>, extra: String = "") {
            val rtl = Bidi.isRightToLeft(spans.joinToString("") { it.text })
            out.append("{\\pard${if (rtl) "\\rtlpar\\qr" else "\\ltrpar"}$extra ${inline(spans)}\\par}\n")
        }
        var number = 0
        for (block in doc.blocks) {
            if (block !is ListItem) number = 0
            when (block) {
                is Heading -> para(listOf(Span("", bold = true)) + block.spans.map { it.copy(bold = true) }, "\\fs${(40 - block.level * 4).coerceAtLeast(24)}\\sb240\\sa80")
                is Paragraph -> para(block.spans, when (block.align) { Align.Center -> "\\qc"; Align.Justify -> "\\qj"; else -> "" })
                is ListItem -> para(listOf(Span(if (block.ordered) "${++number}. " else "• ")) + block.spans, "\\li${360 + block.depth * 360}")
                is Quote -> para(block.spans.map { it.copy(italic = true) }, "\\li720")
                is CodeBlock -> block.code.trimEnd().split('\n').forEach { para(listOf(Span(it, code = true))) }
                is Table -> block.rows.forEach { row ->
                    out.append("\\trowd")
                    row.indices.forEach { out.append("\\cellx${(it + 1) * 2400}") }
                    row.forEach { cell -> out.append("\\pard\\intbl ${inline(cell)}\\cell") }
                    out.append("\\row\n")
                }
                PageBreak -> out.append("\\page\n")
                is Picture -> Unit
            }
        }
        out.append("}")
        file.writeText(out.toString(), Charsets.US_ASCII)
    }
}
