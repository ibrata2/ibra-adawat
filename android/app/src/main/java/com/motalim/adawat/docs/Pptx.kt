package com.motalim.adawat.docs

import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/** A small PowerPoint file: one picture filling each slide. */
object Pptx {
    private const val P = "http://schemas.openxmlformats.org/presentationml/2006/main"
    private const val A = "http://schemas.openxmlformats.org/drawingml/2006/main"
    private const val R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    private const val REL = "http://schemas.openxmlformats.org/package/2006/relationships"
    private const val HEAD = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"

    /** [slides] are PNG bytes with their width and height in EMU. */
    fun write(slides: List<Triple<ByteArray, Long, Long>>, file: File) {
        val (cx, cy) = slides.firstOrNull()?.let { it.second to it.third } ?: (12192000L to 6858000L)
        ZipOutputStream(file.outputStream().buffered()).use { zip ->
            fun put(name: String, text: String) { zip.putNextEntry(ZipEntry(name)); zip.write(text.toByteArray()); zip.closeEntry() }
            put("[Content_Types].xml", HEAD + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
                "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
                "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>" +
                "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                slides.indices.joinToString("") { "<Override PartName=\"/ppt/slides/slide${it + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>" } +
                "</Types>")
            put("_rels/.rels", HEAD + "<Relationships xmlns=\"$REL\"><Relationship Id=\"rId1\" Type=\"$R/officeDocument\" Target=\"ppt/presentation.xml\"/></Relationships>")
            put("ppt/presentation.xml", HEAD + "<p:presentation xmlns:a=\"$A\" xmlns:r=\"$R\" xmlns:p=\"$P\" saveSubsetFonts=\"1\">" +
                "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst>" +
                slides.indices.joinToString("") { "<p:sldId id=\"${256 + it}\" r:id=\"rId${it + 2}\"/>" } +
                "</p:sldIdLst><p:sldSz cx=\"$cx\" cy=\"$cy\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>")
            put("ppt/_rels/presentation.xml.rels", HEAD + "<Relationships xmlns=\"$REL\"><Relationship Id=\"rId1\" Type=\"$R/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>" +
                slides.indices.joinToString("") { "<Relationship Id=\"rId${it + 2}\" Type=\"$R/slide\" Target=\"slides/slide${it + 1}.xml\"/>" } +
                "<Relationship Id=\"rId${slides.size + 2}\" Type=\"$R/theme\" Target=\"theme/theme1.xml\"/></Relationships>")
            val tree = "<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld>"
            put("ppt/slideMasters/slideMaster1.xml", HEAD + "<p:sldMaster xmlns:a=\"$A\" xmlns:r=\"$R\" xmlns:p=\"$P\">$tree" +
                "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst></p:sldMaster>")
            put("ppt/slideMasters/_rels/slideMaster1.xml.rels", HEAD + "<Relationships xmlns=\"$REL\"><Relationship Id=\"rId1\" Type=\"$R/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"$R/theme\" Target=\"../theme/theme1.xml\"/></Relationships>")
            put("ppt/slideLayouts/slideLayout1.xml", HEAD + "<p:sldLayout xmlns:a=\"$A\" xmlns:r=\"$R\" xmlns:p=\"$P\" type=\"blank\">$tree<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>")
            put("ppt/slideLayouts/_rels/slideLayout1.xml.rels", HEAD + "<Relationships xmlns=\"$REL\"><Relationship Id=\"rId1\" Type=\"$R/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>")
            put("ppt/theme/theme1.xml", HEAD + theme())
            slides.forEachIndexed { i, (png, w, h) ->
                // Each picture fills its slide; pages of another shape are fitted and centred.
                val scale = minOf(cx.toDouble() / w, cy.toDouble() / h)
                val pw = (w * scale).toLong(); val ph = (h * scale).toLong()
                put("ppt/slides/slide${i + 1}.xml", HEAD + "<p:sld xmlns:a=\"$A\" xmlns:r=\"$R\" xmlns:p=\"$P\"><p:cSld><p:spTree>" +
                    "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
                    "<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Page ${i + 1}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                    "<p:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                    "<p:spPr><a:xfrm><a:off x=\"${(cx - pw) / 2}\" y=\"${(cy - ph) / 2}\"/><a:ext cx=\"$pw\" cy=\"$ph\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
                    "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>")
                put("ppt/slides/_rels/slide${i + 1}.xml.rels", HEAD + "<Relationships xmlns=\"$REL\"><Relationship Id=\"rId1\" Type=\"$R/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"$R/image\" Target=\"../media/image${i + 1}.png\"/></Relationships>")
                zip.putNextEntry(ZipEntry("ppt/media/image${i + 1}.png")); zip.write(png); zip.closeEntry()
            }
        }
    }

    private fun theme(): String {
        fun color(name: String, value: String) = "<a:$name><a:srgbClr val=\"$value\"/></a:$name>"
        val solid = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
        val line = "<a:ln w=\"9525\">$solid</a:ln>"
        return "<a:theme xmlns:a=\"$A\" name=\"Adawat\"><a:themeElements><a:clrScheme name=\"Adawat\">" +
            "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
            color("dk2", "1F1F2E") + color("lt2", "EEECE1") + color("accent1", "5B47E6") + color("accent2", "7CF2C8") + color("accent3", "ED4236") +
            color("accent4", "FFCC1A") + color("accent5", "2978F5") + color("accent6", "33B859") + color("hlink", "2F5BD6") + color("folHlink", "7A4FC9") +
            "</a:clrScheme><a:fontScheme name=\"Adawat\"><a:majorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
            "<a:fmtScheme name=\"Adawat\"><a:fillStyleLst>$solid$solid$solid</a:fillStyleLst><a:lnStyleLst>$line$line$line</a:lnStyleLst>" +
            "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
            "<a:bgFillStyleLst>$solid$solid$solid</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>"
    }
}
