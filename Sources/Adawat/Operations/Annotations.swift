import AppKit
import CoreText

enum AnnotationTool: String, CaseIterable, Identifiable {
    case arrow, highlight, text, rectangle, ellipse, draw, callout, magnifier, image

    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .arrow: return "arrow.up.right"
        case .highlight: return "highlighter"
        case .text: return "textformat"
        case .rectangle: return "rectangle"
        case .ellipse: return "circle"
        case .draw: return "pencil.tip"
        case .callout: return "1.circle"
        case .magnifier: return "plus.magnifyingglass"
        case .image: return "photo.badge.plus"
        }
    }

    var title: String {
        switch self {
        case .arrow: return "Arrow"
        case .highlight: return "Highlight"
        case .text: return "Text"
        case .rectangle: return "Rectangle"
        case .ellipse: return "Ellipse"
        case .draw: return "Draw"
        case .callout: return "Numbered Callout"
        case .magnifier: return "Magnifier"
        case .image: return "Image"
        }
    }

    var hint: String {
        switch self {
        case .arrow: return "Drag from the tail toward the arrowhead. Drag an annotation to move it."
        case .highlight: return "Drag over the image to highlight."
        case .text: return "Click to add a text box, then type below. Drag a box to move it."
        case .rectangle, .ellipse: return "Drag on the image to draw. Drag an annotation to move it."
        case .draw: return "Drag to draw freehand. Undo removes the last stroke."
        case .callout: return "Click to add numbered callouts; drag to move them. Their size stays fixed."
        case .magnifier: return "Drag out from the spot to magnify. Drag a magnifier to move it."
        case .image: return "Paste an image with ⌘V or choose one. Drag it to move."
        }
    }
}

struct AnnotationColor: Hashable {
    let red, green, blue: Double

    var cgColor: CGColor { CGColor(srgbRed: red, green: green, blue: blue, alpha: 1) }

    static let palette: [AnnotationColor] = [
        AnnotationColor(red: 0.93, green: 0.26, blue: 0.21),
        AnnotationColor(red: 1, green: 0.32, blue: 0),
        AnnotationColor(red: 1, green: 0.8, blue: 0.1),
        AnnotationColor(red: 0.2, green: 0.72, blue: 0.35),
        AnnotationColor(red: 0.16, green: 0.47, blue: 0.96),
        AnnotationColor(red: 1, green: 1, blue: 1),
        AnnotationColor(red: 0.08, green: 0.08, blue: 0.08),
    ]
}

/// A mark on the picture. Positions are normalized (0…1) with a top-left origin, so the same marks
/// draw on the small preview and on the full-size photo.
struct Annotation: Identifiable {
    enum Kind {
        case arrow(from: CGPoint, to: CGPoint)
        case highlight(CGRect)
        case rectangle(CGRect)
        case ellipse(CGRect)
        case draw([CGPoint])
        case text(origin: CGPoint, string: String)
        case callout(center: CGPoint, number: Int)
        case magnifier(center: CGPoint, radius: CGFloat)
        case image(rect: CGRect, image: CGImage)
    }

    let id = UUID()
    var kind: Kind
    var color: AnnotationColor

    /// The normalized area used for picking an annotation up.
    func bounds(aspect: CGFloat) -> CGRect {
        switch kind {
        case .arrow(let from, let to):
            return CGRect(x: min(from.x, to.x), y: min(from.y, to.y), width: abs(to.x - from.x), height: abs(to.y - from.y))
                .insetBy(dx: -0.02, dy: -0.02)
        case .highlight(let rect), .rectangle(let rect), .ellipse(let rect), .image(let rect, _):
            return rect.insetBy(dx: -0.01, dy: -0.01)
        case .draw(let points):
            let xs = points.map(\.x), ys = points.map(\.y)
            return CGRect(x: xs.min() ?? 0, y: ys.min() ?? 0, width: (xs.max() ?? 0) - (xs.min() ?? 0),
                          height: (ys.max() ?? 0) - (ys.min() ?? 0)).insetBy(dx: -0.015, dy: -0.015)
        case .text(let origin, let string):
            let width = CGFloat(max(4, string.count)) * 0.028 / max(aspect, 0.3)
            return CGRect(x: origin.x, y: origin.y, width: min(width, 1 - origin.x), height: 0.07)
        case .callout(let center, _):
            return CGRect(x: center.x - 0.035, y: center.y - 0.035 * aspect, width: 0.07, height: 0.07 * aspect)
        case .magnifier(let center, let radius):
            return CGRect(x: center.x - radius, y: center.y - radius * aspect, width: radius * 2, height: radius * 2 * aspect)
        }
    }

