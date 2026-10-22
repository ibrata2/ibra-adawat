// Draws Adawat's app icon: a white wheel of six tool petals on a violet squircle, one petal lifted in mint
// as the chosen tool. Run: swift scripts/make-icon.swift <out.png>
import AppKit

let size: CGFloat = 1024
let out = URL(fileURLWithPath: CommandLine.arguments[1])
let space = CGColorSpace(name: CGColorSpace.displayP3)!
let ctx = CGContext(data: nil, width: Int(size), height: Int(size), bitsPerComponent: 8, bytesPerRow: 0, space: space,
                    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!

// macOS icon grid: the shape sits inside an 824 pt square with a soft shadow below.
let inset: CGFloat = 100
let body = CGRect(x: inset, y: inset + 6, width: size - inset * 2, height: size - inset * 2)
let squircle = CGPath(roundedRect: body, cornerWidth: 186, cornerHeight: 186, transform: nil)
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -14), blur: 34, color: CGColor(gray: 0, alpha: 0.32))
ctx.addPath(squircle)
ctx.setFillColor(CGColor(srgbRed: 0.36, green: 0.28, blue: 0.9, alpha: 1))
ctx.fillPath()
ctx.restoreGState()

ctx.saveGState()
ctx.addPath(squircle)
ctx.clip()
let gradient = CGGradient(colorsSpace: space, colors: [
    CGColor(srgbRed: 0.50, green: 0.44, blue: 1.00, alpha: 1),
    CGColor(srgbRed: 0.36, green: 0.28, blue: 0.90, alpha: 1),
    CGColor(srgbRed: 0.23, green: 0.16, blue: 0.66, alpha: 1),
] as CFArray, locations: [0, 0.5, 1])!
ctx.drawLinearGradient(gradient, start: CGPoint(x: size / 2, y: body.maxY), end: CGPoint(x: size / 2, y: body.minY), options: [])
let glow = CGGradient(colorsSpace: space, colors: [CGColor(gray: 1, alpha: 0.24), CGColor(gray: 1, alpha: 0)] as CFArray, locations: [0, 1])!
ctx.drawRadialGradient(glow, startCenter: CGPoint(x: body.minX + 200, y: body.maxY - 160), startRadius: 0,
                       endCenter: CGPoint(x: body.minX + 200, y: body.maxY - 160), endRadius: 620, options: [])
ctx.restoreGState()

// Six rounded petals around a hub; the top-right one is lifted outward like the tool under the pointer.
let center = CGPoint(x: size / 2, y: size / 2 + 6)
let inner: CGFloat = 104, outer: CGFloat = 300, gap: CGFloat = 22, count = 6
let lifted = 0
func petal(_ index: Int, lift: CGFloat) -> CGPath {
    let step = 2 * Double.pi / Double(count)
    let mid = Double.pi / 2 - step / 2 - Double(index) * step
    let c = CGPoint(x: center.x + cos(mid) * lift, y: center.y + sin(mid) * lift)
    let a0 = mid - step / 2, a1 = mid + step / 2
    let i0 = a0 + asin(gap / inner), i1 = a1 - asin(gap / inner), o0 = a0 + asin(gap / outer), o1 = a1 - asin(gap / outer)
    func p(_ r: CGFloat, _ a: Double) -> CGPoint { CGPoint(x: c.x + r * cos(a), y: c.y + r * sin(a)) }
    let path = CGMutablePath()
    path.move(to: p(inner, i0))
    path.addLine(to: p(outer, o0))
    path.addArc(center: c, radius: outer, startAngle: o0, endAngle: o1, clockwise: false)
    path.addLine(to: p(inner, i1))
    path.addArc(center: c, radius: inner, startAngle: i1, endAngle: i0, clockwise: true)
    path.closeSubpath()
    return path
}
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -10), blur: 22, color: CGColor(srgbRed: 0.1, green: 0.05, blue: 0.4, alpha: 0.45))
for index in 0..<count {
    let isLifted = index == lifted
    ctx.setAlpha(isLifted ? 1 : 0.95)
    ctx.beginTransparencyLayer(auxiliaryInfo: nil)
    ctx.setFillColor(isLifted ? CGColor(srgbRed: 0.49, green: 0.95, blue: 0.78, alpha: 1) : CGColor(gray: 1, alpha: 1))
    // Rounded corners: fill the shape, then a round-joined stroke of it, as two passes in one layer.
    let shape = petal(index, lift: isLifted ? 26 : 0)
    ctx.addPath(shape)
    ctx.fillPath()
    ctx.addPath(shape.copy(strokingWithWidth: 30, lineCap: .round, lineJoin: .round, miterLimit: 1))
    ctx.fillPath()
    ctx.endTransparencyLayer()
}
ctx.setAlpha(1)
ctx.restoreGState()
// Hub
ctx.setFillColor(CGColor(gray: 1, alpha: 0.97))
ctx.fillEllipse(in: CGRect(x: center.x - 54, y: center.y - 54, width: 108, height: 108))
ctx.setFillColor(CGColor(srgbRed: 0.36, green: 0.28, blue: 0.9, alpha: 1))
ctx.fillEllipse(in: CGRect(x: center.x - 20, y: center.y - 20, width: 40, height: 40))

let image = ctx.makeImage()!
let dest = CGImageDestinationCreateWithURL(out as CFURL, "public.png" as CFString, 1, nil)!
CGImageDestinationAddImage(dest, image, nil)
CGImageDestinationFinalize(dest)
print(out.path)
