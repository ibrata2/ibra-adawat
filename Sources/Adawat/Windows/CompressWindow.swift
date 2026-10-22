import AppKit
import SwiftUI

@MainActor
final class CompressModel: ObservableObject {
    enum TargetMode: String, CaseIterable, Identifiable {
        case size, reduction
        var id: String { rawValue }
        var title: String { self == .size ? "Target file size" : "File size reduction" }
    }

    enum SizeUnit: String, CaseIterable, Identifiable {
        case kb = "KB", mb = "MB", gb = "GB"
        var id: String { rawValue }
        var bytes: Double {
            switch self {
            case .kb: return 1_000
            case .mb: return 1_000_000
            case .gb: return 1_000_000_000
            }
        }
    }

    let files: [URL]
    let kind: FileKind
    let originalSize: Int64

    @Published var preset: CompressPreset
    @Published var imageMaxPixels: Int
    @Published var videoMaxHeight: Int
    @Published var useTarget = false
    @Published var targetMode: TargetMode = .size
    @Published var targetValue: Double
    @Published var targetUnit: SizeUnit
    @Published var reductionPercent: Double = 50
    @Published var estimate: Int64?
    @Published var isEstimating = false

    init(files: [URL], kind: FileKind) {
        self.files = files
        self.kind = kind
        originalSize = files.reduce(0) { $0 + ImageOps.fileSize($1) }
        let settings = AppSettings.shared
        preset = settings.compressPreset
        imageMaxPixels = settings.compressImageMaxPixelsRaw
        videoMaxHeight = settings.compressVideoMaxHeightRaw
        // Start the target at half of a single file's size, in a readable unit.
        let half = Double(originalSize / Int64(max(files.count, 1))) / 2
        targetUnit = half >= 1_000_000_000 ? .gb : half >= 1_000_000 ? .mb : .kb
        targetValue = (half / (half >= 1_000_000_000 ? 1e9 : half >= 1_000_000 ? 1e6 : 1e3) * 10).rounded() / 10
    }

    var title: String {
        files.count == 1 ? files[0].lastPathComponent : "\(files.count) files"
    }

    var settings: CompressSettings {
        var settings = CompressSettings(preset: preset,
                                        imageMaxPixels: kind == .image && imageMaxPixels > 0 ? imageMaxPixels : nil,
                                        videoMaxHeight: kind == .video && videoMaxHeight > 0 ? videoMaxHeight : nil)
        if useTarget {
            settings.target = targetMode == .size
                ? .bytes(Int64(targetValue * targetUnit.bytes))
                : .reduction(reductionPercent / 100)
        }
        return settings
    }

    var validationMessage: String? {
        guard useTarget else { return nil }
        switch targetMode {
        case .size:
            let bytes = targetValue * targetUnit.bytes
            if bytes < 1_000 { return "Enter a target size of at least 1 KB." }
            if files.count == 1, Int64(bytes) >= originalSize { return "Target size cannot exceed the original file size." }
        case .reduction:
            if !(1...99).contains(reductionPercent) { return "Choose a reduction from 1% to 99%." }
        }
        return nil
    }

    /// Recomputes the expected size a moment after the settings stop changing.
    func refreshEstimate() async {
        isEstimating = true
        try? await Task.sleep(nanoseconds: 250_000_000)
        guard !Task.isCancelled else { return }
        let value = await Compression.estimate(files, kind: kind, settings: settings)
        guard !Task.isCancelled else { return }
        estimate = value
        isEstimating = false
    }

    func remember() {
        let app = AppSettings.shared
        app.compressPreset = preset
        if kind == .image { app.compressImageMaxPixelsRaw = imageMaxPixels }
        if kind == .video { app.compressVideoMaxHeightRaw = videoMaxHeight }
    }

    /// A key that changes whenever anything affecting the estimate changes.
    var estimateKey: String {
        "\(preset)\(imageMaxPixels)\(videoMaxHeight)\(useTarget)\(targetMode)\(targetValue)\(targetUnit)\(reductionPercent)"
    }
}

struct CompressView: View {
    @StateObject var model: CompressModel
    let handle: ToolWindowHandle

    private static let imageSizes = [3840, 2560, 2048, 1920, 1600, 1280, 1024, 800, 640]
    private static let videoHeights = [2160, 1440, 1080, 720, 480]

