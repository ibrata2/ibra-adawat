// Frames a window snapshot for the README: rounded corners, a hairline edge and a soft shadow on a
// transparent canvas, so it sits well on GitHub's light and dark themes.
// Run: swift scripts/frame-shot.swift <in.png> <out.png> [max-width]
import AppKit

let input = URL(fileURLWithPath: CommandLine.arguments[1])
let output = URL(fileURLWithPath: CommandLine.arguments[2])
let maxWidth = CommandLine.arguments.count > 3 ? CGFloat(Double(CommandLine.arguments[3]) ?? 1400) : 1400
guard let source = CGImageSourceCreateWithURL(input as CFURL, nil), let shot = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
    fatalError("cannot read \(input.path)")
}
let scale = min(1, maxWidth / CGFloat(shot.width))
let size = CGSize(width: CGFloat(shot.width) * scale, height: CGFloat(shot.height) * scale)
let margin: CGFloat = 48 * max(scale, 0.5), radius: CGFloat = 22 * max(scale, 0.5)
let canvas = CGSize(width: size.width + margin * 2, height: size.height + margin * 2)
let ctx = CGContext(data: nil, width: Int(canvas.width), height: Int(canvas.height), bitsPerComponent: 8, bytesPerRow: 0,
                    space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
let frame = CGRect(x: margin, y: margin, width: size.width, height: size.height)
let shape = CGPath(roundedRect: frame, cornerWidth: radius, cornerHeight: radius, transform: nil)
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -10 * scale), blur: 36 * max(scale, 0.5), color: CGColor(gray: 0, alpha: 0.28))
ctx.addPath(shape)
ctx.setFillColor(.white)
ctx.fillPath()
ctx.restoreGState()
ctx.saveGState()
ctx.addPath(shape)
ctx.clip()
ctx.interpolationQuality = .high
ctx.draw(shot, in: frame)
ctx.restoreGState()
ctx.addPath(shape)
ctx.setStrokeColor(CGColor(gray: 0, alpha: 0.12))
ctx.setLineWidth(1)
ctx.strokePath()
let dest = CGImageDestinationCreateWithURL(output as CFURL, "public.png" as CFString, 1, nil)!
CGImageDestinationAddImage(dest, ctx.makeImage()!, nil)
CGImageDestinationFinalize(dest)
print(output.path)
