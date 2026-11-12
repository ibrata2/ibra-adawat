import Foundation
import AVFoundation
import UniformTypeIdentifiers

/// One tile: what it is called, which files it takes, and the work it does.
struct Tool: Identifiable {
    let id: String
    let english: String
    let arabic: String
    let symbol: String
    var badge: String? = nil
    var kinds: [Kind]
    var minFiles = 1
    /// All files go into one result (merge, PDF from pictures); otherwise each file gets its own.
    var together = false
    let run: @Sendable ([URL], Report) async throws -> [URL]

    var title: String { L.t(english, arabic) }

    func fits(_ url: URL) -> Bool { kinds.contains(url.kind) }
}

struct ToolSection: Identifiable {
    let kind: Kind
    let english: String
    let arabic: String
    let symbol: String
    let tools: [Tool]
    var id: String { kind.rawValue }
    var title: String { L.t(english, arabic) }
}

/// Runs a one-file job over every file, reporting progress across all of them.
private func each(_ files: [URL], _ report: Report, _ work: @escaping @Sendable (URL, @escaping @Sendable (Double) -> Void) async throws -> URL) async throws -> [URL] {
    var outputs: [URL] = []
    for (index, file) in files.enumerated() {
        report.status(L.t("Working on \(index + 1) of \(files.count)", "جاري العمل على \(index + 1) من \(files.count)"))
        let base = Double(index) / Double(files.count)
        outputs.append(try await work(file) { report.progress(base + $0 / Double(files.count)) })
    }
    return outputs
}