    func moved(by delta: CGSize) -> Annotation {
        var copy = self
        func shift(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x + delta.width, y: p.y + delta.height) }
        func shift(_ r: CGRect) -> CGRect { r.offsetBy(dx: delta.width, dy: delta.height) }
        switch kind {
        case .arrow(let from, let to): copy.kind = .arrow(from: shift(from), to: shift(to))
        case .highlight(let rect): copy.kind = .highlight(shift(rect))
        case .rectangle(let rect): copy.kind = .rectangle(shift(rect))
        case .ellipse(let rect): copy.kind = .ellipse(shift(rect))
        case .draw(let points): copy.kind = .draw(points.map(shift))
        case .text(let origin, let string): copy.kind = .text(origin: shift(origin), string: string)
        case .callout(let center, let number): copy.kind = .callout(center: shift(center), number: number)
        case .magnifier(let center, let radius): copy.kind = .magnifier(center: shift(center), radius: radius)
        case .image(let rect, let image): copy.kind = .image(rect: shift(rect), image: image)
        }
        return copy
    }
}

/// Draws annotations into a context whose origin is the top-left corner (y grows downward), sized `size`.
enum AnnotationRenderer {
    static func draw(_ annotations: [Annotation], in ctx: CGContext, size: CGSize, base: CGImage?) {
        let short = min(size.width, size.height)
        let line = max(2, short * 0.007)
        func point(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x * size.width, y: p.y * size.height) }
        func rect(_ r: CGRect) -> CGRect {
            CGRect(x: r.minX * size.width, y: r.minY * size.height, width: r.width * size.width, height: r.height * size.height)
        }

