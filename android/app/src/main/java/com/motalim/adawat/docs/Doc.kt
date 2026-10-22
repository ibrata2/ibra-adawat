package com.motalim.adawat.docs

import android.util.Base64

/**
 * A document every format reads into and writes out of: headings, paragraphs, lists, tables, pictures and page breaks,
 * with bold, italic, underline, links and code in the text. Small on purpose, as on the desktop: it holds what survives
 * a round trip through every format Adawat writes.
 */
class Doc(var title: String = "Untitled") {
    val blocks = mutableListOf<Block>()

    val plainText: String get() = blocks.map { it.text }.filter { it.isNotEmpty() }.joinToString("\n\n")

    companion object {
        fun fromText(text: String, title: String = "Untitled") = Doc(title).apply {
            for (paragraph in text.replace("\r\n", "\n").split("\n\n")) {
                val lines = paragraph.trim('\n')
                if (lines.isNotEmpty()) blocks += Paragraph(listOf(Span(lines)))
            }
        }
    }
}

data class Span(val text: String, val bold: Boolean = false, val italic: Boolean = false, val underline: Boolean = false,
                val link: String? = null, val code: Boolean = false)

enum class Align { Start, Center, End, Justify }

sealed class Block {
    abstract val text: String
}

private fun List<Span>.joined() = joinToString("") { it.text }

data class Paragraph(val spans: List<Span>, val align: Align = Align.Start) : Block() { override val text get() = spans.joined() }
data class Heading(val level: Int, val spans: List<Span>) : Block() { override val text get() = spans.joined() }
data class ListItem(val spans: List<Span>, val ordered: Boolean, val depth: Int = 0) : Block() { override val text get() = spans.joined() }
data class Table(val rows: List<List<List<Span>>>, val headerRow: Boolean = true) : Block() {
    override val text get() = rows.joinToString("\n") { row -> row.joinToString("\t") { it.joined() } }
}
class Picture(val data: ByteArray, val extension: String, val pixelWidth: Int = 0, val pixelHeight: Int = 0, val alt: String = "") : Block() {
    override val text get() = ""
    val mime get() = when (extension) { "png" -> "image/png"; "gif" -> "image/gif"; "webp" -> "image/webp"; "svg" -> "image/svg+xml"; else -> "image/jpeg" }
}
data class CodeBlock(val code: String) : Block() { override val text get() = code }
data class Quote(val spans: List<Span>) : Block() { override val text get() = spans.joined() }
object PageBreak : Block() { override val text get() = "" }

/** Arabic and Hebrew paragraphs read right to left; this decides from the first strong letter. */
object Bidi {
    fun isRightToLeft(text: String): Boolean {
        for (c in text) {
            if (c in '֐'..'ࣿ' || c in 'יִ'..'ﻼ') return true
            if (c.isLetter()) return false
        }
        return false
    }
}

/** Writes a [Doc] as HTML, Markdown or plain text, the same as the desktop apps. */
object DocText {
    fun html(doc: Doc, forPrint: Boolean = false, pictureSource: ((Picture) -> String)? = null): String {
        val rtl = Bidi.isRightToLeft(doc.plainText)
        val html = StringBuilder()
        html.append("<!DOCTYPE html>\n<html lang=\"${if (rtl) "ar" else "en"}\" dir=\"${if (rtl) "rtl" else "ltr"}\">\n<head>\n<meta charset=\"utf-8\">\n")
        html.append("<title>${escape(doc.title)}</title>\n<style>\n${css(forPrint)}\n</style>\n</head>\n<body>\n")
        body(doc, html, pictureSource)
        html.append("</body>\n</html>\n")
        return html.toString()
    }

    fun css(forPrint: Boolean) = (if (forPrint) "@page { size: Letter; margin: 1in; }\n" else "body { max-width: 46em; margin: 2em auto; padding: 0 1em; }\n") +
        "body { font-family: 'Segoe UI', 'Noto Naskh Arabic', 'Arial', sans-serif; font-size: 11pt; line-height: 1.55; color: #1b1b1b; }\n" +
        "h1, h2, h3, h4 { line-height: 1.25; margin: 1.1em 0 0.4em; }\n" +
        "table { border-collapse: collapse; margin: 0.8em 0; } td, th { border: 1px solid #bbb; padding: 4px 8px; vertical-align: top; }\n" +
        "th { background: #f1f1f4; } img { max-width: 100%; height: auto; } pre, code { font-family: Consolas, monospace; font-size: 0.92em; }\n" +
        "pre { background: #f5f5f7; padding: 0.8em; overflow: auto; white-space: pre-wrap; } blockquote { border-inline-start: 3px solid #ccc; margin: 0.8em 0; padding-inline-start: 1em; color: #555; }\n" +
        ".break { page-break-after: always; break-after: page; }"

