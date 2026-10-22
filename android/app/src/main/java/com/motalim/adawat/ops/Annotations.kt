package com.motalim.adawat.ops

import android.graphics.Bitmap
import android.graphics.BitmapShader
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PointF
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import com.motalim.adawat.AdawatApp
import com.motalim.adawat.core.OperationContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

enum class AnnotationTool { Arrow, Highlight, Text, Rectangle, Ellipse, Draw, Callout, Magnifier }

/** A mark on the picture. Positions are shares (0…1) from the top left, so the same marks draw on the preview and the full photo. */
data class Annotation(
    val tool: AnnotationTool,
    val color: Int,
    var a: PointF,
    var b: PointF = PointF(a.x, a.y),
    val points: MutableList<PointF> = mutableListOf(),
    var text: String = "",
    var number: Int = 0,
) {
    fun copy(): Annotation = Annotation(tool, color, PointF(a.x, a.y), PointF(b.x, b.y), points.map { PointF(it.x, it.y) }.toMutableList(), text, number)

    /** Where a touch picks the mark, in shares; [aspect] is width over height. */
    fun bounds(aspect: Float): RectF = when (tool) {
        AnnotationTool.Draw -> if (points.isEmpty()) RectF() else RectF(points.minOf { it.x }, points.minOf { it.y }, points.maxOf { it.x }, points.maxOf { it.y }).apply { inset(-0.02f, -0.02f) }
        AnnotationTool.Text -> RectF(a.x, a.y, min(1f, a.x + max(4, text.length) * 0.028f / max(aspect, 0.3f)), a.y + 0.07f)
        AnnotationTool.Callout -> RectF(a.x - 0.04f, a.y - 0.04f * aspect, a.x + 0.04f, a.y + 0.04f * aspect)
        AnnotationTool.Magnifier -> RectF(a.x - b.x, a.y - b.x * aspect, a.x + b.x, a.y + b.x * aspect)
        else -> RectF(min(a.x, b.x), min(a.y, b.y), max(a.x, b.x), max(a.y, b.y)).apply { inset(-0.015f, -0.015f) }
    }

    fun move(dx: Float, dy: Float) {
        a.offset(dx, dy)
        if (tool != AnnotationTool.Magnifier && tool != AnnotationTool.Callout && tool != AnnotationTool.Text) b.offset(dx, dy)
        points.forEach { it.offset(dx, dy) }
    }

    fun json(): JSONObject = JSONObject().put("tool", tool.name).put("color", color).put("ax", a.x).put("ay", a.y).put("bx", b.x).put("by", b.y)
        .put("text", text).put("number", number).put("points", JSONArray().apply { points.forEach { put(it.x.toDouble()); put(it.y.toDouble()) } })

    companion object {
        fun from(o: JSONObject) = Annotation(
            AnnotationTool.valueOf(o.getString("tool")), o.getInt("color"),
            PointF(o.getDouble("ax").toFloat(), o.getDouble("ay").toFloat()), PointF(o.getDouble("bx").toFloat(), o.getDouble("by").toFloat()),
            o.getJSONArray("points").let { arr -> (0 until arr.length() / 2).map { PointF(arr.getDouble(it * 2).toFloat(), arr.getDouble(it * 2 + 1).toFloat()) }.toMutableList() },
            o.optString("text"), o.optInt("number"),
        )
    }
}

/** Draws annotations at any size; the editor's preview and the saved photo use the same code. */
object AnnotationRenderer {
    val colors = listOf(0xFFED4236.toInt(), 0xFFFF5200.toInt(), 0xFFFFCC1A.toInt(), 0xFF33B859.toInt(), 0xFF2978F5.toInt(), Color.WHITE, 0xFF141414.toInt())

    private val bold: Typeface by lazy {
        runCatching { androidx.core.content.res.ResourcesCompat.getFont(AdawatApp.instance, com.motalim.adawat.R.font.noto_arabic_bold) }.getOrNull() ?: Typeface.DEFAULT_BOLD
    }

