import AppKit
import CoreImage
import CoreImage.CIFilterBuiltins

/// A QR code as its grid of modules.
struct QRMatrix {
    enum Correction: String, CaseIterable, Identifiable {
        case low = "L", medium = "M", quartile = "Q", high = "H"
        var id: String { rawValue }
        var title: String {
            switch self {
            case .low: return "Low · 7%"
            case .medium: return "Medium · 15%"
            case .quartile: return "Quartile · 25%"
            case .high: return "High · 30%"
            }
        }
    }

    let size: Int
    private let dark: [Bool]

    func isDark(_ x: Int, _ y: Int) -> Bool {
        x >= 0 && y >= 0 && x < size && y < size && dark[y * size + x]
    }

    init(size: Int, dark: [Bool]) {
        self.size = size
        self.dark = dark
    }

    /// Encodes `text`, or nil when it does not fit a QR code at this correction level.
    init?(_ text: String, correction: Correction) {
        guard !text.isEmpty else { return nil }
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(text.utf8)
        filter.correctionLevel = correction.rawValue
        guard let image = filter.outputImage,
              let cg = CIContext().createCGImage(image, from: image.extent) else { return nil }
        // Core Image draws one pixel per module with its own white border; read the pixels and drop that border.
        let width = cg.width, height = cg.height
        var pixels = [UInt8](repeating: 255, count: width * height)
        guard let ctx = CGContext(data: &pixels, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return nil }
        ctx.draw(cg, in: CGRect(x: 0, y: 0, width: width, height: height))
        let isDark = { (x: Int, y: Int) in pixels[y * width + x] < 128 }
        var minX = width, minY = height, maxX = -1, maxY = -1
        for y in 0..<height { for x in 0..<width where isDark(x, y) {
            minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
        } }
        guard maxX >= minX, maxX - minX == maxY - minY else { return nil }
        let size = maxX - minX + 1
        var grid = [Bool](repeating: false, count: size * size)
        for y in 0..<size { for x in 0..<size { grid[y * size + x] = isDark(minX + x, minY + y) } }
        self.init(size: size, dark: grid)
    }

    /// True inside the three 7×7 corner squares (finder patterns), which are drawn as whole shapes.
    func isFinder(_ x: Int, _ y: Int) -> Bool {
        // Real codes are at least 21 modules; smaller grids are the pickers' samples, which have no corners.
        guard size >= 21 else { return false }
        let far = size - 7
        return (x < 7 && y < 7) || (x >= far && y < 7) || (x < 7 && y >= far)
    }

    /// Top-left corners of the three finder patterns.
    var finderOrigins: [CGPoint] {
        [CGPoint(x: 0, y: 0), CGPoint(x: size - 7, y: 0), CGPoint(x: 0, y: size - 7)]
    }
}

/// How a QR code looks. Everything is laid out in module units with the origin at the top left,
/// so the same paths draw the PNG and become the SVG.
struct QRDesign {
    enum ModuleShape: String, CaseIterable, Identifiable {
        case square, rounded, dots, diamond, fluid, bars
        var id: String { rawValue }
        var title: String {
            switch self {
            case .square: return "Squares"
            case .rounded: return "Rounded"
            case .dots: return "Dots"
            case .diamond: return "Diamonds"
            case .fluid: return "Fluid"
            case .bars: return "Bars"
            }
        }
    }

    enum EyeShape: String, CaseIterable, Identifiable {
        case square, rounded, circle, leaf
        var id: String { rawValue }
        var title: String {
            switch self {
            case .square: return "Square"
            case .rounded: return "Rounded"
            case .circle: return "Circle"
            case .leaf: return "Leaf"
            }
        }
    }

    enum Plate: String, CaseIterable, Identifiable {
        case none, square, rounded, circle
        var id: String { rawValue }
        var title: String {
            switch self {
            case .none: return "None"
            case .square: return "Square"
            case .rounded: return "Rounded"
            case .circle: return "Circle"
            }
        }
    }

