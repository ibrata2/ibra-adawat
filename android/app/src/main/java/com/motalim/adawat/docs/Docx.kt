package com.motalim.adawat.docs

import android.graphics.BitmapFactory
import com.motalim.adawat.core.AdawatException
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/** Word documents: read into a [Doc], and written with real heading styles, lists, tables, pictures and right-to-left paragraphs for Arabic. */
object Docx {
    fun read(file: File): Doc = ZipFile(file).use { zip ->
        fun part(name: String) = zip.getEntry(name)?.let { e -> zip.getInputStream(e).use { Xml.parse(it) } }
        val document = part("word/document.xml") ?: throw AdawatException("${file.name} could not be read as a document")
        val rels = part("word/_rels/document.xml.rels")?.descendants("Relationship")?.associate { (it.attr("Id") ?: "") to (it.attr("Target") ?: "") }.orEmpty()
        val numbering = part("word/numbering.xml")
        val title = part("docProps/core.xml")?.descendants("dc:title")?.firstOrNull()?.innerText?.takeIf { it.isNotBlank() } ?: file.nameWithoutExtension
        val doc = Doc(title)
        val body = document.descendants("w:body").firstOrNull() ?: return doc

        fun ordered(numId: String, level: Int): Boolean {
            val abstractId = numbering?.descendants("w:num")?.firstOrNull { it.attr("w:numId") == numId }?.child("w:abstractNumId")?.attr("w:val") ?: return false
            val definition = numbering.descendants("w:abstractNum").firstOrNull { it.attr("w:abstractNumId") == abstractId } ?: return false
            val format = definition.elements("w:lvl").firstOrNull { it.attr("w:ilvl") == level.toString() }?.child("w:numFmt")?.attr("w:val")
            return format != null && format != "bullet"
        }

        fun flag(props: XNode?, name: String): Boolean {
            val node = props?.child(name) ?: return false
            return node.attr("w:val").let { it == null || (it != "0" && it != "false") }
        }

        fun runs(container: XNode): List<Span> {
            val spans = mutableListOf<Span>()
            // Only this paragraph's own runs: text boxes inside it hold paragraphs of their own.
            for (run in container.descendants("w:r")) {
                if (run.ancestor("w:p") != container) continue
                val text = run.children.joinToString("") { c ->
                    when (c.name) {
                        "w:t" -> c.innerText
                        "w:tab" -> "\t"
                        "w:br" -> if (c.attr("w:type") == null || c.attr("w:type") == "textWrapping") "\n" else ""
                        else -> ""
                    }
                }
                if (text.isEmpty()) continue
                val props = run.child("w:rPr")
                val link = run.parent?.takeIf { it.name == "w:hyperlink" }?.attr("r:id")?.let { rels[it] }
                val underline = props?.child("w:u")?.attr("w:val")?.let { it != "none" } ?: false
                val font = props?.child("w:rFonts")?.attr("w:ascii")
                spans += Span(text, flag(props, "w:b"), flag(props, "w:i"), underline, link, font == "Consolas" || font == "Courier New")
            }
            return spans
        }

        fun paragraph(p: XNode) {
            for (blip in p.descendants("a:blip")) {
                val target = rels[blip.attr("r:embed") ?: continue] ?: continue
                val entry = zip.getEntry("word/" + target.removePrefix("/word/").removePrefix("./")) ?: zip.getEntry(target.removePrefix("/")) ?: continue
                val data = zip.getInputStream(entry).use { it.readBytes() }
                val ext = target.substringAfterLast('.').lowercase().let { if (it == "png" || it == "gif") it else "jpg" }
                doc.blocks += Picture(data, ext)
            }
            if (p.descendants("w:br").any { it.attr("w:type") == "page" }) doc.blocks += PageBreak
            val spans = runs(p)
            if (spans.isEmpty() || spans.all { it.text.isBlank() }) return
            val pPr = p.child("w:pPr")
            val style = pPr?.child("w:pStyle")?.attr("w:val").orEmpty()
            val outline = pPr?.child("w:outlineLvl")?.attr("w:val")?.toIntOrNull()
            val numPr = pPr?.child("w:numPr")
            when {
                style.startsWith("Heading", true) && style.drop(7).toIntOrNull() != null -> doc.blocks += Heading(style.drop(7).toInt(), spans)
                style.equals("Title", true) -> doc.blocks += Heading(1, spans)
                outline != null && outline < 6 -> doc.blocks += Heading(outline + 1, spans)
                numPr != null -> {
                    val depth = numPr.child("w:ilvl")?.attr("w:val")?.toIntOrNull() ?: 0
                    doc.blocks += ListItem(spans, ordered(numPr.child("w:numId")?.attr("w:val") ?: "0", depth), depth)
                }
                else -> {
                    val align = when (pPr?.child("w:jc")?.attr("w:val")) {
                        "center" -> Align.Center
                        "both" -> Align.Justify
                        "right", "end" -> Align.End
                        else -> Align.Start
                    }
                    doc.blocks += Paragraph(spans, align)
                }
            }
        }

        for (element in body.children) when (element.name) {
            "w:p" -> paragraph(element)
            "w:tbl" -> {
                val rows = element.elements("w:tr").map { row ->
                    row.elements("w:tc").map { cell ->
                        cell.elements("w:p").flatMapIndexed { i, para -> (if (i > 0) listOf(Span("\n")) else emptyList()) + runs(para) }
                    }
                }
                if (rows.isNotEmpty()) doc.blocks += Table(rows)
            }
        }
        doc
    }

