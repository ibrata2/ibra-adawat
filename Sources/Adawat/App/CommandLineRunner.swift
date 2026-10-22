import ImageIO
import AppKit
import SwiftUI

/// `Adawat --list <files…>` prints the wheel choices for the files.
/// `Adawat --run <operation-id> <files…>` runs one choice without the UI.
@MainActor
enum CommandLineRunner {
    static func run(_ arguments: [String]) {
        _ = NSApplication.shared
        let command = arguments[0]
        if command == "--doctor" {
            doctor()
            exit(0)
        }
        if command == "--list" {
            let files = arguments.dropFirst().map(fileURL)
            for mode in WheelMode.allCases {
                let ids = OperationCatalog.operations(for: mode, files: files).map(\.id)
                print("\(mode.title): \(ids.joined(separator: " "))")
            }
            exit(0)
        }

        // Adawat --compress <light|balanced|strong> <max-pixels or 0> <target-bytes or 0> <files…>
        if command == "--compress", arguments.count >= 5 {
            var settings = CompressSettings(preset: CompressPreset(rawValue: arguments[1]) ?? .balanced)
            if let pixels = Int(arguments[2]), pixels > 0 { settings.imageMaxPixels = pixels; settings.videoMaxHeight = pixels }
            if let bytes = Int64(arguments[3]), bytes > 0 { settings.target = .bytes(bytes) }
            let files = arguments.dropFirst(4).map(fileURL)
            Task { @MainActor in
                do {
                    let context = OperationContext()
                    let outputs: [URL]
                    switch files.commonKind {
                    case .video: outputs = try await Compression.videos(files, settings, context: context)
                    case .audio: outputs = try await Compression.audio(files, settings, context: context)
                    default: outputs = try await Compression.images(files, settings, context: context)
                    }
                    if let message = context.message { print(message) }
                    outputs.forEach { print($0.path) }
                    exit(0)
                } catch {
                    fputs("error: \(error.localizedDescription)\n", stderr)
                    exit(1)
                }
            }
            RunLoop.main.run()
        }

        // Adawat --trim <start-seconds> <end-seconds> [--silence] <file>
        if command == "--trim", arguments.count >= 4, let start = Double(arguments[1]), let end = Double(arguments[2]) {
            let silence = arguments.contains("--silence")
            let file = fileURL(arguments.last!)
            Task { @MainActor in
                do {
                    print(try await MediaOps.trim(file, start: start, end: end, trimSilence: silence, context: OperationContext()).path)
                    exit(0)
                } catch {
                    fputs("error: \(error.localizedDescription)\n", stderr)
                    exit(1)
                }
            }
            RunLoop.main.run()
        }

        // Adawat --annotate-sample <in> <out.png>: burns the sample marks into the full-size photo.
        if command == "--annotate-sample", arguments.count >= 3 {
            let input = fileURL(arguments[1])
            guard let model = try? AnnotateModel(file: input), let full = try? ImageFile(input).upright() else { exit(1) }
            AnnotateWindow.addSample(to: model)
            if let image = AnnotationRenderer.render(model.annotations, on: full) {
                try? ImageEngine.write(image, as: .png, to: fileURL(arguments[2]), quality: 1, metadata: [:])
            }
            exit(0)
        }

        // Adawat --edit-sample <in> <out>: saves a copy with a fixed set of adjustments.
        if command == "--edit-sample", arguments.count >= 3 {
            var adjustments = PhotoAdjustments()
            adjustments[.exposure] = 0.6
            adjustments[.contrast] = 30
            adjustments[.temperature] = 40
            adjustments[.vignette] = 60
            adjustments[.grain] = 40
            let input = fileURL(arguments[1]), output = fileURL(arguments[2])
            Task { @MainActor in
                do {
                    try await ImageEngine.saveFiltered(try ImageFile(input), as: ImageFormat.of(output) ?? .png, to: output,
                                                       quality: 0.92) { adjustments.apply(to: $0) }
                    exit(0)
                } catch {
                    fputs("error: \(error.localizedDescription)\n", stderr)
                    exit(1)
                }
            }
            RunLoop.main.run()
        }

        if command == "--demo-gif", arguments.count >= 3 {
            DemoGIF.make(to: fileURL(arguments[1]), photo: fileURL(arguments[2]))
            exit(0)
        }

        // Adawat --qr-sample <out-dir> [logo]: every shape and corner style, with the logo if given, and whether each scans.
        if command == "--qr-sample", arguments.count >= 2 {
            let folder = fileURL(arguments[1])
            try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let payload = "https://example.com/adawat?q=مرحبا"
            var logo: CGImage?
            if arguments.count >= 3, let source = CGImageSourceCreateWithURL(fileURL(arguments[2]) as CFURL, nil) {
                logo = CGImageSourceCreateImageAtIndex(source, 0, nil)
            }
            let semaphore = DispatchSemaphore(value: 0)
            Task { @MainActor in
                for module in QRDesign.ModuleShape.allCases {
                    for eye in QRDesign.EyeShape.allCases {
                        var design = QRDesign()
                        design.module = module
                        design.eye = eye
                        design.logo = logo
                        design.foreground = NSColor(red: 0.1, green: 0.1, blue: 0.35, alpha: 1)
                        design.gradientEnd = module == .fluid ? NSColor(red: 0.36, green: 0.28, blue: 0.9, alpha: 1) : nil
                        guard let matrix = QRMatrix(payload, correction: logo == nil ? .medium : .high),
                              let image = design.image(matrix, pixels: 800) else { continue }
                        let name = "\(module.rawValue)-\(eye.rawValue)"
                        try? NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])?
                            .write(to: folder.appendingPathComponent(name + ".png"))
                        try? design.svg(matrix).write(to: folder.appendingPathComponent(name + ".svg"), atomically: true, encoding: .utf8)
                        let found = (try? await ImageOps.readQRCodes(in: image)) ?? []
                        print("\(name): \(found.contains(payload) ? "scans" : "FAILS")")
                    }
                }
                semaphore.signal()
            }
            while semaphore.wait(timeout: .now()) == .timedOut { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
            exit(0)
        }

