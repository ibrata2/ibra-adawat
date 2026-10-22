import AVFoundation
import AppKit

@MainActor
enum FileActions {
    static func copyPaths(_ files: [URL], context: OperationContext) async throws -> [URL] {
        Clipboard.copy(text: files.map(\.path).joined(separator: "\n"))
        context.finish(with: files.count == 1 ? "Path copied" : "\(files.count) paths copied")
        return []
    }

    static func copyFiles(_ files: [URL], context: OperationContext) async throws -> [URL] {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.writeObjects(files as [NSURL])
        context.finish(with: files.count == 1 ? "File copied" : "\(files.count) files copied")
        return []
    }

    static func openTerminal(_ files: [URL], context: OperationContext) async throws -> [URL] {
        let folder = FileKind(url: files[0]) == .folder ? files[0] : files[0].deletingLastPathComponent()
        let terminal = URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app")
        let configuration = NSWorkspace.OpenConfiguration()
        try await NSWorkspace.shared.open([folder], withApplicationAt: terminal, configuration: configuration)
        context.finish(with: "Opened Terminal in \(folder.lastPathComponent)")
        return []
    }

    static func airDrop(_ files: [URL], context: OperationContext) async throws -> [URL] {
        guard let service = NSSharingService(named: .sendViaAirDrop), service.canPerform(withItems: files) else {
            throw AdawatError.message("AirDrop isn't available right now")
        }
        service.perform(withItems: files)
        context.finish(with: "Sharing with AirDrop")
        return []
    }

    /// Asks for a destination folder and moves the originals there.
    static func move(_ files: [URL], context: OperationContext) async throws -> [URL] {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = L10n.t("Move Here")
        panel.message = "Choose where to move \(files.count == 1 ? files[0].lastPathComponent : "\(files.count) items")"
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let destination = panel.url else {
            context.finish(with: "Move cancelled")
            return []
        }
        var moved: [URL] = []
        for file in files {
            let target = Output.unique(destination.appendingPathComponent(file.lastPathComponent))
            try FileManager.default.moveItem(at: file, to: target)
            moved.append(target)
        }
        context.finish(with: "Moved \(moved.count) item\(moved.count == 1 ? "" : "s") to \(destination.lastPathComponent)")
        return moved
    }

    static func reveal(_ files: [URL], context: OperationContext) async throws -> [URL] {
        NSWorkspace.shared.activateFileViewerSelecting(files)
        context.finish(with: "Shown in Finder")
        return []
    }

    static func zip(_ files: [URL], context: OperationContext) async throws -> [URL] {
        try await ArchiveOps.pack(files, as: .zip, context: context)
    }

    static func extract(_ files: [URL], context: OperationContext) async throws -> [URL] {
        try await ArchiveOps.extract(files, context: context)
    }

    static func newFloat(_ files: [URL], context: OperationContext) async throws -> [URL] {
        FloatManager.shared.createFloat(with: files)
        context.finish(with: "Added to a new Float")
        return []
    }
}

@MainActor
enum Clipboard {
    static func copy(text: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }
}

@MainActor
enum Feedback {
    static func success() {
        guard AppSettings.shared.playSounds else { return }
        NSSound(named: "Glass")?.play()
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
    }

    static func failure() {
        guard AppSettings.shared.playSounds else { return }
        NSSound(named: "Basso")?.play()
    }

    static func tick() {
        NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
        guard AppSettings.shared.playSounds else { return }
        TickSound.shared.play()
    }
}

/// The soft click as the pointer moves onto a petal: a few milliseconds of a damped tone, made once in memory.
/// A small pool of players lets quick sweeps across the wheel overlap instead of cutting each other off.
@MainActor
final class TickSound {
    static let shared = TickSound()
    private let players: [AVAudioPlayer]
    private var next = 0

    private init() {
        let data = Self.wave()
        players = (0..<4).compactMap { _ in
            let player = try? AVAudioPlayer(data: data)
            player?.volume = 0.32
            player?.prepareToPlay()
            return player
        }
    }

    func play() {
        guard !players.isEmpty else { return }
        let player = players[next]
        next = (next + 1) % players.count
        player.currentTime = 0
        player.play()
    }

    /// 16-bit mono WAV: a 1.9 kHz tone with a quieter octave, a 1 ms rise and a fast decay, about 35 ms long.
    private static func wave() -> Data {
        let rate = 44_100.0
        let count = Int(rate * 0.035)
        var samples = [Int16](repeating: 0, count: count)
        for index in 0..<count {
            let t = Double(index) / rate
            let envelope = min(1, t / 0.001) * exp(-t / 0.0055)
            let tone = sin(2 * .pi * 1_900 * t) + 0.35 * sin(2 * .pi * 3_800 * t)
            samples[index] = Int16(max(-1, min(1, tone * envelope * 0.55)) * Double(Int16.max))
        }
        var data = Data()
        func append<T: FixedWidthInteger>(_ value: T) { withUnsafeBytes(of: value.littleEndian) { data.append(contentsOf: $0) } }
        let bytes = UInt32(count * 2)
        data.append(contentsOf: Array("RIFF".utf8)); append(36 + bytes)
        data.append(contentsOf: Array("WAVEfmt ".utf8)); append(UInt32(16)); append(UInt16(1)); append(UInt16(1))
        append(UInt32(rate)); append(UInt32(rate * 2)); append(UInt16(2)); append(UInt16(16))
        data.append(contentsOf: Array("data".utf8)); append(bytes)
        samples.forEach { append($0) }
        return data
    }
}