        for annotation in annotations {
            ctx.saveGState()
            ctx.setStrokeColor(annotation.color.cgColor)
            ctx.setFillColor(annotation.color.cgColor)
            ctx.setLineWidth(line)
            ctx.setLineCap(.round)
            ctx.setLineJoin(.round)
            // A soft shadow keeps marks readable on busy photos.
            ctx.setShadow(offset: CGSize(width: 0, height: line * 0.4), blur: line * 1.2,
                          color: CGColor(gray: 0, alpha: 0.35))
            switch annotation.kind {
            case .arrow(let from, let to):
                drawArrow(ctx, from: point(from), to: point(to), width: line * 1.3)
            case .highlight(let r):
                // Plain transparency, so the preview canvas and the saved photo blend the same way.
                ctx.setShadow(offset: .zero, blur: 0)
                ctx.setFillColor(annotation.color.cgColor.copy(alpha: 0.38)!)
                ctx.fill(rect(r))
            case .rectangle(let r):
                ctx.addPath(CGPath(roundedRect: rect(r), cornerWidth: line, cornerHeight: line, transform: nil))
                ctx.strokePath()
            case .ellipse(let r):
                ctx.strokeEllipse(in: rect(r))
            case .draw(let points):
                guard let first = points.first else { break }
                ctx.move(to: point(first))
                points.dropFirst().forEach { ctx.addLine(to: point($0)) }
                ctx.strokePath()
            case .text(let origin, let string):
                drawText(ctx, string, at: point(origin), size: short * 0.05, color: annotation.color, maxWidth: size.width)
            case .callout(let center, let number):
                let radius = short * 0.032
                let c = point(center)
                ctx.fillEllipse(in: CGRect(x: c.x - radius, y: c.y - radius, width: radius * 2, height: radius * 2))
                ctx.setShadow(offset: .zero, blur: 0)
                let light = annotation.color.red + annotation.color.green + annotation.color.blue > 2.4
                drawCentered(ctx, "\(number)", at: c, size: radius * 1.15,
                             color: light ? AnnotationColor.palette[6] : AnnotationColor.palette[5])
            case .magnifier(let center, let radius):
                drawMagnifier(ctx, center: point(center), radius: radius * size.width, base: base, size: size, border: line)
            case .image(let r, let image):
                drawImage(ctx, image, in: rect(r))
            }
            ctx.restoreGState()
        }
    }

    private static func drawArrow(_ ctx: CGContext, from: CGPoint, to: CGPoint, width: CGFloat) {
        let angle = atan2(to.y - from.y, to.x - from.x)
        let head = max(width * 4.5, 10)
        let base = CGPoint(x: to.x - cos(angle) * head * 0.8, y: to.y - sin(angle) * head * 0.8)
        ctx.setLineWidth(width)
        ctx.move(to: from)
        ctx.addLine(to: base)
        ctx.strokePath()
        ctx.move(to: to)
        ctx.addLine(to: CGPoint(x: to.x - cos(angle - 0.45) * head, y: to.y - sin(angle - 0.45) * head))
        ctx.addLine(to: CGPoint(x: to.x - cos(angle + 0.45) * head, y: to.y - sin(angle + 0.45) * head))
        ctx.closePath()
        ctx.fillPath()
    }

    private static func attributed(_ string: String, size: CGFloat, color: AnnotationColor) -> NSAttributedString {
        let font = NSFont.systemFont(ofSize: size, weight: .bold)
        return NSAttributedString(string: string, attributes: [
            .font: font,
            .foregroundColor: NSColor(cgColor: color.cgColor) ?? .red,
        ])
    }

    /// Core Text draws with y up, so each text block is flipped back locally.
    private static func drawText(_ ctx: CGContext, _ string: String, at origin: CGPoint, size: CGFloat,
                                 color: AnnotationColor, maxWidth: CGFloat) {
        let text = attributed(string, size: size, color: color)
        let setter = CTFramesetterCreateWithAttributedString(text)
        let width = max(size * 2, maxWidth - origin.x)
        let fit = CTFramesetterSuggestFrameSizeWithConstraints(setter, CFRange(location: 0, length: 0), nil,
                                                               CGSize(width: width, height: .greatestFiniteMagnitude), nil)
        let frame = CTFramesetterCreateFrame(setter, CFRange(location: 0, length: 0),
                                             CGPath(rect: CGRect(origin: .zero, size: fit), transform: nil), nil)
        ctx.saveGState()
        ctx.translateBy(x: origin.x, y: origin.y + fit.height)
        ctx.scaleBy(x: 1, y: -1)
        CTFrameDraw(frame, ctx)
        ctx.restoreGState()
    }

    private static func drawCentered(_ ctx: CGContext, _ string: String, at center: CGPoint, size: CGFloat, color: AnnotationColor) {
        let line = CTLineCreateWithAttributedString(attributed(string, size: size, color: color))
        let bounds = CTLineGetBoundsWithOptions(line, .useOpticalBounds)
        ctx.saveGState()
        ctx.translateBy(x: center.x - bounds.width / 2 - bounds.minX, y: center.y + bounds.height / 2 + bounds.minY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.textPosition = .zero
        CTLineDraw(line, ctx)
        ctx.restoreGState()
    }

    private static func drawMagnifier(_ ctx: CGContext, center: CGPoint, radius: CGFloat, base: CGImage?, size: CGSize, border: CGFloat) {
        let circle = CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
        ctx.fillEllipse(in: circle) // casts the shadow
        ctx.setShadow(offset: .zero, blur: 0)
        if let base {
            ctx.saveGState()
            ctx.addEllipse(in: circle)
            ctx.clip()
            // Twice the size, centered on the same spot.
            let zoom: CGFloat = 2
            let drawn = CGRect(x: center.x - center.x * zoom, y: center.y - center.y * zoom,
                               width: size.width * zoom, height: size.height * zoom)
            drawImage(ctx, base, in: drawn)
            ctx.restoreGState()
        }
        ctx.setStrokeColor(CGColor(gray: 1, alpha: 1))
        ctx.setLineWidth(border * 1.2)
        ctx.strokeEllipse(in: circle)
    }

    /// CGContext draws images bottom-up; flip locally so they appear upright in a top-left context.
    private static func drawImage(_ ctx: CGContext, _ image: CGImage, in rect: CGRect) {
        ctx.saveGState()
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(image, in: CGRect(origin: .zero, size: rect.size))
        ctx.restoreGState()
    }

    /// The full-size photo with its annotations burned in.
    static func render(_ annotations: [Annotation], on image: CGImage) -> CGImage? {
        let size = CGSize(width: image.width, height: image.height)
        let space = image.colorSpace.flatMap { $0.model == .rgb ? $0 : nil } ?? CGColorSpace(name: CGColorSpace.sRGB)!
        guard let ctx = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.draw(image, in: CGRect(origin: .zero, size: size))
        ctx.translateBy(x: 0, y: size.height)
        ctx.scaleBy(x: 1, y: -1)
        draw(annotations, in: ctx, size: size, base: image)
        return ctx.makeImage()
    }
}
