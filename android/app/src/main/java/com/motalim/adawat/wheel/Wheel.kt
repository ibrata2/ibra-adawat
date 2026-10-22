package com.motalim.adawat.wheel

import android.graphics.BlurMaskFilter
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.spring
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.asAndroidPath
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.graphics.PathFillType
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.runtime.CompositionLocalProvider
import com.motalim.adawat.core.L10n
import com.motalim.adawat.core.Operation
import com.motalim.adawat.ui.Look
import com.motalim.adawat.ui.iconText
import kotlin.math.cos
import kotlin.math.sin

/**
 * The wheel. Tap a petal, or press anywhere on the wheel and slide to a petal and let go, as the desktop wheel works with a drag.
 * The centre label names the petal under the finger, and a tap on it switches between Convert, Tools and Actions.
 */
@Composable
fun Wheel(
    operations: List<Operation>,
    centerText: String,
    modifier: Modifier = Modifier,
    size: Dp = 300.dp,
    enabled: Boolean = true,
    initiallyHovered: Int? = null,
    onChoose: (Operation) -> Unit = {},
    onCenter: () -> Unit = {},
) {
    var hovered by remember(operations) { mutableStateOf(initiallyHovered) }
    val haptics = LocalHapticFeedback.current
    val density = LocalDensity.current
    val scale = with(density) { size.toPx() } / WheelGeometry.SIZE
    val count = operations.size
    val petals = remember(count) { List(count) { WheelGeometry.petal(it, count) } }
    val disc = remember {
        Path().apply {
            fillType = PathFillType.EvenOdd
            val c = Offset(WheelGeometry.SIZE / 2, WheelGeometry.SIZE / 2)
            addOval(androidx.compose.ui.geometry.Rect(c, WheelGeometry.OUTER_RADIUS + 6.5f))
            addOval(androidx.compose.ui.geometry.Rect(c, WheelGeometry.INNER_RADIUS - 6f))
        }
    }
    val appear = remember { Animatable(0.9f) }
    LaunchedEffect(Unit) { appear.animateTo(1f, spring(dampingRatio = 0.55f, stiffness = Spring.StiffnessMediumLow)) }
    val choose by rememberUpdatedState(onChoose)
    val center by rememberUpdatedState(onCenter)
    val ops by rememberUpdatedState(operations)

    fun setHovered(index: Int?) {
        if (index == hovered) return
        hovered = index
        if (index != null) haptics.performHapticFeedback(HapticFeedbackType.SegmentTick)
    }

    // Petals sit by angle; mirroring for Arabic would swap them with their touch zones.
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Box(
            modifier
                .size(size)
                .graphicsLayer { scaleX = appear.value; scaleY = appear.value }
                .pointerInput(enabled, count) {
                    if (!enabled) return@pointerInput
                    awaitEachGesture {
                        val down = awaitFirstDown()
                        down.consume()
                        val start = down.position / scale
                        val startedInCenter = WheelGeometry.isCenter(start)
                        setHovered(WheelGeometry.index(start, ops.size))
                        var end = start
                        while (true) {
                            val event = awaitPointerEvent()
                            val change = event.changes.firstOrNull { it.id == down.id } ?: break
                            end = change.position / scale
                            // The lift is the wheel's too, or the screen behind would take it as a tap outside.
                            change.consume()
                            if (!change.pressed) break
                            setHovered(WheelGeometry.index(end, ops.size))
                        }
                        val index = WheelGeometry.index(end, ops.size)
                        hovered = null
                        when {
                            index != null && index < ops.size -> choose(ops[index])
                            startedInCenter && WheelGeometry.isCenter(end) -> center()
                        }
                    }
                },
        ) {
            Canvas(Modifier.fillMaxSize()) {
                withTransform({ scale(scale, scale, Offset.Zero) }) {
                    drawIntoCanvas { canvas ->
                        val shadow = android.graphics.Paint().apply {
                            isAntiAlias = true
                            color = Look.Shadow.copy(alpha = 0.16f).toArgb()
                            maskFilter = BlurMaskFilter(14f, BlurMaskFilter.Blur.NORMAL)
                        }
                        canvas.nativeCanvas.save()
                        canvas.nativeCanvas.translate(0f, 5f)
                        canvas.nativeCanvas.drawPath(disc.asAndroidPath(), shadow)
                        canvas.nativeCanvas.restore()
                    }
                    drawPath(disc, Look.Disc)
                    drawPath(disc, Color(0x55FFFFFF), style = Stroke(1f))
                    petals.forEachIndexed { index, petal ->
                        val isHovered = index == hovered
                        val angle = WheelGeometry.angle(index, count)
                        val shift = if (isHovered && count > 1) Offset((cos(angle) * 5).toFloat(), (sin(angle) * 5).toFloat()) else Offset.Zero
                        withTransform({ translate(shift.x, shift.y) }) {
                            drawIntoCanvas { canvas ->
                                val shadow = android.graphics.Paint().apply {
                                    isAntiAlias = true
                                    color = (if (isHovered) Look.Accent.copy(alpha = 0.35f) else Look.Shadow.copy(alpha = 0.10f)).toArgb()
                                    maskFilter = BlurMaskFilter(if (isHovered) 11f else 8f, BlurMaskFilter.Blur.NORMAL)
                                }
                                canvas.nativeCanvas.save()
                                canvas.nativeCanvas.translate(0f, 3f)
                                canvas.nativeCanvas.drawPath(petal.asAndroidPath(), shadow)
                                canvas.nativeCanvas.restore()
                            }
                            drawPath(petal, if (isHovered) Look.Accent else Look.PetalFill)
                            drawPath(petal, if (isHovered) Color(0x703E2EAA) else Look.PetalStroke, style = Stroke(0.75f))
                        }
                    }
                }
            }
            Layout(
                content = { operations.forEachIndexed { index, op -> PetalLabel(op, index == hovered, count, scale) } },
                modifier = Modifier.fillMaxSize(),
            ) { measurables, constraints ->
                val placeables = measurables.map { it.measure(Constraints()) }
                layout(constraints.maxWidth, constraints.maxHeight) {
                    placeables.forEachIndexed { index, placeable ->
                        var position = WheelGeometry.position(index, count) * scale
                        if (index == hovered && count > 1) {
                            val angle = WheelGeometry.angle(index, count)
                            position += Offset((cos(angle) * 5 * scale).toFloat(), (sin(angle) * 5 * scale).toFloat())
                        }
                        placeable.place((position.x - placeable.width / 2).toInt(), (position.y - placeable.height / 2).toInt())
                    }
                }
            }
            val current = hovered?.let { operations.getOrNull(it) }
            val isTool = current != null && current.badge == null
            Box(
                Modifier
                    .align(Alignment.Center)
                    .sizeIn(minWidth = (70 * scale / density.density).dp, minHeight = (32 * scale / density.density).dp)
                    .shadow(4.dp, RoundedCornerShape(50), clip = false, ambientColor = Color(0x22000000), spotColor = Color(0x22000000))
                    .background(Color(0xEEFFFFFF), RoundedCornerShape(50))
                    .border(1.dp, Color(0xB3FFFFFF), RoundedCornerShape(50))
                    .padding(horizontal = 8.dp, vertical = 6.dp),
                contentAlignment = Alignment.Center,
            ) {
                val px = scale / density.density
                Text(
                    text = current?.wheelLabel ?: centerText,
                    color = Color.Black,
                    fontFamily = Look.font,
                    fontSize = with(density) { ((if (isTool || current == null) 10.5f else 13f) * px).dp.toSp() },
                    fontWeight = if (isTool || current == null) FontWeight.SemiBold else FontWeight.Medium,
                    textAlign = TextAlign.Center,
                    style = TextStyle(textDirection = labelDirection()),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.widthIn(max = (64 * px).dp),
                )
            }
        }
    }
}

private operator fun Offset.div(value: Float) = Offset(x / value, y / value)

/** The wheel itself is laid out left to right, but its words read in the app's language. */
private fun labelDirection() = if (L10n.isRightToLeft) TextDirection.Rtl else TextDirection.Ltr

@Composable
private fun PetalLabel(operation: Operation, hovered: Boolean, count: Int, scale: Float) {
    val density = LocalDensity.current
    val px = scale / density.density
    fun sp(units: Float) = with(density) { (units * px).dp.toSp() }
    val color = if (hovered) Color.White else Color.Black
    val badge = operation.badge
    if (badge != null) {
        Text(badge.uppercase(), color = color, fontSize = sp(if (count > 8) 12.5f else 14.5f), fontWeight = FontWeight.SemiBold, maxLines = 1)
        return
    }
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(iconText(operation.icon), color = color, fontFamily = Look.Icons, fontSize = sp(19f), maxLines = 1)
        Text(
            operation.wheelLabel, color = color, fontFamily = Look.font, fontWeight = FontWeight.SemiBold, fontSize = sp(9.5f),
            style = TextStyle(textDirection = labelDirection()),
            maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
            modifier = Modifier.widthIn(max = (66 * px).dp).padding(top = (1 * px).dp),
        )
    }
}