    var module = ModuleShape.square
    var eye = EyeShape.square
    var foreground = NSColor.black
    /// The second color of a diagonal gradient across the code, or nil for one color.
    var gradientEnd: NSColor?
    var eyeColor = NSColor.black
    var background: NSColor? = .white
    var margin = 4
    var logo: CGImage?
    /// The logo's width as a share of the code's width.
    var logoScale = 0.22
    var plate = Plate.rounded

    // MARK: Layout

    /// The square the logo sits in, plus the cleared space around it, in module units without the margin.
    func logoBox(_ matrix: QRMatrix) -> (logo: CGRect, clear: CGRect)? {
        guard logo != nil else { return nil }
        let side = (Double(matrix.size) * logoScale).rounded()
        let origin = (Double(matrix.size) - side) / 2
        let logo = CGRect(x: origin, y: origin, width: side, height: side)
        return (logo, logo.insetBy(dx: plate == .none ? -0.4 : -0.9, dy: plate == .none ? -0.4 : -0.9))
    }

    /// Every data module, in the chosen shape.
    func modulePath(_ matrix: QRMatrix) -> CGPath {
        let path = CGMutablePath()
        let clear = logoBox(matrix)?.clear
        func keep(_ x: Int, _ y: Int) -> Bool {
            guard matrix.isDark(x, y), !matrix.isFinder(x, y) else { return false }
            if let clear, clear.intersects(CGRect(x: x, y: y, width: 1, height: 1).insetBy(dx: 0.2, dy: 0.2)) { return false }
            return true
        }
        for y in 0..<matrix.size { for x in 0..<matrix.size where keep(x, y) {
            let cell = CGRect(x: x + margin, y: y + margin, width: 1, height: 1)
            switch module {
            case .square:
                path.addRect(cell.insetBy(dx: -0.01, dy: -0.01))
            case .rounded:
                path.addRoundedRect(cell.insetBy(dx: 0.06, dy: 0.06), radii: (0.28, 0.28, 0.28, 0.28))
            case .dots:
                path.addEllipse(in: cell.insetBy(dx: 0.07, dy: 0.07))
            case .diamond:
                path.addLines(between: [CGPoint(x: cell.midX, y: cell.minY + 0.02), CGPoint(x: cell.maxX - 0.02, y: cell.midY),
                                        CGPoint(x: cell.midX, y: cell.maxY - 0.02), CGPoint(x: cell.minX + 0.02, y: cell.midY)])
                path.closeSubpath()
            case .fluid:
                // Neighbors join flat; a corner is round only where both of its sides are open.
                let up = keep(x, y - 1), down = keep(x, y + 1), left = keep(x - 1, y), right = keep(x + 1, y)
                let r = 0.5
                path.addRoundedRect(cell.insetBy(dx: -0.005, dy: -0.005),
                                    radii: (!up && !left ? r : 0, !up && !right ? r : 0, !down && !right ? r : 0, !down && !left ? r : 0))
            case .bars:
                let up = keep(x, y - 1), down = keep(x, y + 1)
                let r = 0.4
                path.addRoundedRect(CGRect(x: cell.minX + 0.1, y: cell.minY - (up ? 0.005 : 0), width: 0.8,
                                           height: 1 + (up ? 0.005 : 0) + (down ? 0.005 : 0)),
                                    radii: (up ? 0 : r, up ? 0 : r, down ? 0 : r, down ? 0 : r))
            }
        } }
        return path
    }

    /// The three corner squares: each a ring with a ball inside, filled even-odd.
    func eyePath(_ matrix: QRMatrix) -> CGPath {
        let path = CGMutablePath()
        for origin in matrix.finderOrigins {
            let outer = CGRect(x: origin.x + CGFloat(margin), y: origin.y + CGFloat(margin), width: 7, height: 7)
            Self.addEye(eye, outer: outer, to: path)
        }
        return path
    }

