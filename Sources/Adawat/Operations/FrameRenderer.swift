import AppKit
import CoreImage

struct RGB: Hashable {
    var red, green, blue: Double

    var cgColor: CGColor { CGColor(srgbRed: red, green: green, blue: blue, alpha: 1) }

    init(_ red: Double, _ green: Double, _ blue: Double) {
        self.red = red
        self.green = green
        self.blue = blue
    }

    init(_ color: CGColor) {
        let srgb = color.converted(to: CGColorSpace(name: CGColorSpace.sRGB)!, intent: .defaultIntent, options: nil)
        let c = srgb?.components ?? [1, 1, 1]
        self.init(Double(c[0]), Double(c.count > 2 ? c[1] : c[0]), Double(c.count > 2 ? c[2] : c[0]))
    }
}

enum FrameBackground: Hashable {
    case gradient(Int)
    case solid(RGB)
    case image(URL)
    /// The photo itself, enlarged and blurred behind it.
    case blurred

    /// Two-stop diagonal gradients, warm to cool.
    static let gradients: [[RGB]] = [
        [RGB(0.96, 0.45, 0.33), RGB(0.98, 0.78, 0.33)],
        [RGB(0.98, 0.62, 0.55), RGB(0.6, 0.45, 0.92)],
        [RGB(1, 0.84, 0.4), RGB(0.95, 0.42, 0.62)],
        [RGB(0.98, 0.85, 0.88), RGB(0.82, 0.8, 0.98)],
        [RGB(0.8, 0.62, 0.92), RGB(0.98, 0.72, 0.8)],
        [RGB(0.13, 0.33, 0.42), RGB(0.95, 0.6, 0.36)],
        [RGB(0.35, 0.65, 0.98), RGB(0.6, 0.9, 0.95)],
        [RGB(0.45, 0.85, 0.82), RGB(0.25, 0.5, 0.92)],
        [RGB(0.92, 0.94, 1), RGB(0.68, 0.74, 0.98)],
    ]

    static let solids: [RGB] = [
        RGB(1, 1, 1), RGB(0.93, 0.78, 0.38), RGB(0.68, 0.8, 0.36), RGB(0.38, 0.62, 0.44), RGB(0.36, 0.6, 0.6),
        RGB(0.5, 0.78, 0.9), RGB(0.27, 0.4, 0.82), RGB(0.16, 0.2, 0.3), RGB(0.42, 0.3, 0.68),
    ]
}

enum FrameRatio: String, CaseIterable, Identifiable {
    case auto = "Auto", square = "1:1", wide = "16:9", portrait = "4:5", story = "9:16"

    var id: String { rawValue }

    var value: CGFloat? {
        switch self {
        case .auto: return nil
        case .square: return 1
        case .wide: return 16.0 / 9
        case .portrait: return 4.0 / 5
        case .story: return 9.0 / 16
        }
    }
}

struct FrameOptions: Equatable {
    var background: FrameBackground = .gradient(0)
    /// In output pixels.
    var padding: Double = 160
    var corners: Double = 24
    var shadow: Double = 24
    var ratio: FrameRatio = .auto
}

/// Places a photo on a new canvas: background, padding, rounded corners and a soft shadow.
enum FrameRenderer {
    /// `scale` shrinks every pixel measure for previews (1 for the saved photo).
    static func render(_ photo: CGImage, options: FrameOptions, scale: CGFloat = 1, original: CGImage? = nil) throws -> CGImage {
        let padding = CGFloat(options.padding) * scale
        var canvas = CGSize(width: CGFloat(photo.width) + padding * 2, height: CGFloat(photo.height) + padding * 2)
        if let ratio = options.ratio.value {
            if canvas.width / canvas.height < ratio { canvas.width = canvas.height * ratio } else { canvas.height = canvas.width / ratio }
        }
        canvas = CGSize(width: canvas.width.rounded(), height: canvas.height.rounded())
        guard canvas.width * canvas.height < 400_000_000 else {
            throw AdawatError.message("This frame would be too large. Use less padding or a closer output ratio.")
        }
        let space = CGColorSpace(name: CGColorSpace.displayP3)!
        guard let ctx = CGContext(data: nil, width: Int(canvas.width), height: Int(canvas.height), bitsPerComponent: 8,
                                  bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
            throw AdawatError.message("Could not prepare the image background.")
        }
        let bounds = CGRect(origin: .zero, size: canvas)
        try drawBackground(options.background, in: ctx, bounds: bounds, photo: original ?? photo)

        let photoRect = CGRect(x: (canvas.width - CGFloat(photo.width)) / 2, y: (canvas.height - CGFloat(photo.height)) / 2,
                               width: CGFloat(photo.width), height: CGFloat(photo.height))
        let radius = min(CGFloat(options.corners) * scale, min(photoRect.width, photoRect.height) / 2)
        let rounded = CGPath(roundedRect: photoRect, cornerWidth: radius, cornerHeight: radius, transform: nil)
        if options.shadow > 0 {
            let blur = CGFloat(options.shadow) * scale
            ctx.saveGState()
            ctx.setShadow(offset: CGSize(width: 0, height: -blur * 0.35), blur: blur, color: CGColor(gray: 0, alpha: 0.4))
            ctx.addPath(rounded)
            ctx.setFillColor(CGColor(gray: 0, alpha: 1))
            ctx.fillPath()
            ctx.restoreGState()
        }
        ctx.saveGState()
        ctx.addPath(rounded)
        ctx.clip()
        ctx.draw(photo, in: photoRect)
        ctx.restoreGState()
        guard let image = ctx.makeImage() else { throw AdawatError.message("Could not finish adding the background.") }
        return image
    }

    private static func drawBackground(_ background: FrameBackground, in ctx: CGContext, bounds: CGRect, photo: CGImage) throws {
        switch background {
        case .solid(let color):
            ctx.setFillColor(color.cgColor)
            ctx.fill(bounds)
        case .gradient(let index):
            let stops = FrameBackground.gradients[index % FrameBackground.gradients.count]
            let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                                      colors: stops.map(\.cgColor) as CFArray, locations: [0, 1])!
            ctx.drawLinearGradient(gradient, start: CGPoint(x: bounds.minX, y: bounds.maxY),
                                   end: CGPoint(x: bounds.maxX, y: bounds.minY), options: [])
        case .image(let url):
            guard let image = try? ImageFile(url).upright(maxPixels: Int(max(bounds.width, bounds.height))) else {
                throw AdawatError.message("Choose a background image.")
            }
            ctx.draw(image, in: aspectFill(image, in: bounds))
        case .blurred:
            let base = CIImage(cgImage: photo)
            let radius = max(bounds.width, bounds.height) * 0.04
            let blurred = base.clampedToExtent()
                .applyingGaussianBlur(sigma: radius)
                .applyingFilter("CIColorControls", parameters: [kCIInputBrightnessKey: -0.06, kCIInputSaturationKey: 1.15])
                .cropped(to: base.extent)
            if let image = ImageEngine.ciContext.createCGImage(blurred, from: base.extent) {
                ctx.draw(image, in: aspectFill(image, in: bounds))
            }
        }
    }

    private static func aspectFill(_ image: CGImage, in bounds: CGRect) -> CGRect {
        let scale = max(bounds.width / CGFloat(image.width), bounds.height / CGFloat(image.height))
        let size = CGSize(width: CGFloat(image.width) * scale, height: CGFloat(image.height) * scale)
        return CGRect(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2, width: size.width, height: size.height)
    }
}
