import Foundation
import UniformTypeIdentifiers

enum FileKind: Hashable {
    case image, pdf, video, audio, document, spreadsheet, presentation, subtitle, archive, folder, other

    init(url: URL) {
        if url.hasDirectoryPath || (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true,
           !Self.isPackageDocument(url) {
            self = .folder
            return
        }
        let ext = url.pathExtension.lowercased()
        let type = UTType(filenameExtension: ext) ?? .data
        // Checked first: some of these also conform to generic text or archive types.
        if Self.spreadsheetExtensions.contains(ext) {
            self = .spreadsheet
        } else if Self.presentationExtensions.contains(ext) {
            self = .presentation
        } else if SubtitleOps.extensions.contains(ext) {
            self = .subtitle
        } else if type.conforms(to: .pdf) {
            self = .pdf
        } else if type.conforms(to: .image) {
            self = .image
        } else if type.conforms(to: .movie) || type.conforms(to: .video) {
            self = .video
        } else if type.conforms(to: .audio) {
            self = .audio
        } else if Self.documentExtensions.contains(url.pathExtension.lowercased()) {
            self = .document
        } else if type.conforms(to: .archive) || Self.archiveExtensions.contains(url.pathExtension.lowercased()) {
            self = .archive
        } else {
            self = .other
        }
    }

    static let documentExtensions: Set<String> = [
        "txt", "rtf", "rtfd", "doc", "docx", "odt", "html", "htm", "md", "markdown", "webarchive", "epub", "pages", "textclipping",
    ]

    static let spreadsheetExtensions: Set<String> = ["xlsx", "xls", "csv", "tsv", "ods", "numbers"]
    static let presentationExtensions: Set<String> = ["pptx", "ppt", "key", "odp"]

    static let archiveExtensions: Set<String> = ["zip", "tar", "gz", "tgz", "bz2", "xz", "rar", "7z", "tbz", "txz", "iso", "cpio"]

    private static func isPackageDocument(_ url: URL) -> Bool {
        ["rtfd", "pages", "key", "numbers", "app"].contains(url.pathExtension.lowercased())
    }
}

extension Array where Element == URL {
    var kinds: Set<FileKind> { Set(map(FileKind.init(url:))) }

    /// The single kind shared by every file, if there is one.
    var commonKind: FileKind? {
        let kinds = self.kinds
        return kinds.count == 1 ? kinds.first : nil
    }
}