    static func addEye(_ eye: EyeShape, outer: CGRect, to path: CGMutablePath) {
        let hole = outer.insetBy(dx: 1, dy: 1), ball = outer.insetBy(dx: 2, dy: 2)
        switch eye {
        case .square:
            path.addRect(outer); path.addRect(hole); path.addRect(ball)
        case .rounded:
            path.addRoundedRect(outer, radii: (2, 2, 2, 2))
            path.addRoundedRect(hole, radii: (1.3, 1.3, 1.3, 1.3))
            path.addRoundedRect(ball, radii: (0.9, 0.9, 0.9, 0.9))
        case .circle:
            path.addEllipse(in: outer); path.addEllipse(in: hole); path.addEllipse(in: ball)
        case .leaf:
            path.addRoundedRect(outer, radii: (3, 0.4, 3, 0.4))
            path.addRoundedRect(hole, radii: (2.2, 0.2, 2.2, 0.2))
            path.addRoundedRect(ball, radii: (1.4, 0, 1.4, 0))
        }
    }

    /// The plate behind the logo, if it has one.
    func platePath(_ matrix: QRMatrix) -> CGPath? {
        guard let box = logoBox(matrix), plate != .none else { return nil }
        let rect = box.clear.offsetBy(dx: CGFloat(margin), dy: CGFloat(margin)).insetBy(dx: 0.3, dy: 0.3)
        let path = CGMutablePath()
        switch plate {
        case .none: return nil
        case .square: path.addRect(rect)
        case .rounded: path.addRoundedRect(rect, radii: (rect.width * 0.2, rect.width * 0.2, rect.width * 0.2, rect.width * 0.2))
        case .circle: path.addEllipse(in: rect)
        }
        return path
    }

    /// Where the logo image itself goes: fitted inside its box, keeping its shape.
    func logoRect(_ matrix: QRMatrix) -> CGRect? {
        guard let logo, let box = logoBox(matrix)?.logo else { return nil }
        var rect = box.offsetBy(dx: CGFloat(margin), dy: CGFloat(margin))
        if plate == .circle { rect = rect.insetBy(dx: rect.width * 0.12, dy: rect.height * 0.12) }
        let aspect = CGFloat(logo.width) / CGFloat(max(logo.height, 1))
        let size = aspect >= 1 ? CGSize(width: rect.width, height: rect.width / aspect) : CGSize(width: rect.height * aspect, height: rect.height)
        return CGRect(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2, width: size.width, height: size.height)
    }

    var plateColor: NSColor { background ?? .white }

    // MARK: Drawing

    /// The code as a square bitmap `pixels` wide.
    func image(_ matrix: QRMatrix, pixels: Int) -> CGImage? {
        let total = CGFloat(matrix.size + margin * 2)
        guard let ctx = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let scale = CGFloat(pixels) / total
        // Flip to module units with y pointing down, matching the SVG.
        ctx.translateBy(x: 0, y: CGFloat(pixels))
        ctx.scaleBy(x: scale, y: -scale)
        ctx.interpolationQuality = .high
        if let background {
            ctx.setFillColor(background.cgColor)
            ctx.fill(CGRect(x: 0, y: 0, width: total, height: total))
        }
        let antialias = module != .square || eye != .square
        ctx.setShouldAntialias(antialias)
        fill(modulePath(matrix), in: ctx, total: total, color: foreground, evenOdd: false)
        fill(eyePath(matrix), in: ctx, total: total, color: eyeColor, evenOdd: true)
        ctx.setShouldAntialias(true)
        if let plate = platePath(matrix) {
            ctx.addPath(plate)
            ctx.setFillColor(plateColor.cgColor)
            ctx.fillPath()
        }
        if let logo, let rect = logoRect(matrix) {
            ctx.saveGState()
            // Images draw bottom-up; flip locally so the logo is upright.
            ctx.translateBy(x: rect.minX, y: rect.maxY)
            ctx.scaleBy(x: 1, y: -1)
            ctx.draw(logo, in: CGRect(origin: .zero, size: rect.size))
            ctx.restoreGState()
        }
        return ctx.makeImage()
    }

