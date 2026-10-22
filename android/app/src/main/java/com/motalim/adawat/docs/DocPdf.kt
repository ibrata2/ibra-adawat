package com.motalim.adawat.docs

import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.text.Layout
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.StaticLayout
import android.text.TextDirectionHeuristics
import android.text.TextPaint
import android.text.style.ForegroundColorSpan
import android.text.style.StyleSpan
import android.text.style.TypefaceSpan
import android.text.style.UnderlineSpan
import java.io.File
import kotlin.math.max
import kotlin.math.min

/**
 * A [Doc] as a PDF on Letter pages, drawn with Android's own text layout so Arabic is shaped and runs right to left.
 * Long paragraphs and tables flow on to the next page.
 */
object DocPdf {
    private const val PAGE_W = 612
    private const val PAGE_H = 792
    private const val MARGIN = 72f
    private val width get() = PAGE_W - 2 * MARGIN

    private class Writer(val document: PdfDocument) {
        var page: PdfDocument.Page? = null
        var y = MARGIN
        var number = 0

        val canvas: Canvas get() {
            if (page == null) {
                number++
                page = document.startPage(PdfDocument.PageInfo.Builder(PAGE_W, PAGE_H, number).create())
                y = MARGIN
            }
            return page!!.canvas
        }

        fun newPage() { page?.let { document.finishPage(it) }; page = null }

        fun room() = PAGE_H - MARGIN - y

        fun finish() { if (page == null) canvas; newPage() }
    }