    // Writing

    fun write(doc: Doc, file: File) {
        val rels = mutableListOf<String>()
        val media = mutableListOf<Pair<String, ByteArray>>()
        var nextId = 3 // rId1 styles, rId2 numbering
        val body = StringBuilder()

        fun run(span: Span, rtl: Boolean): String {
            val props = StringBuilder()
            if (span.bold) props.append("<w:b/><w:bCs/>")
            if (span.italic) props.append("<w:i/><w:iCs/>")
            if (span.underline) props.append("<w:u w:val=\"single\"/>")
            if (span.code) props.append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/>")
            if (rtl || Bidi.isRightToLeft(span.text)) props.append("<w:rtl/>")
            if (span.link != null) props.append("<w:rStyle w:val=\"Hyperlink\"/>")
            val content = span.text.split('\n').joinToString("<w:br/>") { "<w:t xml:space=\"preserve\">${Xml.escape(it)}</w:t>" }
            val r = "<w:r>${if (props.isNotEmpty()) "<w:rPr>$props</w:rPr>" else ""}$content</w:r>"
            val link = span.link
            if (link != null && (link.startsWith("http") || link.startsWith("mailto:"))) {
                val id = "rId${nextId++}"
                rels += "<Relationship Id=\"$id\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"${Xml.escape(link)}\" TargetMode=\"External\"/>"
                return "<w:hyperlink r:id=\"$id\">$r</w:hyperlink>"
            }
            return r
        }

        fun para(spans: List<Span>, style: String?, align: Align = Align.Start, rtl: Boolean = false, numbering: String = ""): String {
            val props = StringBuilder()
            if (style != null) props.append("<w:pStyle w:val=\"$style\"/>")
            props.append(numbering)
            if (rtl) props.append("<w:bidi/>")
            if (align != Align.Start) {
                val jc = when (align) { Align.Center -> "center"; Align.Justify -> "both"; else -> if (rtl) "left" else "right" }
                props.append("<w:jc w:val=\"$jc\"/>")
            }
            return "<w:p>${if (props.isNotEmpty()) "<w:pPr>$props</w:pPr>" else ""}${spans.joinToString("") { run(it, rtl) }}</w:p>"
        }

        var pictureId = 1
        for (block in doc.blocks) {
            when (block) {
                is Heading -> body.append(para(block.spans, "Heading${block.level.coerceIn(1, 6)}", rtl = Bidi.isRightToLeft(block.text)))
                is Paragraph -> body.append(para(block.spans, null, block.align, Bidi.isRightToLeft(block.text)))
                is Quote -> body.append(para(block.spans, "Quote", rtl = Bidi.isRightToLeft(block.text)))
                is ListItem -> body.append(para(block.spans, "ListParagraph", rtl = Bidi.isRightToLeft(block.text),
                    numbering = "<w:numPr><w:ilvl w:val=\"${block.depth.coerceAtMost(8)}\"/><w:numId w:val=\"${if (block.ordered) 2 else 1}\"/></w:numPr>"))
                is CodeBlock -> block.code.trimEnd().split('\n').forEach { body.append(para(listOf(Span(it.trimEnd('\r'), code = true)), "Code")) }
                is Table -> {
                    body.append("<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders>")
                    for (side in listOf("top", "left", "bottom", "right", "insideH", "insideV")) body.append("<w:$side w:val=\"single\" w:sz=\"4\"/>")
                    body.append("</w:tblBorders></w:tblPr>")
                    block.rows.forEachIndexed { r, row ->
                        body.append("<w:tr>")
                        for (cell in row) {
                            val spans = if (r == 0 && block.headerRow) cell.map { it.copy(bold = true) } else cell
                            body.append("<w:tc>${para(spans, null, rtl = Bidi.isRightToLeft(cell.joinToString("") { it.text }))}</w:tc>")
                        }
                        body.append("</w:tr>")
                    }
                    body.append("</w:tbl>")
                }
                is Picture -> {
                    var data = block.data
                    var ext = if (block.extension == "png" || block.extension == "gif") block.extension else "jpeg"
                    if (block.extension in setOf("webp", "svg", "heic", "avif")) {
                        val bitmap = BitmapFactory.decodeByteArray(data, 0, data.size) ?: continue
                        data = ByteArrayOutputStream().also { bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }.toByteArray()
                        ext = "png"
                    }
                    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }.also { BitmapFactory.decodeByteArray(data, 0, data.size, it) }
                    val w = if (block.pixelWidth > 0) block.pixelWidth else bounds.outWidth.coerceAtLeast(1)
                    val h = if (block.pixelHeight > 0) block.pixelHeight else bounds.outHeight.coerceAtLeast(1)
                    // Fit the 6.5-inch text column at 96 dpi.
                    val emuPerPixel = 9525L
                    val maxWidth = 6L * 914400 + 457200
                    var cx = w * emuPerPixel
                    var cy = h * emuPerPixel
                    if (cx > maxWidth) { cy = cy * maxWidth / cx; cx = maxWidth }
                    val id = "rId${nextId++}"
                    val name = "image$pictureId.${if (ext == "jpeg") "jpg" else ext}"
                    media += name to data
                    rels += "<Relationship Id=\"$id\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/$name\"/>"
                    body.append("<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"$cx\" cy=\"$cy\"/>" +
                        "<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:docPr id=\"$pictureId\" name=\"Picture $pictureId\" descr=\"${Xml.escape(block.alt)}\"/>" +
                        "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
                        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
                        "<pic:nvPicPr><pic:cNvPr id=\"$pictureId\" name=\"Picture $pictureId\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                        "<pic:blipFill><a:blip r:embed=\"$id\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                        "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"$cx\" cy=\"$cy\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
                        "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>")
                    pictureId++
                }
                PageBreak -> body.append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>")
            }
        }
        body.append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:right=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>")

