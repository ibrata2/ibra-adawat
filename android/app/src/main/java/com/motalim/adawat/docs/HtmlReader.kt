package com.motalim.adawat.docs

import android.util.Base64
import org.jsoup.Jsoup
import org.jsoup.nodes.Element
import org.jsoup.nodes.Node
import org.jsoup.nodes.TextNode

/** Reads HTML (web pages, EPUB chapters, Markdown after conversion) into a [Doc], as the desktop's reader does. */
object HtmlReader {
    /** [resolveImage] turns an img src into bytes and an extension, for pictures packed beside the page. */
    fun read(html: String, title: String = "Untitled", resolveImage: ((String) -> Pair<ByteArray, String>?)? = null, into: Doc? = null): Doc {
        val page = Jsoup.parse(html)
        val doc = into ?: Doc(title)
        val heading = page.title().trim()
        if (into == null && heading.isNotEmpty()) doc.title = heading
        Walker(doc, resolveImage).blocks(page.body())
        return doc
    }

    private val blockTags = setOf("p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "table", "pre", "blockquote", "section", "article",
                                  "img", "hr", "figure", "header", "footer", "main")

    private class Walker(private val doc: Doc, private val resolveImage: ((String) -> Pair<ByteArray, String>?)?) {
        private val pending = mutableListOf<Span>()

        fun blocks(node: Element) {
            node.childNodes().forEach(::block)
            flush()
        }

        private fun block(node: Node) {
            if (node is TextNode) { collect(node, Span(""), pending); return }
            if (node !is Element) return
            val name = node.tagName().lowercase()
            when (name) {
                "script", "style", "head", "noscript", "template" -> return
                "h1", "h2", "h3", "h4", "h5", "h6" -> { flush(); doc.blocks += Heading(name[1] - '0', spans(node)) }
                "p", "div", "section", "article", "header", "footer", "main", "aside", "figure", "figcaption", "center", "nav" -> {
                    flush()
                    if (node.children().any { it.tagName().lowercase() in blockTags }) {
                        node.childNodes().forEach(::block)
                        flush()
                    } else {
                        val spans = spans(node)
                        if (spans.any { it.text.isNotBlank() }) doc.blocks += Paragraph(spans, alignOf(node))
                        node.select("img").forEach(::image)
                    }
                }
                "ul", "ol" -> { flush(); list(node, name == "ol", 0) }
                "table" -> {
                    flush()
                    val rows = node.select("tr").map { tr -> tr.children().filter { it.tagName() == "td" || it.tagName() == "th" }.map(::spans) }.filter { it.isNotEmpty() }
                    if (rows.isNotEmpty()) doc.blocks += Table(rows, node.select("th").isNotEmpty())
                }
                "pre" -> { flush(); doc.blocks += CodeBlock(node.wholeText()) }
                "blockquote" -> { flush(); doc.blocks += Quote(spans(node)) }
                "img" -> { flush(); image(node) }
                "hr" -> {
                    flush()
                    if (node.className().contains("break") || node.attr("style").contains("page-break")) doc.blocks += PageBreak
                }
                "br" -> pending += Span("\n")
                else -> {
                    if (node.attr("style").contains("page-break-after") || node.className() == "break") { flush(); doc.blocks += PageBreak; return }
                    collect(node, Span(""), pending)
                }
            }
        }

        private fun list(list: Element, ordered: Boolean, depth: Int) {
            for (li in list.children().filter { it.tagName() == "li" }) {
                val own = mutableListOf<Span>()
                li.childNodes().filter { !(it is Element && (it.tagName() == "ul" || it.tagName() == "ol")) }.forEach { collect(it, Span(""), own) }
                doc.blocks += ListItem(clean(own), ordered, depth)
                li.children().filter { it.tagName() == "ul" || it.tagName() == "ol" }.forEach { list(it, it.tagName() == "ol", depth + 1) }
            }
        }

        private fun image(img: Element) {
            val src = img.attr("src")
            if (src.isEmpty()) return
            val picture = if (src.startsWith("data:")) {
                val comma = src.indexOf(',')
                val meta = if (comma > 5) src.substring(5, comma) else ""
                if (comma > 0 && meta.contains("base64")) runCatching {
                    Base64.decode(src.substring(comma + 1), Base64.DEFAULT) to when {
                        meta.contains("png") -> "png"; meta.contains("gif") -> "gif"; meta.contains("webp") -> "webp"; else -> "jpg"
                    }
                }.getOrNull() else null
            } else resolveImage?.invoke(java.net.URLDecoder.decode(src, "UTF-8"))
            picture?.let { (data, ext) -> doc.blocks += Picture(data, ext, alt = img.attr("alt")) }
        }

        private fun spans(node: Element): List<Span> {
            val spans = mutableListOf<Span>()
            node.childNodes().forEach { collect(it, Span(""), spans) }
            return clean(spans)
        }

        private fun collect(node: Node, style: Span, into: MutableList<Span>) {
            if (node is TextNode) {
                val text = node.wholeText.replace(Regex("\\s+"), " ")
                if (text.isNotEmpty()) into += style.copy(text = text)
                return
            }
            if (node !is Element) return
            val name = node.tagName().lowercase()
            if (name == "script" || name == "style" || name == "img") return
            if (name == "br") { into += Span("\n"); return }
            val css = node.attr("style")
            val href = node.attr("href")
            val next = style.copy(
                bold = style.bold || name == "b" || name == "strong" || css.contains("font-weight:bold") || css.contains("font-weight: bold") || css.contains("font-weight:700"),
                italic = style.italic || name == "i" || name == "em" || css.contains("italic"),
                underline = style.underline || name == "u" || css.contains("underline"),
                code = style.code || name == "code" || name == "kbd" || name == "tt",
                link = if (name == "a" && href.isNotEmpty() && !href.startsWith("#")) href else style.link,
            )
            node.childNodes().forEach { collect(it, next, into) }
        }

        /** Trims the edges and merges neighbours that look the same. */
        private fun clean(spans: List<Span>): List<Span> {
            val merged = mutableListOf<Span>()
            for (span in spans) {
                if (merged.isNotEmpty() && merged.last().copy(text = "") == span.copy(text = "")) merged[merged.lastIndex] = merged.last().copy(text = merged.last().text + span.text)
                else merged += span
            }
            if (merged.isNotEmpty()) merged[0] = merged[0].copy(text = merged[0].text.trimStart())
            if (merged.isNotEmpty()) merged[merged.lastIndex] = merged.last().copy(text = merged.last().text.trimEnd())
            return merged.filter { it.text.isNotEmpty() }
        }

        private fun flush() {
            val spans = clean(pending.toList())
            pending.clear()
            if (spans.any { it.text.isNotBlank() }) doc.blocks += Paragraph(spans)
        }

        private fun alignOf(node: Element): Align {
            val css = (node.attr("style") + " " + node.attr("align")).lowercase()
            return when {
                css.contains("center") -> Align.Center
                css.contains("justify") -> Align.Justify
                css.contains("right") || css.contains("end") -> Align.End
                else -> Align.Start
            }
        }
    }
}