        // Adawat --metadata-sample <in> <out>: drops GPS fields and rewrites the user comment.
        if command == "--metadata-sample", arguments.count >= 3, let file = try? ImageFile(fileURL(arguments[1])) {
            var fields = ImageMetadata.fields(of: file)
            for index in fields.indices {
                if fields[index].group == "GPS" { fields[index].removed = true }
                if fields[index].name == "User Comment" { fields[index].value = "edited by Adawat" }
            }
            do {
                try ImageMetadata.save(fields, of: file, to: fileURL(arguments[2]))
                print(fields.filter { $0.path != nil }.map { "\($0.group) \($0.name)" }.joined(separator: " | "))
                exit(0)
            } catch {
                fputs("error: \(error.localizedDescription)\n", stderr)
                exit(1)
            }
        }

        // Adawat --media <speed|crop|split|split-join|frames|redact|redact-blur|normalize|visualize|mono|bleep> <file> [value]
        if command == "--media", arguments.count >= 3 {
            let tool = arguments[1], file = fileURL(arguments[2])
            let value = arguments.count > 3 ? Double(arguments[3]) ?? 1 : 1
            Task { @MainActor in
                do {
                    let context = OperationContext()
                    let box = CGRect(x: 0.1, y: 0.1, width: 0.4, height: 0.3)
                    let outputs: [URL] = switch tool {
                    case "speed": [try await MediaOps.changeSpeed(file, speed: value, context: context)]
                    case "crop": try await MediaOps.cropVideo([file], rect: CGRect(x: 0.25, y: 0, width: 0.5, height: 1), context: context)
                    case "split": try await MediaOps.split(file, points: [1, 2], removed: [], joinKept: false, context: context)
                    case "split-join": try await MediaOps.split(file, points: [1, 2], removed: [1], joinKept: true, context: context)
                    case "frames": try await MediaOps.saveFrames(file, times: [0.5, 1.25, 2], context: context)
                    case "redact": [try await MediaOps.redactVideo(file, redactions: [VideoRedaction(rect: box, start: 0, end: 1.5)],
                                                                    blur: false, color: RGB(0, 0, 0), context: context)]
                    case "redact-blur": [try await MediaOps.redactVideo(file, redactions: [VideoRedaction(rect: box, start: 0, end: 99)],
                                                                         blur: true, color: RGB(0, 0, 0), context: context)]
                    case "normalize": [try await MediaOps.normalize(file, target: value == 1 ? -14 : value, range: 9, peak: -1, context: context)]
                    case "visualize": [try await MediaOps.visualize(file, image: nil, shape: .square, color: RGB(1, 0.32, 0), context: context)]
                    case "mono": [try await MediaOps.convertChannels(file, mono: true, left: 1, right: 0.5, context: context)]
                    case "bleep": [try await MediaOps.bleep(file, ranges: [0.5...1.0, 2.0...2.5], context: context)]
                    case "organize":
                        // Reverse the pages, turn the first one and repeat the last.
                        try await {
                            let count = try PDFOps.open(file).pageCount
                            var slots = (0..<count).reversed().map { PageSlot(source: $0) }
                            slots[0].rotation = 90
                            slots.append(PageSlot(source: 0))
                            return [try await PDFOps.organize(file, slots: slots, equalWidths: true, context: context)]
                        }()
                    default: throw AdawatError.message("unknown media tool \(tool)")
                    }
                    if let message = context.message { print(message) }
                    outputs.forEach { print($0.path) }
                    exit(0)
                } catch {
                    fputs("error: \(error.localizedDescription)\n", stderr)
                    exit(1)
                }
            }
            RunLoop.main.run()
        }

        if command == "--snapshot-window", arguments.count >= 4 {
            snapshotWindow(arguments[1], to: fileURL(arguments[2]), files: fileArguments(arguments.dropFirst(3)))
            exit(0)
        }

        if command == "--snapshot", arguments.count >= 4 {
            snapshot(mode: WheelMode(rawValue: arguments[1]), to: fileURL(arguments[2]),
                     files: fileArguments(arguments.dropFirst(3)))
            exit(0)
        }