        val document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document " +
            "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>$body</w:body></w:document>"
        val documentRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>" +
            rels.joinToString("") + "</Relationships>"
        val types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpg\" ContentType=\"image/jpeg\"/><Default Extension=\"gif\" ContentType=\"image/gif\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
            "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/></Types>"
        val packageRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>"
        val core = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>${Xml.escape(doc.title)}</dc:title><dc:creator>Adawat</dc:creator></cp:coreProperties>"
        ZipOutputStream(file.outputStream().buffered()).use { zip ->
            fun put(name: String, text: String) { zip.putNextEntry(ZipEntry(name)); zip.write(text.toByteArray()); zip.closeEntry() }
            put("[Content_Types].xml", types)
            put("_rels/.rels", packageRels)
            put("docProps/core.xml", core)
            put("word/document.xml", document)
            put("word/_rels/document.xml.rels", documentRels)
            put("word/styles.xml", styles())
            put("word/numbering.xml", numbering())
            for ((name, data) in media) { zip.putNextEntry(ZipEntry("word/media/$name")); zip.write(data); zip.closeEntry() }
        }
    }

    private fun styles(): String {
        val sizes = listOf(40, 32, 28, 26, 24, 22)
        val headings = (1..6).joinToString("") { level ->
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading$level\"><w:name w:val=\"heading $level\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
                "<w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/><w:outlineLvl w:val=\"${level - 1}\"/></w:pPr>" +
                "<w:rPr><w:b/><w:bCs/><w:color w:val=\"1F1F2E\"/><w:sz w:val=\"${sizes[level - 1]}\"/><w:szCs w:val=\"${sizes[level - 1]}\"/></w:rPr></w:style>"
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Arial\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault>" +
            "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" + headings +
            "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"720\"/></w:pPr><w:rPr><w:i/><w:color w:val=\"555555\"/></w:rPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"Code\"><w:name w:val=\"Code\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"0\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/><w:sz w:val=\"20\"/></w:rPr></w:style>" +
            "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"2F5BD6\"/><w:u w:val=\"single\"/></w:rPr></w:style>" +
            "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/></w:style></w:styles>"
    }

    private fun numbering(): String {
        fun definition(id: Int, ordered: Boolean) = "<w:abstractNum w:abstractNumId=\"$id\">" + (0 until 9).joinToString("") { level ->
            "<w:lvl w:ilvl=\"$level\"><w:start w:val=\"1\"/><w:numFmt w:val=\"${if (ordered) "decimal" else "bullet"}\"/>" +
                "<w:lvlText w:val=\"${if (ordered) "%${level + 1}." else if (level % 2 == 0) "•" else "◦"}\"/><w:lvlJc w:val=\"left\"/>" +
                "<w:pPr><w:ind w:left=\"${720 + 360 * level}\" w:hanging=\"360\"/></w:pPr></w:lvl>"
        } + "</w:abstractNum>"
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            definition(0, false) + definition(1, true) +
            "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>"
    }
}