    private fun styled(spans: List<Span>, bold: Boolean = false): CharSequence {
        val text = SpannableStringBuilder()
        for (span in spans) {
            val start = text.length
            text.append(span.text)
            val end = text.length
            if (span.bold || bold) text.setSpan(StyleSpan(Typeface.BOLD), start, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            if (span.italic) text.setSpan(StyleSpan(Typeface.ITALIC), start, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            if (span.underline || span.link != null) text.setSpan(UnderlineSpan(), start, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            if (span.link != null) text.setSpan(ForegroundColorSpan(0xFF2F5BD6.toInt()), start, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            if (span.code) text.setSpan(TypefaceSpan("monospace"), start, end, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        }
        return text
    }

    private fun layout(text: CharSequence, paint: TextPaint, w: Float, align: Layout.Alignment = Layout.Alignment.ALIGN_NORMAL): StaticLayout =
        StaticLayout.Builder.obtain(text, 0, text.length, paint, max(1, w.toInt())).setAlignment(align)
            .setTextDirection(TextDirectionHeuristics.FIRSTSTRONG_LTR).setLineSpacing(0f, 1.3f).setIncludePad(false).build()

    /** Draws [layout] at [x], line by line, starting new pages as it runs out of room. */
    private fun flow(writer: Writer, layout: StaticLayout, x: Float, before: Float = 0f, after: Float = 6f) {
        writer.y += before
        var line = 0
        while (line < layout.lineCount) {
            if (writer.room() < layout.getLineBottom(line) - layout.getLineTop(line)) writer.newPage()
            val canvas = writer.canvas
            val top = layout.getLineTop(line)
            var last = line
            while (last + 1 < layout.lineCount && layout.getLineBottom(last + 1) - top <= writer.room()) last++
            canvas.save()
            canvas.translate(x, writer.y - top)
            canvas.clipRect(0f, top.toFloat(), layout.width.toFloat(), layout.getLineBottom(last).toFloat())
            layout.draw(canvas)
            canvas.restore()
            writer.y += layout.getLineBottom(last) - top
            line = last + 1
        }
        writer.y += after
    }

    fun write(doc: Doc, file: File) {
        val document = PdfDocument()
        val writer = Writer(document)
        val body = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 11f; color = 0xFF1B1B1B.toInt() }
        var number = 0
        for (block in doc.blocks) {
            if (block !is ListItem) number = 0
            when (block) {
                is Heading -> {
                    val size = when (block.level) { 1 -> 22f; 2 -> 18f; 3 -> 15f; else -> 13f }
                    val paint = TextPaint(body).apply { textSize = size; typeface = Typeface.DEFAULT_BOLD; color = 0xFF1F1F2E.toInt() }
                    val l = layout(styled(block.spans, bold = true), paint, width)
                    // Keep a heading with the text after it.
                    if (writer.room() < l.height + 40) writer.newPage()
                    flow(writer, l, MARGIN, before = 10f, after = 4f)
                }
                is Paragraph -> {
                    val align = when (block.align) { Align.Center -> Layout.Alignment.ALIGN_CENTER; Align.End -> Layout.Alignment.ALIGN_OPPOSITE; else -> Layout.Alignment.ALIGN_NORMAL }
                    flow(writer, layout(styled(block.spans), body, width, align), MARGIN)
                }
                is ListItem -> {
                    val marker = if (block.ordered) "${++number}. " else "•  "
                    val indent = 18f + block.depth * 18f
                    val rtl = Bidi.isRightToLeft(block.text)
                    val l = layout(SpannableStringBuilder(marker).append(styled(block.spans)), body, width - indent)
                    flow(writer, l, if (rtl) MARGIN else MARGIN + indent, after = 3f)
                }
                is Quote -> {
                    val paint = TextPaint(body).apply { color = 0xFF555555.toInt(); textSkewX = -0.2f }
                    val l = layout(styled(block.spans), paint, width - 18f)
                    val start = writer.y
                    val rtl = Bidi.isRightToLeft(block.text)
                    flow(writer, l, if (rtl) MARGIN else MARGIN + 18f)
                    if (writer.page != null) writer.canvas.drawRect(if (rtl) MARGIN + width - 3f else MARGIN, start, if (rtl) MARGIN + width else MARGIN + 3f, writer.y - 6f,
                                                                    Paint().apply { color = 0xFFCCCCCC.toInt() })
                }
                is CodeBlock -> {
                    val paint = TextPaint(body).apply { typeface = Typeface.MONOSPACE; textSize = 10f }
                    val l = layout(block.code.trimEnd(), paint, width - 16f)
                    if (writer.room() < min(l.height.toFloat(), 200f)) writer.newPage()
                    val start = writer.y
                    writer.canvas.drawRect(MARGIN, start, MARGIN + width, start + min(l.height + 16f, writer.room()), Paint().apply { color = 0xFFF5F5F7.toInt() })
                    flow(writer, l, MARGIN + 8f, before = 8f, after = 14f)
                }
                is Table -> table(writer, block, body)
                is Picture -> {
                    val bitmap = BitmapFactory.decodeByteArray(block.data, 0, block.data.size) ?: continue
                    val scale = min(1f, min(width / bitmap.width, (PAGE_H - 2 * MARGIN) / bitmap.height))
                    val w = bitmap.width * scale
                    val h = bitmap.height * scale
                    if (writer.room() < h) writer.newPage()
                    writer.canvas.drawBitmap(bitmap, null, RectF(MARGIN, writer.y, MARGIN + w, writer.y + h), Paint(Paint.FILTER_BITMAP_FLAG))
                    writer.y += h + 8f
                }
                PageBreak -> writer.newPage()
            }
        }
        writer.finish()
        file.outputStream().buffered().use { document.writeTo(it) }
        document.close()
    }

    private fun table(writer: Writer, table: Table, body: TextPaint) {
        val columns = table.rows.maxOf { it.size }.coerceAtLeast(1)
        val cellW = width / columns
        val pad = 4f
        val border = Paint().apply { color = 0xFFBBBBBB.toInt(); style = Paint.Style.STROKE; strokeWidth = 0.7f }
        val header = Paint().apply { color = 0xFFF1F1F4.toInt() }
        val rtl = Bidi.isRightToLeft(table.text)
        writer.y += 4f
        table.rows.forEachIndexed { r, row ->
            val isHeader = r == 0 && table.headerRow
            val layouts = (0 until columns).map { c -> layout(styled(row.getOrElse(c) { emptyList() }, bold = isHeader), body, cellW - 2 * pad) }
            val height = layouts.maxOf { it.height } + 2 * pad
            if (writer.room() < height) writer.newPage()
            val canvas = writer.canvas
            layouts.forEachIndexed { c, l ->
                // Arabic tables start from the right.
                val column = if (rtl) columns - 1 - c else c
                val x = MARGIN + column * cellW
                if (isHeader) canvas.drawRect(x, writer.y, x + cellW, writer.y + height, header)
                canvas.drawRect(x, writer.y, x + cellW, writer.y + height, border)
                canvas.save(); canvas.translate(x + pad, writer.y + pad); l.draw(canvas); canvas.restore()
            }
            writer.y += height
        }
        writer.y += 8f
    }
}