    /// Fills with one color, or with the diagonal gradient when there is one (the eyes keep their own color).
    private func fill(_ path: CGPath, in ctx: CGContext, total: CGFloat, color: NSColor, evenOdd: Bool) {
        ctx.saveGState()
        ctx.addPath(path)
        if let end = gradientEnd, color == foreground,
           let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: [color.cgColor, end.cgColor] as CFArray,
                                     locations: [0, 1]) {
            ctx.clip(using: evenOdd ? .evenOdd : .winding)
            ctx.drawLinearGradient(gradient, start: .zero, end: CGPoint(x: total, y: total), options: [])
        } else {
            ctx.setFillColor(color.cgColor)
            ctx.fillPath(using: evenOdd ? .evenOdd : .winding)
        }
        ctx.restoreGState()
    }

    /// The code as scalable SVG, one unit per module, with the logo embedded.
    func svg(_ matrix: QRMatrix) -> String {
        let total = matrix.size + margin * 2
        var defs = ""
        var codeFill = Self.hex(foreground)
        if let end = gradientEnd {
            defs = "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"\(total)\" y2=\"\(total)\" gradientUnits=\"userSpaceOnUse\">"
                + "<stop offset=\"0\" stop-color=\"\(Self.hex(foreground))\"/><stop offset=\"1\" stop-color=\"\(Self.hex(end))\"/></linearGradient></defs>"
            codeFill = "url(#g)"
        }
        let eyeFill = eyeColor == foreground ? codeFill : Self.hex(eyeColor)
        var body = background.map { "<rect width=\"\(total)\" height=\"\(total)\" fill=\"\(Self.hex($0))\"/>" } ?? ""
        body += "<path fill=\"\(codeFill)\" d=\"\(modulePath(matrix).svgData)\"/>"
        body += "<path fill=\"\(eyeFill)\" fill-rule=\"evenodd\" d=\"\(eyePath(matrix).svgData)\"/>"
        if let plate = platePath(matrix) {
            body += "<path fill=\"\(Self.hex(plateColor))\" d=\"\(plate.svgData)\"/>"
        }
        if let logo, let rect = logoRect(matrix),
           let png = NSBitmapImageRep(cgImage: logo).representation(using: .png, properties: [:]) {
            body += "<image x=\"\(Self.number(rect.minX))\" y=\"\(Self.number(rect.minY))\" width=\"\(Self.number(rect.width))\" "
                + "height=\"\(Self.number(rect.height))\" href=\"data:image/png;base64,\(png.base64EncodedString())\"/>"
        }
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 \(total) \(total)\">\(defs)\(body)</svg>\n"
    }

    static func hex(_ color: NSColor) -> String {
        let c = color.usingColorSpace(.sRGB) ?? .black
        return String(format: "#%02X%02X%02X", Int((c.redComponent * 255).rounded()), Int((c.greenComponent * 255).rounded()),
                      Int((c.blueComponent * 255).rounded()))
    }

    static func number(_ value: CGFloat) -> String {
        var text = String(format: "%.3f", Double(value))
        while text.hasSuffix("0") { text.removeLast() }
        if text.hasSuffix(".") { text.removeLast() }
        return text == "-0" ? "0" : text
    }
}

