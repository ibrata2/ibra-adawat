import Foundation

@MainActor
enum Output {
    /// The folder results for `source` are written to.
    static func directory(for source: URL) -> URL {
        let fm = FileManager.default
        switch AppSettings.shared.outputLocation {
        case .nextToOriginal:
            let parent = source.deletingLastPathComponent()
            if fm.isWritableFile(atPath: parent.path) { return parent }
            return fm.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
        case .downloads:
            return fm.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
        case .desktop:
            return fm.urls(for: .desktopDirectory, in: .userDomainMask)[0]
        }
    }

    /// A free file URL named after `source`, with a new extension and optional suffix.
    static func url(for source: URL, extension ext: String, suffix: String? = nil) -> URL {
        let base = source.deletingPathExtension().lastPathComponent
        let name = suffix.map { "\(base) \($0)" } ?? base
        return unique(directory(for: source).appendingPathComponent(name).appendingPathExtension(ext))
    }

    /// A free URL named `name` in the output folder for `source`.
    static func url(named name: String, near source: URL) -> URL {
        unique(directory(for: source).appendingPathComponent(name))
    }

    /// Appends " 2", " 3"… until the name is not taken.
    static func unique(_ url: URL) -> URL {
        let fm = FileManager.default
        guard fm.fileExists(atPath: url.path) else { return url }
        let dir = url.deletingLastPathComponent()
        let ext = url.pathExtension
        let base = url.deletingPathExtension().lastPathComponent
        var index = 2
        while true {
            var candidate = dir.appendingPathComponent("\(base) \(index)")
            if !ext.isEmpty { candidate.appendPathExtension(ext) }
            if !fm.fileExists(atPath: candidate.path) { return candidate }
            index += 1
        }
    }

    static func temporaryURL(extension ext: String) -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("adawat-\(UUID().uuidString)")
            .appendingPathExtension(ext)
    }
}
