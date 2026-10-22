// Draws the README banner in light and dark: the app icon, the name in English and Arabic, and the tagline.
// The Arabic text uses a Google Font (OFL), passed as two .ttf files: one bold for the name, one for the tagline.
// Run: swift scripts/make-banner.swift Resources/AppIcon-1024.png docs/media Tajawal-ExtraBold.ttf Tajawal-Medium.ttf
import AppKit

let icon = NSImage(contentsOfFile: CommandLine.arguments[1])!
let folder = URL(fileURLWithPath: CommandLine.arguments[2])
/// Registers a font file for this process and returns its PostScript name.
func register(_ path: String) -> String? {
    let url = URL(fileURLWithPath: path) as CFURL
    CTFontManagerRegisterFontsForURL(url, .process, nil)
    guard let provider = CGDataProvider(url: url), let font = CGFont(provider) else { return nil }
    return font.postScriptName as String?
}
let arabicBold = CommandLine.arguments.count > 3 ? register(CommandLine.arguments[3]) : nil
let arabicText = CommandLine.arguments.count > 4 ? register(CommandLine.arguments[4]) : nil
let size = NSSize(width: 1600, height: 520)

func banner(dark: Bool) -> Data {
    let image = NSImage(size: size, flipped: false) { rect in
        let background = dark ? NSColor(srgbRed: 0.09, green: 0.08, blue: 0.16, alpha: 1) : NSColor(srgbRed: 0.96, green: 0.95, blue: 1, alpha: 1)
        let card = NSBezierPath(roundedRect: rect.insetBy(dx: 2, dy: 2), xRadius: 40, yRadius: 40)
        background.setFill()
        card.fill()
        // A soft violet glow behind the icon.
        let glow = NSGradient(colors: [NSColor(srgbRed: 0.36, green: 0.28, blue: 0.9, alpha: dark ? 0.45 : 0.22), .clear])!
        glow.draw(fromCenter: NSPoint(x: 330, y: 260), radius: 0, toCenter: NSPoint(x: 330, y: 260), radius: 330, options: [])
        icon.draw(in: NSRect(x: 150, y: 80, width: 360, height: 360))

        let ink = dark ? NSColor.white : NSColor(srgbRed: 0.10, green: 0.09, blue: 0.18, alpha: 1)
        let muted = dark ? NSColor(white: 1, alpha: 0.66) : NSColor(srgbRed: 0.33, green: 0.32, blue: 0.42, alpha: 1)
        let accent = dark ? NSColor(srgbRed: 0.49, green: 0.95, blue: 0.78, alpha: 1) : NSColor(srgbRed: 0.36, green: 0.28, blue: 0.9, alpha: 1)
        func draw(_ text: String, _ font: NSFont, _ color: NSColor, at point: NSPoint, rtl: Bool = false) {
            let style = NSMutableParagraphStyle()
            style.baseWritingDirection = rtl ? .rightToLeft : .leftToRight
            NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color, .paragraphStyle: style]).draw(at: point)
        }
        let rounded = { (size: CGFloat, weight: NSFont.Weight) -> NSFont in
            let base = NSFont.systemFont(ofSize: size, weight: weight)
            return NSFont(descriptor: base.fontDescriptor.withDesign(.rounded) ?? base.fontDescriptor, size: size) ?? base
        }
        let name = NSAttributedString(string: "Adawat", attributes: [.font: rounded(132, .bold), .foregroundColor: ink])
        name.draw(at: NSPoint(x: 590, y: 270))
        let arabic = NSAttributedString(string: "أدوات", attributes: [.font: NSFont(name: arabicBold ?? "GeezaPro-Bold", size: 104) ?? rounded(104, .bold),
                                                                      .foregroundColor: accent])
        arabic.draw(at: NSPoint(x: 590 + name.size().width + 44, y: 284))
        draw("Convert, compress and edit any file while you drag it.", rounded(40, .medium), muted, at: NSPoint(x: 596, y: 200))
        draw("حوّل واضغط وعدّل أي ملف وأنت تسحبه.", NSFont(name: arabicText ?? "GeezaPro", size: 40) ?? rounded(40, .regular), muted,
             at: NSPoint(x: 596, y: 134), rtl: true)
        draw("macOS 14+  ·  Free and open source  ·  Everything stays on your Mac", rounded(26, .regular), accent,
             at: NSPoint(x: 598, y: 66))
        return true
    }
    let rep = NSBitmapImageRep(cgImage: image.cgImage(forProposedRect: nil, context: nil, hints: nil)!)
    return rep.representation(using: .png, properties: [:])!
}

try! banner(dark: false).write(to: folder.appendingPathComponent("banner-light.png"))
try! banner(dark: true).write(to: folder.appendingPathComponent("banner-dark.png"))
print("banners written")
