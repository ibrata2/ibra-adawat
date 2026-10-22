import AppKit

/// Zip packages whose first entry must be an uncompressed `mimetype` (EPUB, ODF), or plain zipped folders.
@MainActor
enum ZipPackage {
    static func zip(_ folder: URL, to out: URL, storedFirst: String? = nil) async throws {
        let zip = URL(fileURLWithPath: "/usr/bin/zip")
        try? FileManager.default.removeItem(at: out)
        if let storedFirst {
            try await ProcessRunner.run(zip, ["-X", "-0", "-q", out.path, storedFirst], currentDirectory: folder)
        }
        let items = try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0 != storedFirst && $0 != ".DS_Store" }
        try await ProcessRunner.run(zip, ["-X", "-r", "-q", "-9", out.path] + items, currentDirectory: folder)
    }

    static func unzip(_ url: URL) async throws -> URL {
        let folder = Output.temporaryURL(extension: "").deletingPathExtension()
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try await ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/ditto"), ["-x", "-k", url.path, folder.path])
        return folder
    }

    static func escape(_ text: String) -> String {
        text.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }
}

// MARK: - Markdown

enum Markdown {
    /// Full CommonMark: headings, lists, quotes, code blocks, emphasis and links, as styled text.
    static func attributed(_ source: String) -> NSAttributedString {
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .full,
                                                              failurePolicy: .returnPartiallyParsedIfPossible)
        guard let parsed = try? AttributedString(markdown: source, options: options) else {
            return NSAttributedString(string: source)
        }
        let result = NSMutableAttributedString()
        var lastBlock: Int?
        for run in parsed.runs {
            let components = run.presentationIntent?.components ?? []
            let block = components.first?.identity
            if block != lastBlock {
                if result.length > 0 { result.append(NSAttributedString(string: "\n")) }
                // The list marker goes in front of the item's first run.
                if let item = components.first(where: { if case .listItem = $0.kind { return true }; return false }),
                   case .listItem(let ordinal) = item.kind {
                    let ordered = components.contains { if case .orderedList = $0.kind { return true }; return false }
                    result.append(NSAttributedString(string: ordered ? "\(ordinal). " : "•  ", attributes: [.font: NSFont.systemFont(ofSize: 13)]))
                }
            }
            lastBlock = block
            var size: CGFloat = 13
            var bold = false, italic = false, monospaced = false, quote = false
            for component in components {
                switch component.kind {
                case .header(let level): size = [26, 21, 17, 15, 14, 13][min(max(level, 1), 6) - 1]; bold = true
                case .codeBlock: monospaced = true
                case .blockQuote: quote = true
                default: break
                }
            }
            if let inline = run.inlinePresentationIntent {
                if inline.contains(.stronglyEmphasized) { bold = true }
                if inline.contains(.emphasized) { italic = true }
                if inline.contains(.code) { monospaced = true }
            }
            var font = monospaced ? NSFont.monospacedSystemFont(ofSize: size - 1, weight: bold ? .bold : .regular)
                : NSFont.systemFont(ofSize: size, weight: bold ? .bold : .regular)
            if italic { font = NSFontManager.shared.convert(font, toHaveTrait: .italicFontMask) }
            var attributes: [NSAttributedString.Key: Any] = [.font: font]
            if quote { attributes[.foregroundColor] = NSColor.secondaryLabelColor }
            if let link = run.link { attributes[.link] = link }
            result.append(NSAttributedString(string: String(parsed[run.range].characters), attributes: attributes))
        }
        ArabicText.applyParagraphDirection(result)
        return result
    }

    /// Markdown from styled text: large bold paragraphs become headings, bold and italic runs keep their markers.
    static func string(from text: NSAttributedString) -> String {
        let string = text.string as NSString
        var lines: [String] = []
        string.enumerateSubstrings(in: NSRange(location: 0, length: string.length), options: .byParagraphs) { _, range, _, _ in
            guard range.length > 0 else { lines.append(""); return }
            let font = text.attribute(.font, at: range.location, effectiveRange: nil) as? NSFont
            let size = font?.pointSize ?? 13
            let prefix = size >= 22 ? "# " : size >= 18 ? "## " : size >= 15.5 ? "### " : ""
            var line = ""
            text.enumerateAttributes(in: range) { attributes, runRange, _ in
                var run = string.substring(with: runRange)
                guard !run.trimmingCharacters(in: .whitespaces).isEmpty else { line += run; return }
                let traits = (attributes[.font] as? NSFont)?.fontDescriptor.symbolicTraits ?? []
                if prefix.isEmpty, traits.contains(.bold) { run = "**\(run)**" }
                if traits.contains(.italic) { run = "*\(run)*" }
                if let link = attributes[.link] { run = "[\(run)](\((link as? URL)?.absoluteString ?? "\(link)"))" }
                line += run
            }
            for bullet in ["•  ", "•\t", "• ", "◦\t", "–\t"] where line.hasPrefix(bullet) {
                line = "- " + line.dropFirst(bullet.count)
                break
            }
            lines.append(prefix + line)
        }
        // Consecutive list items stay together; every other block gets a blank line.
        var output = ""
        for (index, line) in lines.enumerated() where !line.isEmpty {
            let isItem = line.hasPrefix("- ") || line.range(of: #"^\d+\. "#, options: .regularExpression) != nil
            let previous = lines[..<index].last { !$0.isEmpty }
            let previousIsItem = previous.map { $0.hasPrefix("- ") || $0.range(of: #"^\d+\. "#, options: .regularExpression) != nil } ?? false
            if !output.isEmpty { output += isItem && previousIsItem ? "\n" : "\n\n" }
            output += line
        }
        return output + "\n"
    }
}

// MARK: - EPUB

@MainActor
enum EPUB {
    /// The book's chapters in reading order, joined into one styled text.
    static func read(_ url: URL) async throws -> NSAttributedString {
        let root = try await ZipPackage.unzip(url)
        defer { try? FileManager.default.removeItem(at: root) }
        guard let container = try? Data(contentsOf: root.appendingPathComponent("META-INF/container.xml")),
              let opfPath = XMLScan.attributes(of: "rootfile", in: container).first?["full-path"],
              let opf = try? Data(contentsOf: root.appendingPathComponent(opfPath)) else {
            throw AdawatError.message("The EPUB does not identify a readable package document.")
        }
        let base = root.appendingPathComponent(opfPath).deletingLastPathComponent()
        var hrefs: [String: String] = [:]
        for item in XMLScan.attributes(of: "item", in: opf) {
            if let id = item["id"], let href = item["href"] { hrefs[id] = href.removingPercentEncoding ?? href }
        }
        let spine = XMLScan.attributes(of: "itemref", in: opf).compactMap { $0["idref"].flatMap { hrefs[$0] } }
        guard !spine.isEmpty else { throw AdawatError.message("The EPUB has no readable chapters.") }
        let book = NSMutableAttributedString()
        for href in spine {
            let chapter = base.appendingPathComponent(href)
            guard let text = try? NSAttributedString(url: chapter, options: [.documentType: NSAttributedString.DocumentType.html,
                                                                              .characterEncoding: String.Encoding.utf8.rawValue],
                                                     documentAttributes: nil) else { continue }
            if book.length > 0 { book.append(NSAttributedString(string: "\n\n")) }
            book.append(text)
        }
        guard book.length > 0 else { throw AdawatError.message("The EPUB has no readable chapters.") }
        return book
    }

    /// An EPUB 3 book: chapters start at each top-level heading, with a table of contents and the right reading direction.
    static func write(_ text: NSAttributedString, title: String, to out: URL) async throws {
        let fm = FileManager.default
        let root = Output.temporaryURL(extension: "").deletingPathExtension()
        defer { try? fm.removeItem(at: root) }
        try fm.createDirectory(at: root.appendingPathComponent("META-INF"), withIntermediateDirectories: true)
        try fm.createDirectory(at: root.appendingPathComponent("OEBPS"), withIntermediateDirectories: true)
        let arabic = ArabicText.containsArabic(text.string)
        let language = arabic ? "ar" : "en"
        let chapters = split(text)
        for (index, chapter) in chapters.enumerated() {
            let page = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="\(language)" lang="\(language)" dir="\(arabic ? "rtl" : "ltr")">
            <head><meta charset="UTF-8"/><title>\(ZipPackage.escape(chapter.title))</title><link rel="stylesheet" href="style.css"/></head>
            <body>\(chapter.body)</body></html>
            """
            try page.write(to: root.appendingPathComponent("OEBPS/chapter\(index + 1).xhtml"), atomically: true, encoding: .utf8)
        }
        let manifest = chapters.indices.map { "<item id=\"c\($0 + 1)\" href=\"chapter\($0 + 1).xhtml\" media-type=\"application/xhtml+xml\"/>" }.joined()
        let spine = chapters.indices.map { "<itemref idref=\"c\($0 + 1)\"/>" }.joined()
        let toc = chapters.enumerated().map { "<li><a href=\"chapter\($0.offset + 1).xhtml\">\(ZipPackage.escape($0.element.title))</a></li>" }.joined()
        let modified = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: "\\.\\d+", with: "", options: .regularExpression)
        let files: [String: String] = [
            "mimetype": "application/epub+zip",
            "META-INF/container.xml": """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>
            """,
            "OEBPS/content.opf": """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id" xml:lang="\(language)" dir="\(arabic ? "rtl" : "ltr")">
            <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:uuid:\(UUID().uuidString)</dc:identifier><dc:title>\(ZipPackage.escape(title))</dc:title><dc:language>\(language)</dc:language><meta property="dcterms:modified">\(modified)</meta></metadata>
            <manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="css" href="style.css" media-type="text/css"/>\(manifest)</manifest>
            <spine page-progression-direction="\(arabic ? "rtl" : "ltr")">\(spine)</spine></package>
            """,
            "OEBPS/nav.xhtml": """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="\(language)" dir="\(arabic ? "rtl" : "ltr")"><head><meta charset="UTF-8"/><title>\(ZipPackage.escape(title))</title></head>
            <body><nav epub:type="toc"><h1>\(arabic ? "المحتويات" : "Contents")</h1><ol>\(toc)</ol></nav></body></html>
            """,
            "OEBPS/style.css": "body { font-family: serif; line-height: 1.6; } h1, h2, h3 { font-family: sans-serif; } p { margin: 0 0 0.8em; }",
        ]
        for (path, content) in files { try content.write(to: root.appendingPathComponent(path), atomically: true, encoding: .utf8) }
        try await ZipPackage.zip(root, to: out, storedFirst: "mimetype")
    }

    private struct Chapter { var title: String; var body: String }

    /// XHTML paragraphs from styled text, cut into chapters at large headings.
    private static func split(_ text: NSAttributedString) -> [Chapter] {
        let string = text.string as NSString
        var chapters: [Chapter] = []
        var current = Chapter(title: "", body: "")
        string.enumerateSubstrings(in: NSRange(location: 0, length: string.length), options: .byParagraphs) { paragraph, range, _, _ in
            guard let paragraph, !paragraph.trimmingCharacters(in: .whitespaces).isEmpty else { return }
            let size = (text.attribute(.font, at: range.location, effectiveRange: nil) as? NSFont)?.pointSize ?? 13
            let level = size >= 22 ? 1 : size >= 17 ? 2 : size >= 15 ? 3 : 0
            if level == 1, !current.body.isEmpty {
                chapters.append(current)
                current = Chapter(title: "", body: "")
            }
            var inline = ""
            text.enumerateAttributes(in: range) { attributes, runRange, _ in
                var run = ZipPackage.escape(string.substring(with: runRange))
                let traits = (attributes[.font] as? NSFont)?.fontDescriptor.symbolicTraits ?? []
                if level == 0, traits.contains(.bold) { run = "<strong>\(run)</strong>" }
                if traits.contains(.italic) { run = "<em>\(run)</em>" }
                if let link = attributes[.link] {
                    run = "<a href=\"\(ZipPackage.escape((link as? URL)?.absoluteString ?? "\(link)"))\">\(run)</a>"
                }
                inline += run
            }
            let dir = ArabicText.containsArabic(paragraph) ? " dir=\"rtl\"" : ""
            current.body += level > 0 ? "<h\(level)\(dir)>\(inline)</h\(level)>\n" : "<p\(dir)>\(inline)</p>\n"
            if current.title.isEmpty || level == 1 { current.title = String(paragraph.prefix(80)) }
        }
        if !current.body.isEmpty { chapters.append(current) }
        return chapters.isEmpty ? [Chapter(title: L10n.t("Untitled"), body: "<p></p>")] : chapters
    }
}
