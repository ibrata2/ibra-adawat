import Foundation
import SwiftUI
import UniformTypeIdentifiers
import Photos

enum Kind: String { case video, audio, image, pdf, other }

extension URL {
    var kind: Kind {
        guard let type = UTType(filenameExtension: pathExtension.lowercased()) else { return .other }
        if type.conforms(to: .movie) || type.conforms(to: .video) { return .video }
        if type.conforms(to: .audio) { return .audio }
        if type.conforms(to: .image) { return .image }
        if type.conforms(to: .pdf) { return .pdf }
        return .other
    }
}

/// Where results go: Documents/IBMC, which the Files app shows under "On My iPhone › IBMC".
enum Outputs {
    static var folder: URL {
        let url = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("IBMC", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// A free file name in the results folder: "name suffix.ext", then "name suffix 2.ext"…
    static func path(for source: URL, ext: String, suffix: String) -> URL {
        var stem = source.deletingPathExtension().lastPathComponent
        // Picked files carry a short random prefix ("A1B2C3-"); results get the original name back.
        if stem.count > 7, stem[stem.index(stem.startIndex, offsetBy: 6)] == "-" { stem = String(stem.dropFirst(7)) }
        let base = suffix.isEmpty ? stem : "\(stem) \(suffix)"
        var candidate = folder.appendingPathComponent("\(base).\(ext)")
        var n = 2
        while FileManager.default.fileExists(atPath: candidate.path) {
            candidate = folder.appendingPathComponent("\(base) \(n).\(ext)")
            n += 1
        }
        return candidate
    }

    static var inbox: URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("Inbox", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// Copies a picked or shared file into the app's own temporary folder, so it can be read at any time.
    static func stage(_ url: URL) -> URL? {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        let target = inbox.appendingPathComponent(String(UUID().uuidString.prefix(6)) + "-" + url.lastPathComponent)
        do {
            try FileManager.default.copyItem(at: url, to: target)
            return target
        } catch { return nil }
    }
}

struct OpError: LocalizedError {
    let message: String
    init(_ english: String, _ arabic: String) { message = L.t(english, arabic) }
    var errorDescription: String? { message }
}

/// One task: a tool run on some files, with its progress and results.
@MainActor
final class Job: ObservableObject, Identifiable {
    enum State { case running, done, failed }
    let id = UUID()
    let title: String
    let files: [URL]
    @Published var state = State.running
    @Published var progress: Double = 0
    @Published var message = ""
    @Published var outputs: [URL] = []

    init(title: String, files: [URL]) {
        self.title = title
        self.files = files
    }
}

/// Reports progress and a closing message from the work, which runs off the main thread.
final class Report: @unchecked Sendable {
    private let job: Job
    var finish: String?
    init(job: Job) { self.job = job }
    func progress(_ value: Double) { Task { @MainActor in self.job.progress = min(max(value, 0), 1) } }
    func status(_ text: String) { Task { @MainActor in self.job.message = text } }
}

@MainActor
final class Store: ObservableObject {
    @Published var inputs: [URL] = []
    @Published var jobs: [Job] = []
    @Published var section: Kind = .video

    func receive(_ urls: [URL]) {
        let staged = urls.compactMap(Outputs.stage)
        add(staged)
    }

    func add(_ urls: [URL]) {
        for url in urls where !inputs.contains(url) { inputs.append(url) }
        if let first = urls.first, [.video, .audio, .image, .pdf].contains(first.kind) { section = first.kind }
    }

    func run(_ tool: Tool, on files: [URL]) {
        let job = Job(title: tool.title, files: files)
        jobs.insert(job, at: 0)
        let report = Report(job: job)
        Task.detached(priority: .userInitiated) {
            do {
                let outputs = try await tool.run(files, report)
                await MainActor.run {
                    job.outputs = outputs
                    job.progress = 1
                    job.state = .done
                    job.message = report.finish ?? (outputs.count == 1 ? outputs[0].lastPathComponent
                                                    : L.t("Saved \(outputs.count) files", "انحفظت \(outputs.count) ملفات"))
                }
            } catch {
                await MainActor.run {
                    job.state = .failed
                    job.message = error.localizedDescription
                }
            }
        }
    }

    /// Saves pictures and videos to the photo library.
    static func saveToPhotos(_ urls: [URL]) async -> Bool {
        let status = await PHPhotoLibrary.requestAuthorization(for: .addOnly)
        guard status == .authorized || status == .limited else { return false }
        do {
            try await PHPhotoLibrary.shared().performChanges {
                for url in urls {
                    if url.kind == .image { PHAssetCreationRequest.creationRequestForAssetFromImage(atFileURL: url) }
                    else if url.kind == .video { PHAssetCreationRequest.creationRequestForAssetFromVideo(atFileURL: url) }
                }
            }
            return true
        } catch { return false }
    }
}