extension CGMutablePath {
    /// A rectangle with its own radius at each corner: top left, top right, bottom right, bottom left (y down).
    func addRoundedRect(_ r: CGRect, radii: (CGFloat, CGFloat, CGFloat, CGFloat)) {
        let limit = min(r.width, r.height) / 2
        let (tl, tr, br, bl) = (min(radii.0, limit), min(radii.1, limit), min(radii.2, limit), min(radii.3, limit))
        move(to: CGPoint(x: r.minX + tl, y: r.minY))
        addLine(to: CGPoint(x: r.maxX - tr, y: r.minY))
        if tr > 0 { addArc(tangent1End: CGPoint(x: r.maxX, y: r.minY), tangent2End: CGPoint(x: r.maxX, y: r.minY + tr), radius: tr) }
        addLine(to: CGPoint(x: r.maxX, y: r.maxY - br))
        if br > 0 { addArc(tangent1End: CGPoint(x: r.maxX, y: r.maxY), tangent2End: CGPoint(x: r.maxX - br, y: r.maxY), radius: br) }
        addLine(to: CGPoint(x: r.minX + bl, y: r.maxY))
        if bl > 0 { addArc(tangent1End: CGPoint(x: r.minX, y: r.maxY), tangent2End: CGPoint(x: r.minX, y: r.maxY - bl), radius: bl) }
        addLine(to: CGPoint(x: r.minX, y: r.minY + tl))
        if tl > 0 { addArc(tangent1End: CGPoint(x: r.minX, y: r.minY), tangent2End: CGPoint(x: r.minX + tl, y: r.minY), radius: tl) }
        closeSubpath()
    }
}

extension CGPath {
    /// SVG path data for this path.
    var svgData: String {
        var d = ""
        let n = QRDesign.number
        applyWithBlock { element in
            let p = element.pointee.points
            switch element.pointee.type {
            case .moveToPoint: d += "M\(n(p[0].x)) \(n(p[0].y))"
            case .addLineToPoint: d += "L\(n(p[0].x)) \(n(p[0].y))"
            case .addQuadCurveToPoint: d += "Q\(n(p[0].x)) \(n(p[0].y)) \(n(p[1].x)) \(n(p[1].y))"
            case .addCurveToPoint: d += "C\(n(p[0].x)) \(n(p[0].y)) \(n(p[1].x)) \(n(p[1].y)) \(n(p[2].x)) \(n(p[2].y))"
            case .closeSubpath: d += "Z"
            @unknown default: break
            }
        }
        return d
    }
}

/// What a QR code holds, and how files become that text.
enum QRContent {
    static let fileExtensions: Set<String> = ["txt", "md", "markdown", "webloc", "url", "vcf"]

    static func canEncode(_ files: [URL]) -> Bool {
        files.count == 1 && fileExtensions.contains(files[0].pathExtension.lowercased())
    }

    /// The text inside a text file, a Safari link (.webloc), a Windows link (.url) or a contact card.
    static func text(from file: URL) -> String? {
        switch file.pathExtension.lowercased() {
        case "webloc":
            guard let data = try? Data(contentsOf: file),
                  let plist = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any] else { return nil }
            return plist["URL"] as? String
        case "url":
            guard let text = try? String(contentsOf: file, encoding: .utf8) else { return nil }
            return text.split(whereSeparator: \.isNewline).first { $0.hasPrefix("URL=") }.map { String($0.dropFirst(4)) }
        default:
            guard let text = try? String(contentsOf: file, encoding: .utf8) else { return nil }
            return text.trimmingCharacters(in: .whitespacesAndNewlines)
        }
    }

    enum Security: String, CaseIterable, Identifiable {
        case wpa = "WPA", wep = "WEP", none = "nopass"
        var id: String { rawValue }
        var title: String { self == .none ? "None" : self == .wpa ? "WPA / WPA2 / WPA3" : "WEP" }
    }

    /// The text phones read as "join this network".
    static func wifi(name: String, password: String, security: Security, hidden: Bool) -> String {
        func escape(_ value: String) -> String {
            value.reduce(into: "") { result, character in
                if "\\;,:\"".contains(character) { result.append("\\") }
                result.append(character)
            }
        }
        var text = "WIFI:T:\(security.rawValue);S:\(escape(name));"
        if security != .none { text += "P:\(escape(password));" }
        if hidden { text += "H:true;" }
        return text + ";"
    }
}
