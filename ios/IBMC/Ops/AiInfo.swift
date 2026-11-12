import Foundation
import ImageIO
import AVFoundation
import UniformTypeIdentifiers

/// Finds and removes the information inside a file that says it was made with AI: C2PA Content Credentials, XMP/IPTC
/// "digital source type", software tags, and the names of generators in tags. Watermarks inside the pixels or the sound are not touched.
enum AiInfo {
    private static let signs: [(String, String)] = [
        ("c2pa", "C2PA"), ("jumb", "C2PA"), ("contentauth", "C2PA"),
        ("trainedAlgorithmicMedia", "IPTC: AI"), ("compositeWithTrainedAlgorithmicMedia", "IPTC: AI edit"), ("algorithmicMedia", "IPTC: AI"),
        ("Made with AI", "Made with AI"), ("AI generated", "AI generated"), ("Firefly", "Adobe Firefly"), ("Generative Fill", "Generative Fill"),
        ("DALL-E", "DALL·E"), ("OpenAI", "OpenAI"), ("ChatGPT", "ChatGPT"), ("Midjourney", "Midjourney"), ("Stable Diffusion", "Stable Diffusion"),
        ("ComfyUI", "ComfyUI"), ("NovelAI", "NovelAI"), ("Imagen", "Imagen"), ("Gemini", "Gemini"), ("Ideogram", "Ideogram"), ("Leonardo", "Leonardo"),
        ("Suno", "Suno"), ("Udio", "Udio"), ("ElevenLabs", "ElevenLabs"), ("Sora", "Sora"), ("Veo", "Veo"), ("Runway", "Runway"), ("Pika", "Pika"),
        ("Kling", "Kling"), ("Hailuo", "Hailuo"), ("Luma", "Luma"), ("HeyGen", "HeyGen"), ("Synthesia", "Synthesia"), ("Grok", "Grok"),
    ]

    /// Labels of every sign found; names count only as whole words.
    static func match(_ text: String) -> [String] {
        var found: [String] = []
        let lower = text.lowercased()
        for (needle, label) in signs where !found.contains(label) {
            var range = lower.startIndex..<lower.endIndex
            while let hit = lower.range(of: needle.lowercased(), range: range) {
                let before = hit.lowerBound == lower.startIndex || !lower[lower.index(before: hit.lowerBound)].isLetter
                let after = hit.upperBound == lower.endIndex || !lower[hit.upperBound].isLetter
                if before && after { found.append(label); break }
                range = hit.upperBound..<lower.endIndex
            }
        }
        return found
    }

    /// The metadata of a file as text, plus a look for a C2PA box near its start and end.
    static func scan(_ url: URL) async -> [String] {
        var text = ""
        if url.kind == .image, let source = CGImageSourceCreateWithURL(url as CFURL, nil) {
            if let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) { text += "\(properties)" }
            if let metadata = CGImageSourceCopyMetadataAtIndex(source, 0, nil),
               let xmp = CGImageMetadataCreateXMPData(metadata, nil) as Data? { text += String(decoding: xmp, as: UTF8.self) }
        } else if url.kind == .video || url.kind == .audio {
            let asset = AVURLAsset(url: url)
            var items = (try? await asset.load(.metadata)) ?? []
            for format in (try? await asset.load(.availableMetadataFormats)) ?? [] { items += (try? await asset.loadMetadata(for: format)) ?? [] }
            for item in items {
                if let value = try? await item.load(.stringValue) { text += " \(value)" }
                if let key = item.identifier?.rawValue { text += " \(key)" }
            }
        }
        if let handle = try? FileHandle(forReadingFrom: url) {
            defer { try? handle.close() }
            let head = (try? handle.read(upToCount: 1 << 20)) ?? Data()
            text += " " + String(decoding: head.filter { $0 >= 32 && $0 < 127 }, as: UTF8.self)
                .components(separatedBy: CharacterSet.alphanumerics.inverted).filter { ["c2pa", "jumb", "jumd", "contentauth"].contains($0.lowercased()) }.joined(separator: " ")
        }
        return match(text)
    }

    static func check(_ files: [URL], report: Report) async throws -> [URL] {
        var marked = 0
        var first: [String] = []
        for (index, file) in files.enumerated() {
            report.progress(Double(index) / Double(files.count))
            let found = await scan(file)
            if index == 0 { first = found }
            if !found.isEmpty { marked += 1 }
        }
        report.finish = marked == 0
            ? L.t("No AI information found", "ما لكيت معلومات ذكاء اصطناعي")
            : files.count == 1 ? L.t("AI information found: ", "لكيت معلومات ذكاء اصطناعي: ") + first.prefix(3).joined(separator: ", ")
            : L.t("AI information found in \(marked) of \(files.count) files", "لكيت معلومات ذكاء اصطناعي بـ \(marked) من \(files.count) ملفات")
        return []
    }

    static func remove(_ files: [URL], report: Report) async throws -> [URL] {
        var outputs: [URL] = []
        var had = 0, left = 0
        for (index, file) in files.enumerated() {
            report.status(L.t("Removing AI information \(index + 1) of \(files.count)", "إزالة معلومات الذكاء الاصطناعي \(index + 1) من \(files.count)"))
            if !(await scan(file)).isEmpty { had += 1 }
            let base = Double(index) / Double(files.count)
            let output: URL
            if file.kind == .image {
                let ext = file.pathExtension.lowercased()
                let (type, outExt): (UTType, String) = ext == "png" ? (.png, "png") : ext == "heic" ? (.heic, "heic") : (.jpeg, "jpg")
                output = try ImageOps.write(try ImageOps.upright(file), to: Outputs.path(for: file, ext: outExt, suffix: "clean"), type: type, quality: 0.97)
            } else {
                output = try await MediaOps.stripped(file) { report.progress(base + $0 / Double(files.count)) }
            }
            outputs.append(output)
            if !(await scan(output)).isEmpty { left += 1 }
        }
        report.finish = left > 0 ? L.t("Some information could not be removed", "بعض المعلومات ما انشالت")
            : had == 0 ? L.t("No AI information was found; saved a clean copy", "ما چانت بيها معلومات ذكاء اصطناعي؛ انحفظت نسخة نظيفة")
            : L.t("AI information removed", "انشالت معلومات الذكاء الاصطناعي")
        return outputs
    }
}