    fun draw(canvas: Canvas, annotations: List<Annotation>, width: Float, height: Float, photo: Bitmap?) {
        val shortSide = min(width, height)
        val line = max(2f, shortSide * 0.007f)
        fun p(point: PointF) = PointF(point.x * width, point.y * height)
        fun r(a: PointF, b: PointF) = RectF(min(a.x, b.x) * width, min(a.y, b.y) * height, max(a.x, b.x) * width, max(a.y, b.y) * height)
        for (note in annotations) {
            val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = note.color; style = Paint.Style.STROKE; strokeWidth = line; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND
            }
            when (note.tool) {
                AnnotationTool.Arrow -> {
                    val from = p(note.a); val to = p(note.b)
                    val angle = atan2(to.y - from.y, to.x - from.x)
                    val width1 = line * 1.3f
                    val head = max(width1 * 4.5f, 10f)
                    val base = PointF(to.x - cos(angle) * head * 0.8f, to.y - sin(angle) * head * 0.8f)
                    canvas.drawLine(from.x, from.y, base.x, base.y, Paint(paint).apply { strokeWidth = width1 })
                    val tip = Path().apply {
                        moveTo(to.x, to.y)
                        lineTo(to.x - cos(angle - 0.45f) * head, to.y - sin(angle - 0.45f) * head)
                        lineTo(to.x - cos(angle + 0.45f) * head, to.y - sin(angle + 0.45f) * head)
                        close()
                    }
                    canvas.drawPath(tip, Paint(paint).apply { style = Paint.Style.FILL })
                }
                AnnotationTool.Highlight -> canvas.drawRect(r(note.a, note.b), Paint().apply { color = (note.color and 0xFFFFFF) or (97 shl 24) })
                AnnotationTool.Rectangle -> canvas.drawRoundRect(r(note.a, note.b), line, line, paint)
                AnnotationTool.Ellipse -> canvas.drawOval(r(note.a, note.b), paint)
                AnnotationTool.Draw -> if (note.points.size > 1) {
                    val path = Path()
                    note.points.forEachIndexed { i, pt -> val q = p(pt); if (i == 0) path.moveTo(q.x, q.y) else path.lineTo(q.x, q.y) }
                    canvas.drawPath(path, paint)
                }
                AnnotationTool.Text -> if (note.text.isNotEmpty()) {
                    val text = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { color = note.color; textSize = shortSide * 0.05f; typeface = bold }
                    val at = p(note.a)
                    val available = max(shortSide * 0.1f, width - at.x).toInt()
                    val layout = StaticLayout.Builder.obtain(note.text, 0, note.text.length, text, available)
                        .setAlignment(Layout.Alignment.ALIGN_NORMAL).setTextDirection(android.text.TextDirectionHeuristics.FIRSTSTRONG_LTR).build()
                    canvas.save(); canvas.translate(at.x, at.y); layout.draw(canvas); canvas.restore()
                }
                AnnotationTool.Callout -> {
                    val radius = shortSide * 0.032f
                    val c = p(note.a)
                    canvas.drawCircle(c.x, c.y, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = note.color })
                    val light = Color.red(note.color) + Color.green(note.color) + Color.blue(note.color) > 2.4 * 255
                    val number = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                        color = if (light) Color.BLACK else Color.WHITE; textSize = radius * 1.15f; typeface = Typeface.DEFAULT_BOLD; textAlign = Paint.Align.CENTER
                    }
                    canvas.drawText(note.number.toString(), c.x, c.y - (number.descent() + number.ascent()) / 2, number)
                }
                AnnotationTool.Magnifier -> if (photo != null) {
                    val c = p(note.a)
                    val radius = note.b.x * width
                    // Twice the size, centred on the same spot.
                    val shader = BitmapShader(photo, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP).apply {
                        setLocalMatrix(Matrix().apply { setScale(2f * width / photo.width, 2f * height / photo.height); postTranslate(-c.x, -c.y) })
                    }
                    canvas.drawCircle(c.x, c.y, radius, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.shader = shader })
                    canvas.drawCircle(c.x, c.y, radius, Paint(paint).apply { color = Color.WHITE; strokeWidth = line * 1.2f })
                    canvas.drawCircle(c.x, c.y, radius + line * 0.8f, Paint(paint).apply { color = 0x40000000; strokeWidth = line * 0.4f })
                }
            }
        }
    }

    fun encode(annotations: List<Annotation>) = JSONArray().apply { annotations.forEach { put(it.json()) } }.toString()

    fun decode(text: String): List<Annotation> = runCatching { JSONArray(text).let { a -> (0 until a.length()).map { Annotation.from(a.getJSONObject(it)) } } }.getOrDefault(emptyList())

    suspend fun save(files: List<File>, context: OperationContext): List<File> {
        context.report("Saving annotated copy")
        val file = files[0]
        val photo = Images.decode(file)
        val result = photo.copy(Bitmap.Config.ARGB_8888, true)
        draw(Canvas(result), decode(context.text("annotations", "[]")), result.width.toFloat(), result.height.toFloat(), photo)
        val format = ImageTools.editedFormat(file)
        val output = context.pathFor(file, format.ext, "annotated")
        ImageOps.write(result, format, output, quality = 92)
        ImageOps.copyMetadata(file, output)
        return listOf(output)
    }
}