    fun body(doc: Doc, html: StringBuilder, pictureSource: ((Picture) -> String)? = null) {
        var openList: Boolean? = null
        fun close() { openList?.let { html.append(if (it) "</ol>\n" else "</ul>\n") }; openList = null }
        for (block in doc.blocks) {
            if (block is ListItem) {
                if (openList != block.ordered) {
                    close()
                    html.append(if (block.ordered) "<ol>\n" else "<ul>\n")
                    openList = block.ordered
                }
                html.append("<li${dir(block.text)}>${inline(block.spans)}</li>\n")
                continue
            }
            close()
            when (block) {
                is Heading -> { val level = block.level.coerceIn(1, 6); html.append("<h$level${dir(block.text)}>${inline(block.spans)}</h$level>\n") }
                is Paragraph -> {
                    val align = when (block.align) { Align.Center -> " style=\"text-align:center\""; Align.End -> " style=\"text-align:end\""; Align.Justify -> " style=\"text-align:justify\""; else -> "" }
                    html.append("<p${dir(block.text)}$align>${inline(block.spans).replace("\n", "<br>")}</p>\n")
                }
                is Table -> {
                    html.append("<table>\n")
                    block.rows.forEachIndexed { r, row ->
                        val tag = if (r == 0 && block.headerRow) "th" else "td"
                        html.append("<tr>").append(row.joinToString("") { cell -> "<$tag${dir(cell.joined())}>${inline(cell)}</$tag>" }).append("</tr>\n")
                    }
                    html.append("</table>\n")
                }
                is Picture -> {
                    val src = pictureSource?.invoke(block) ?: "data:${block.mime};base64,${Base64.encodeToString(block.data, Base64.NO_WRAP)}"
                    html.append("<p><img src=\"$src\" alt=\"${escape(block.alt)}\"></p>\n")
                }
                is CodeBlock -> html.append("<pre><code>${escape(block.code)}</code></pre>\n")
                is Quote -> html.append("<blockquote${dir(block.text)}>${inline(block.spans)}</blockquote>\n")
                PageBreak -> html.append("<div class=\"break\"></div>\n")
                is ListItem -> Unit
            }
        }
        close()
    }

    private fun dir(text: String) = if (Bidi.isRightToLeft(text)) " dir=\"rtl\"" else ""

    fun inline(spans: List<Span>): String = spans.joinToString("") { span ->
        var text = escape(span.text)
        if (span.code) text = "<code>$text</code>"
        if (span.bold) text = "<strong>$text</strong>"
        if (span.italic) text = "<em>$text</em>"
        if (span.underline) text = "<u>$text</u>"
        if (span.link != null) text = "<a href=\"${escape(span.link)}\">$text</a>"
        text
    }

    fun escape(text: String) = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;").replace("'", "&#39;")

    fun markdown(doc: Doc): String {
        val md = StringBuilder()
        var number = 0
        for (block in doc.blocks) {
            if (block !is ListItem) number = 0
            when (block) {
                is Heading -> md.append("#".repeat(block.level.coerceIn(1, 6))).append(' ').append(mdInline(block.spans)).append("\n\n")
                is Paragraph -> md.append(mdInline(block.spans).replace("\n", "  \n")).append("\n\n")
                is ListItem -> md.append(" ".repeat(block.depth * 2)).append(if (block.ordered) "${++number}. " else "- ").append(mdInline(block.spans)).append('\n')
                is Table -> {
                    block.rows.forEachIndexed { r, row ->
                        md.append("| ").append(row.joinToString(" | ") { mdInline(it).replace("|", "\\|") }).append(" |\n")
                        if (r == 0) md.append('|').append(" --- |".repeat(block.rows[0].size)).append('\n')
                    }
                    md.append('\n')
                }
                is Picture -> md.append("![${block.alt}](data:${block.mime};base64,${Base64.encodeToString(block.data, Base64.NO_WRAP)})\n\n")
                is CodeBlock -> md.append("```\n").append(block.code.trimEnd()).append("\n```\n\n")
                is Quote -> md.append("> ").append(mdInline(block.spans)).append("\n\n")
                PageBreak -> md.append("\n---\n\n")
            }
        }
        return md.toString().trimEnd() + "\n"
    }

    private fun mdInline(spans: List<Span>) = spans.joinToString("") { span ->
        if (span.text.isEmpty()) return@joinToString ""
        val text = if (span.code) "`${span.text}`" else span.text.replace("*", "\\*").replace("_", "\\_")
        val trimmed = text.trim()
        if (trimmed.isEmpty()) return@joinToString text
        val lead = text.substring(0, text.indexOf(trimmed[0]))
        val trail = text.substring(text.lastIndexOf(trimmed.last()) + 1)
        var t = trimmed
        if (span.bold) t = "**$t**"
        if (span.italic) t = "*$t*"
        if (span.link != null) t = "[$t](${span.link})"
        lead + t + trail
    }
}
