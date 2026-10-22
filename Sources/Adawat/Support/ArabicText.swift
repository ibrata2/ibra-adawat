import AppKit

/// Repairs and layout rules that keep Arabic readable through PDF and document conversions.
enum ArabicText {
    private static let arabicLetters: ClosedRange<UInt32> = 0x0600...0x06FF
    private static let presentationForms: [ClosedRange<UInt32>] = [0xFB50...0xFDFF, 0xFE70...0xFEFF]

    static func containsArabic(_ text: String) -> Bool {
        text.unicodeScalars.contains { arabicLetters.contains($0.value) || isPresentationForm($0) }
    }

    private static func isPresentationForm(_ scalar: Unicode.Scalar) -> Bool {
        presentationForms.contains { $0.contains(scalar.value) }
    }

    /// PDFs often store Arabic as presentation forms (one code point per letter shape). Text copied out
    /// of them looks right but cannot be searched, edited or shaped again, so map each back to its letter.
    static func normalizeExtracted(_ text: String) -> String {
        guard text.unicodeScalars.contains(where: isPresentationForm) else { return text }
        var scalars = String.UnicodeScalarView()
        for scalar in text.unicodeScalars {
            if isPresentationForm(scalar) {
                scalars.append(contentsOf: String(scalar).precomposedStringWithCompatibilityMapping.unicodeScalars)
            } else {
                scalars.append(scalar)
            }
        }
        return String(scalars)
    }

    /// Repairs presentation forms inside rich text while keeping each run's attributes.
    static func normalizeExtracted(_ text: NSAttributedString) -> NSMutableAttributedString {
        let fixed = NSMutableAttributedString()
        text.enumerateAttributes(in: NSRange(location: 0, length: text.length)) { attributes, range, _ in
            let run = (text.string as NSString).substring(with: range)
            fixed.append(NSAttributedString(string: normalizeExtracted(run), attributes: attributes))
        }
        return fixed
    }

    /// Paragraphs whose first letter is Arabic read right to left and align right, as Word and PDF expect.
    static func applyParagraphDirection(_ text: NSMutableAttributedString) {
        let string = text.string as NSString
        string.enumerateSubstrings(in: NSRange(location: 0, length: string.length), options: .byParagraphs) { sub, range, _, _ in
            guard let sub, let first = sub.unicodeScalars.first(where: { CharacterSet.letters.contains($0) }),
                  arabicLetters.contains(first.value) || isPresentationForm(first) else { return }
            let existing = text.attribute(.paragraphStyle, at: range.location, effectiveRange: nil) as? NSParagraphStyle
            let style = (existing?.mutableCopy() as? NSMutableParagraphStyle) ?? NSMutableParagraphStyle()
            style.baseWritingDirection = .rightToLeft
            style.alignment = .right
            text.addAttribute(.paragraphStyle, value: style, range: range)
        }
    }

    /// Sets Arabic runs in Geeza Pro before printing to PDF. The system's default Arabic font embeds in a
    /// way that comes back garbled when the PDF's text is copied or searched; Geeza Pro round-trips cleanly.
    static func withPrintableArabicFont(_ text: NSAttributedString) -> NSAttributedString {
        guard containsArabic(text.string) else { return text }
        let result = NSMutableAttributedString(attributedString: text)
        let string = text.string as NSString
        result.enumerateAttribute(.font, in: NSRange(location: 0, length: result.length)) { value, range, _ in
            guard containsArabic(string.substring(with: range)) else { return }
            let current = value as? NSFont
            let bold = current?.fontDescriptor.symbolicTraits.contains(.bold) ?? false
            if let font = NSFont(name: bold ? "GeezaPro-Bold" : "GeezaPro", size: current?.pointSize ?? 13) {
                result.addAttribute(.font, value: font, range: range)
            }
        }
        return result
    }
}