    var body: some View {
        ToolScaffold(title: L10n.t("Compress"), onClose: handle.close) {
            VStack(alignment: .leading, spacing: 14) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(model.title).font(.system(size: 12)).foregroundStyle(Theme.muted).lineLimit(1).truncationMode(.middle)
                    Text(L10n.t("Original size: \(ByteText.string(model.originalSize))")).font(.system(size: 14, weight: .semibold))
                }

                VStack(alignment: .leading, spacing: 6) {
                    Text(L10n.t("Compression preset")).font(.system(size: 14, weight: .semibold))
                    Picker("", selection: $model.preset) {
                        ForEach(CompressPreset.allCases) { Text(L10n.t($0.title)).tag($0) }
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                    .disabled(model.useTarget)
                    Text(L10n.t(model.preset.detail)).font(.system(size: 11)).foregroundStyle(Theme.muted)
                }
                .opacity(model.useTarget ? 0.45 : 1)

                if model.kind == .image {
                    ToolRow(title: L10n.t("Image size")) {
                        Picker("", selection: $model.imageMaxPixels) {
                            Text(L10n.t("Keep original dimensions")).tag(0)
                            ForEach(Self.imageSizes, id: \.self) { Text(L10n.t("\($0) px longest edge")).tag($0) }
                        }
                        .labelsHidden().fixedSize()
                    }
                } else if model.kind == .video {
                    ToolRow(title: L10n.t("Maximum video height")) {
                        Picker("", selection: $model.videoMaxHeight) {
                            Text(L10n.t("Keep original")).tag(0)
                            ForEach(Self.videoHeights, id: \.self) { Text(L10n.t("\($0)p")).tag($0) }
                        }
                        .labelsHidden().fixedSize()
                    }
                }

                Divider()

                Toggle(L10n.t("Compress to target file size"), isOn: $model.useTarget)
                    .toggleStyle(.checkbox)
                if model.useTarget {
                    VStack(alignment: .leading, spacing: 10) {
                        ToolRow(title: L10n.t("Compression mode")) {
                            Picker("", selection: $model.targetMode) {
                                ForEach(CompressModel.TargetMode.allCases) { Text(L10n.t($0.title)).tag($0) }
                            }
                            .labelsHidden().fixedSize()
                        }
                        if model.targetMode == .size {
                            ToolRow(title: model.files.count > 1 ? "Size of each file" : "Target file size") {
                                HStack(spacing: 6) {
                                    TextField("", value: $model.targetValue, format: .number.precision(.fractionLength(0...2)))
                                        .textFieldStyle(.roundedBorder).frame(width: 80).multilineTextAlignment(.trailing)
                                    Picker("", selection: $model.targetUnit) {
                                        ForEach(CompressModel.SizeUnit.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                                    }
                                    .labelsHidden().fixedSize()
                                }
                            }
                        } else {
                            ToolRow(title: L10n.t("Reduce by")) {
                                HStack(spacing: 8) {
                                    Slider(value: $model.reductionPercent, in: 1...99, step: 1).frame(width: 160)
                                    Text(L10n.t("\(Int(model.reductionPercent))%")).monospacedDigit().frame(width: 36, alignment: .trailing)
                                }
                            }
                        }
                    }
                    .padding(.leading, 20)
                }

                if let message = model.validationMessage {
                    Text(message).font(.system(size: 11)).foregroundStyle(Theme.accent)
                }

                HStack(spacing: 6) {
                    Text(L10n.t("Expected size:")).font(.system(size: 12)).foregroundStyle(Theme.muted)
                    if model.isEstimating {
                        ProgressView().controlSize(.mini)
                    } else if let estimate = model.estimate {
                        Text(ByteText.string(estimate)).font(.system(size: 13, weight: .semibold)).monospacedDigit()
                        if model.originalSize > 0 {
                            let saved = 100 - Int((Double(estimate) / Double(model.originalSize) * 100).rounded())
                            Text(L10n.t(saved > 0 ? "about \(saved)% smaller" : "little to gain"))
                                .font(.system(size: 12)).foregroundStyle(Theme.muted)
                        }
                    } else {
                        Text("—").foregroundStyle(Theme.muted)
                    }
                }
            }
        } leading: {
            Button(L10n.t("Restore Defaults")) {
                model.preset = .balanced
                model.imageMaxPixels = 0
                model.videoMaxHeight = 0
                model.useTarget = false
            }
            .buttonStyle(.tool)
        } trailing: {
            Button(L10n.t("Compress"), action: compress)
                .buttonStyle(.tool)
                .keyboardShortcut(.defaultAction)
                .disabled(model.validationMessage != nil)
        }
        .task(id: model.estimateKey) { await model.refreshEstimate() }
    }

    private func compress() {
        model.remember()
        let settings = model.settings
        let kind = model.kind
        handle.close()
        OperationRunner.run("Compress", on: model.files) { files, context in
            switch kind {
            case .video: return try await Compression.videos(files, settings, context: context)
            case .audio: return try await Compression.audio(files, settings, context: context)
            default: return try await Compression.images(files, settings, context: context)
            }
        }
    }
}

@MainActor
enum CompressWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        guard let kind = files.commonKind, [.image, .video, .audio].contains(kind) else { return nil }
        return ToolWindows.present(L10n.t("Compress"), width: 380, show: show) { handle in
            CompressView(model: CompressModel(files: files, kind: kind), handle: handle)
        }
    }
}
