import Foundation

enum ExternalTool: String {
    case ffmpeg, ffprobe, cwebp

    private static let searchPaths = [
        "/opt/homebrew/bin", "/usr/local/bin", "\(NSHomeDirectory())/.local/bin", "/usr/bin",
    ]

    /// The tool bundled in Adawat.app, or the first copy found on this Mac.
    var url: URL? {
        if let bundled = Bundle.main.url(forResource: rawValue, withExtension: nil, subdirectory: "Tools") {
            return bundled
        }
        for dir in Self.searchPaths {
            let candidate = URL(fileURLWithPath: dir).appendingPathComponent(rawValue)
            if FileManager.default.isExecutableFile(atPath: candidate.path) { return candidate }
        }
        return nil
    }

    func require() throws -> URL {
        guard let url else {
            throw AdawatError.message("\(rawValue) is not installed. Install it with: brew install \(installName)")
        }
        return url
    }

    private var installName: String {
        switch self {
        case .ffmpeg, .ffprobe: return "ffmpeg"
        case .cwebp: return "webp"
        }
    }
}

enum ProcessRunner {
    struct Result {
        let status: Int32
        let stdout: Data
        let stderr: String
    }

    /// Runs a command off the main thread and throws with its stderr when it fails.
    @discardableResult
    static func run(_ executable: URL, _ arguments: [String], currentDirectory: URL? = nil) async throws -> Result {
        try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let process = Process()
                process.executableURL = executable
                process.arguments = arguments
                if let currentDirectory { process.currentDirectoryURL = currentDirectory }
                let out = Pipe()
                let err = Pipe()
                process.standardOutput = out
                process.standardError = err
                process.standardInput = FileHandle.nullDevice
                do {
                    try process.run()
                    // Read both pipes before waiting so a full buffer cannot block the child.
                    var outData = Data()
                    var errData = Data()
                    let group = DispatchGroup()
                    group.enter()
                    DispatchQueue.global().async {
                        outData = out.fileHandleForReading.readDataToEndOfFile()
                        group.leave()
                    }
                    errData = err.fileHandleForReading.readDataToEndOfFile()
                    group.wait()
                    process.waitUntilExit()
                    let result = Result(
                        status: process.terminationStatus,
                        stdout: outData,
                        stderr: String(decoding: errData, as: UTF8.self)
                    )
                    if result.status == 0 {
                        continuation.resume(returning: result)
                    } else {
                        let lastLine = result.stderr
                            .split(separator: "\n").last.map(String.init) ?? "exit code \(result.status)"
                        continuation.resume(throwing: AdawatError.message(
                            "\(executable.lastPathComponent) failed: \(lastLine)"))
                    }
                } catch {
                    continuation.resume(throwing: error)
                }
            }
        }
    }
}
