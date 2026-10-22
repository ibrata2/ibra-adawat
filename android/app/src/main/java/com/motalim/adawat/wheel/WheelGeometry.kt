package com.motalim.adawat.wheel

import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathFillType
import kotlin.math.PI
import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.min
import kotlin.math.sin

/** The wheel's shape in its own 300-unit square, the same numbers as the Mac, Windows and Linux wheels. */
object WheelGeometry {
    const val SIZE = 300f
    const val OUTER_RADIUS = 119.5f
    const val INNER_RADIUS = 48.6f
    const val LABEL_RADIUS = (INNER_RADIUS + OUTER_RADIUS) / 2

    /** Centre of petal [index] of [count], starting at the top and going clockwise. */
    fun position(index: Int, count: Int): Offset {
        val angle = -PI / 2 + index.toDouble() / maxOf(count, 1) * 2 * PI
        return Offset((SIZE / 2 + LABEL_RADIUS * cos(angle)).toFloat(), (SIZE / 2 + LABEL_RADIUS * sin(angle)).toFloat())
    }

    /** The angle of petal [index]'s centre line. */
    fun angle(index: Int, count: Int) = -PI / 2 + index.toDouble() / maxOf(count, 1) * 2 * PI

    /** The petal under [point] (top-left origin), or null over the centre label. A finger is broader than a pointer, so the edge reaches further. */
    fun index(point: Offset, count: Int, reach: Float = 18f): Int? {
        if (count <= 0) return null
        val dx = point.x - SIZE / 2
        val dy = point.y - SIZE / 2
        val distance = hypot(dx, dy)
        if (distance <= INNER_RADIUS - 8 || distance > OUTER_RADIUS + reach) return null
        var angle = atan2(dy.toDouble(), dx.toDouble()) + PI / 2
        if (angle < 0) angle += 2 * PI
        val step = 2 * PI / count
        return ((angle + step / 2) / step).toInt() % count
    }

    fun isCenter(point: Offset) = hypot(point.x - SIZE / 2, point.y - SIZE / 2) < INNER_RADIUS - 8

    fun isOutside(point: Offset, reach: Float = 18f) = hypot(point.x - SIZE / 2, point.y - SIZE / 2) > OUTER_RADIUS + reach

    /** One rounded annular sector, separated from its neighbours by a small gap. */
    fun petal(index: Int, count: Int): Path {
        val c = Offset(SIZE / 2, SIZE / 2)
        val ri = INNER_RADIUS.toDouble()
        val ro = OUTER_RADIUS.toDouble()
        val gap = 2.0
        val innerCorner = 7.0
        val outerCorner = 20.0
        if (count == 1) {
            return Path().apply {
                fillType = PathFillType.EvenOdd
                addOval(Rect(c, ro.toFloat()))
                addOval(Rect(c, ri.toFloat()))
            }
        }
        val step = 2 * PI / count
        val mid = -PI / 2 + index * step
        val start = mid - step / 2
        val end = mid + step / 2
        fun p(radius: Double, angle: Double) = Offset((c.x + radius * cos(angle)).toFloat(), (c.y + radius * sin(angle)).toFloat())
        fun toward(from: Offset, to: Offset, distance: Double): Offset {
            val length = hypot((to.x - from.x).toDouble(), (to.y - from.y).toDouble())
            if (length <= 0) return from
            val t = min(distance / length, 0.5).toFloat()
            return Offset(from.x + (to.x - from.x) * t, from.y + (to.y - from.y) * t)
        }
        val i0 = start + asin(gap / ri)
        val o0 = start + asin(gap / ro)
        val o1 = end - asin(gap / ro)
        val i1 = end - asin(gap / ri)
        val pI0 = p(ri, i0)
        val pO0 = p(ro, o0)
        val pO1 = p(ro, o1)
        val pI1 = p(ri, i1)
        return Path().apply {
            fun lineTo(o: Offset) = lineTo(o.x, o.y)
            fun quad(control: Offset, to: Offset) = quadraticTo(control.x, control.y, to.x, to.y)
            fun arc(radius: Double, a0: Double, a1: Double) {
                val segments = 28
                for (i in 1..segments) lineTo(p(radius, a0 + (a1 - a0) * i / segments))
            }
            val first = toward(pI0, pO0, innerCorner)
            moveTo(first.x, first.y)
            lineTo(toward(pO0, pI0, outerCorner))
            quad(pO0, p(ro, o0 + outerCorner / ro))
            arc(ro, o0 + outerCorner / ro, o1 - outerCorner / ro)
            quad(pO1, toward(pO1, pI1, outerCorner))
            lineTo(toward(pI1, pO1, innerCorner))
            quad(pI1, p(ri, i1 - innerCorner / ri))
            arc(ri, i1 - innerCorner / ri, i0 + innerCorner / ri)
            quad(pI0, first)
            close()
        }
    }
}