enum Catalog {
    static let sections: [ToolSection] = [
        ToolSection(kind: .video, english: "Video", arabic: "الفيديو", symbol: "film", tools: [
            Tool(id: "v.mp4", english: "To MP4", arabic: "إلى MP4", symbol: "doc", badge: "MP4", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.convert($0, to: .mp4, ext: "mp4", progress: $1) } },
            Tool(id: "v.mov", english: "To MOV", arabic: "إلى MOV", symbol: "doc", badge: "MOV", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.convert($0, to: .mov, ext: "mov", progress: $1) } },
            Tool(id: "v.m4a", english: "Extract Audio", arabic: "استخراج الصوت", symbol: "waveform", badge: "M4A", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.extractAudio($0, progress: $1) } },
            Tool(id: "v.1080", english: "Compress 1080p", arabic: "ضغط 1080p", symbol: "arrow.down.right.and.arrow.up.left", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.compress($0, preset: AVAssetExportPreset1920x1080, tag: "1080p", progress: $1) } },
            Tool(id: "v.720", english: "Compress 720p", arabic: "ضغط 720p", symbol: "arrow.down.right.and.arrow.up.left", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.compress($0, preset: AVAssetExportPreset1280x720, tag: "720p", progress: $1) } },
            Tool(id: "v.whatsapp", english: "WhatsApp Size", arabic: "حجم واتساب", symbol: "message", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.compress($0, preset: AVAssetExportPreset960x540, tag: "whatsapp", progress: $1) } },
            Tool(id: "v.mute", english: "Mute", arabic: "كتم الصوت", symbol: "speaker.slash", kinds: [.video]) { f, r in
                try await each(f, r) { try await MediaOps.mute($0, progress: $1) } },
            Tool(id: "v.speech.ar", english: "Speech to Text (Arabic)", arabic: "الكلام إلى نص (عربي)", symbol: "captions.bubble", kinds: [.video]) { f, r in
                try await SpeechOps.transcribe(f, locale: "ar-SA", report: r) },
            Tool(id: "v.speech.en", english: "Speech to Text (English)", arabic: "الكلام إلى نص (إنجليزي)", symbol: "captions.bubble", kinds: [.video]) { f, r in
                try await SpeechOps.transcribe(f, locale: "en-US", report: r) },
            Tool(id: "v.noai", english: "Remove AI Info", arabic: "إزالة معلومات الذكاء الاصطناعي", symbol: "eye.slash", kinds: [.video]) { f, r in
                try await AiInfo.remove(f, report: r) },
            Tool(id: "v.aicheck", english: "Check for AI Info", arabic: "فحص معلومات الذكاء الاصطناعي", symbol: "magnifyingglass", kinds: [.video]) { f, r in
                try await AiInfo.check(f, report: r) },
        ]),
        ToolSection(kind: .audio, english: "Audio", arabic: "الصوت", symbol: "music.note", tools: [
            Tool(id: "a.m4a", english: "To M4A", arabic: "إلى M4A", symbol: "doc", badge: "M4A", kinds: [.audio, .video]) { f, r in
                try await each(f, r) { try await MediaOps.extractAudio($0, progress: $1) } },
            Tool(id: "a.wav", english: "To WAV", arabic: "إلى WAV", symbol: "doc", badge: "WAV", kinds: [.audio, .video]) { f, r in
                try await each(f, r) { try await MediaOps.toWav($0, progress: $1) } },
            Tool(id: "a.speech.ar", english: "Speech to Text (Arabic)", arabic: "الكلام إلى نص (عربي)", symbol: "captions.bubble", kinds: [.audio]) { f, r in
                try await SpeechOps.transcribe(f, locale: "ar-SA", report: r) },
            Tool(id: "a.speech.en", english: "Speech to Text (English)", arabic: "الكلام إلى نص (إنجليزي)", symbol: "captions.bubble", kinds: [.audio]) { f, r in
                try await SpeechOps.transcribe(f, locale: "en-US", report: r) },
            Tool(id: "a.noai", english: "Remove AI Info", arabic: "إزالة معلومات الذكاء الاصطناعي", symbol: "eye.slash", kinds: [.audio]) { f, r in
                try await AiInfo.remove(f, report: r) },
            Tool(id: "a.aicheck", english: "Check for AI Info", arabic: "فحص معلومات الذكاء الاصطناعي", symbol: "magnifyingglass", kinds: [.audio]) { f, r in
                try await AiInfo.check(f, report: r) },
        ]),
        ToolSection(kind: .image, english: "Image", arabic: "الصورة", symbol: "photo", tools: [
            Tool(id: "i.jpg", english: "To JPG", arabic: "إلى JPG", symbol: "doc", badge: "JPG", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.convert(url, to: .jpeg, ext: "jpg") } },
            Tool(id: "i.png", english: "To PNG", arabic: "إلى PNG", symbol: "doc", badge: "PNG", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.convert(url, to: .png, ext: "png") } },
            Tool(id: "i.heic", english: "To HEIC", arabic: "إلى HEIC", symbol: "doc", badge: "HEIC", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.convert(url, to: .heic, ext: "heic") } },
            Tool(id: "i.pdf", english: "To PDF", arabic: "إلى PDF", symbol: "doc.richtext", badge: "PDF", kinds: [.image], together: true) { f, _ in
                [try PdfOps.fromImages(f)] },
            Tool(id: "i.compress", english: "Compress", arabic: "ضغط الصور", symbol: "arrow.down.right.and.arrow.up.left", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.compress(url) } },
            Tool(id: "i.nobg", english: "Remove Background", arabic: "إزالة الخلفية", symbol: "person.crop.rectangle", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.removeBackground(url) } },
            Tool(id: "i.story", english: "Story 9:16", arabic: "ستوري 9:16", symbol: "rectangle.portrait", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.social(url, size: CGSize(width: 1080, height: 1920), tag: "story") } },
            Tool(id: "i.post", english: "Post 4:5", arabic: "بوست 4:5", symbol: "rectangle", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.social(url, size: CGSize(width: 1080, height: 1350), tag: "post") } },
            Tool(id: "i.square", english: "Square 1:1", arabic: "مربع 1:1", symbol: "square", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.social(url, size: CGSize(width: 1080, height: 1080), tag: "square") } },
            Tool(id: "i.ocr", english: "Copy Text", arabic: "نسخ النص", symbol: "text.viewfinder", kinds: [.image]) { f, r in
                try await each(f, r) { url, _ in try ImageOps.recognizeText(url) } },
            Tool(id: "i.noai", english: "Remove AI Info", arabic: "إزالة معلومات الذكاء الاصطناعي", symbol: "eye.slash", kinds: [.image]) { f, r in
                try await AiInfo.remove(f, report: r) },
            Tool(id: "i.aicheck", english: "Check for AI Info", arabic: "فحص معلومات الذكاء الاصطناعي", symbol: "magnifyingglass", kinds: [.image]) { f, r in
                try await AiInfo.check(f, report: r) },
        ]),
        ToolSection(kind: .pdf, english: "PDF", arabic: "PDF", symbol: "doc.richtext", tools: [
            Tool(id: "p.merge", english: "Merge PDFs", arabic: "دمج PDF", symbol: "square.stack", kinds: [.pdf], minFiles: 2, together: true) { f, _ in
                [try PdfOps.merge(f)] },
            Tool(id: "p.png", english: "Pages to PNG", arabic: "الصفحات إلى صور", symbol: "photo.on.rectangle", badge: "PNG", kinds: [.pdf]) { f, r in
                var all: [URL] = []
                for file in f { all += try PdfOps.pagesToImages(file) }
                return all },
            Tool(id: "p.txt", english: "To Text", arabic: "إلى نص", symbol: "doc.plaintext", badge: "TXT", kinds: [.pdf]) { f, r in
                try await each(f, r) { url, _ in try PdfOps.text(url) } },
        ]),
    ]
}