        guard arguments.count >= 3 else {
            fputs("usage: Adawat --run <operation-id> <files…>\n", stderr)
            exit(2)
        }
        let id = arguments[1]
        let files = arguments.dropFirst(2).map(fileURL)
        let all = WheelMode.allCases.flatMap { OperationCatalog.operations(for: $0, files: files) }
        guard let operation = all.first(where: { $0.id == id }) else {
            fputs("No operation \(id) for these files. Available: \(all.map(\.id).joined(separator: " "))\n", stderr)
            exit(2)
        }

        Task { @MainActor in
            do {
                let context = OperationContext()
                let outputs = try await operation.run(files, context)
                if let message = context.message { print(message) }
                outputs.forEach { print($0.path) }
                exit(0)
            } catch {
                fputs("error: \(error.localizedDescription)\n", stderr)
                exit(1)
            }
        }
        RunLoop.main.run()
    }

    /// Renders the wheel for `files` to a PNG, for checking its layout without a screen capture.
    private static func snapshot(mode: WheelMode?, to output: URL, files: [URL]) {
        guard let mode else {
            snapshotFloat(to: output, files: files)
            return
        }
        let model = WheelModel()
        model.mode = mode
        model.operations = OperationCatalog.operations(for: mode, files: files)
        model.fileCount = files.count
        model.hoveredIndex = 1
        model.isVisible = true
        let view = WheelView(model: model)
            .background(Color(white: 0.96))
            .environment(\.colorScheme, .light)
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let image = renderer.cgImage else { return }
        let rep = NSBitmapImageRep(cgImage: image)
        try? rep.representation(using: .png, properties: [:])?.write(to: output)
        print(output.path)
    }

    /// Draws a real Float window holding `files` into a PNG, after thumbnails load.
    private static func snapshotFloat(to output: URL, files: [URL]) {
        let controller = FloatWindowController(files: files)
        controller.model.selection = Set(files.prefix(1))
        RunLoop.main.run(until: Date().addingTimeInterval(2))
        guard let rep = controller.snapshot() else { return }
        try? rep.representation(using: .png, properties: [:])?.write(to: output)
        print(output.path)
    }

    private static func render<V: View>(_ view: V, to output: URL) {
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let image = renderer.cgImage else { return }
        try? NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:])?.write(to: output)
        print(output.path)
    }

    /// One screen of facts for bug reports: versions, chip, tools and encoders.
    private static func doctor() {
        let bundle = Bundle.main.infoDictionary
        var chip = [CChar](repeating: 0, count: 256)
        var size = chip.count
        sysctlbyname("machdep.cpu.brand_string", &chip, &size, nil, 0)
        let lines = [
            "Adawat \(bundle?["CFBundleShortVersionString"] as? String ?? "dev")",
            "macOS \(ProcessInfo.processInfo.operatingSystemVersionString)",
            "Chip: \(String(cString: chip))",
            "ffmpeg: \(ExternalTool.ffmpeg.url?.path ?? "missing (brew install ffmpeg)")",
            "cwebp: \(ExternalTool.cwebp.url?.path ?? "missing (brew install webp), WebP output disabled")",
            "Hardware video: H.264 \(FFmpeg.encoders.contains("h264_videotoolbox") ? "yes" : "no"), "
                + "HEVC \(FFmpeg.encoders.contains("hevc_videotoolbox") ? "yes" : "no")",
            "AAC encoder: \(FFmpeg.aac)",
            "Image output: " + ImageFormat.all.filter(ImageEngine.canWrite).map(\.name).joined(separator: ", "),
            "HDR gain maps: \(ProcessInfo.processInfo.isOperatingSystemAtLeast(OperatingSystemVersion(majorVersion: 15, minorVersion: 0, patchVersion: 0)) ? "yes" : "needs macOS 15")",
            "Log: ~/Library/Logs/Adawat/adawat.log",
        ]
        print(lines.joined(separator: "\n"))
    }

    /// Builds a tool window off screen, lets it settle, and saves what it draws.
    private static func snapshotWindow(_ name: String, to output: URL, files: [URL]) {
        guard let window = ToolWindowCatalog.make(name, files: files, show: false) else {
            fputs("No tool window \(name). Known: \(ToolWindowCatalog.names.joined(separator: " "))\n", stderr)
            return
        }
        RunLoop.main.run(until: Date().addingTimeInterval(2.5))
        guard let rep = ToolWindows.snapshot(window) else { return }
        try? rep.representation(using: .png, properties: [:])?.write(to: output)
        print(output.path)
    }

    /// The file paths among `arguments`, leaving out defaults overrides such as `-AppleLanguages (ar)`.
    private static func fileArguments(_ arguments: ArraySlice<String>) -> [URL] {
        var files: [URL] = []
        var skipNext = false
        for argument in arguments {
            if skipNext { skipNext = false; continue }
            if argument.hasPrefix("-"), !argument.hasPrefix("--") { skipNext = true; continue }
            files.append(fileURL(argument))
        }
        return files
    }

    private static func fileURL(_ path: String) -> URL {
        URL(fileURLWithPath: (path as NSString).expandingTildeInPath).standardizedFileURL
    }
}
