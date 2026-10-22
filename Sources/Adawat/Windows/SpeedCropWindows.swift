import AppKit
import SwiftUI

// MARK: - Change Video Speed

/// Maps a 0…5 slider position onto the speeds marked under it, evenly spaced like the labels.
enum SpeedScale {
    static let marks: [Double] = [0.25, 0.5, 1, 1.5, 2, 4]

    static func speed(at position: Double) -> Double {
        let index = min(max(position, 0), Double(marks.count - 1))
        let lower = Int(index.rounded(.down)), upper = min(lower + 1, marks.count - 1)
        let t = index - Double(lower)
        var value = marks[lower] + (marks[upper] - marks[lower]) * t
        // Snap to a mark when close, so 1× is easy to hit again.
        if let near = marks.first(where: { abs($0 - value) < 0.04 }) { value = near }
        return (value * 100).rounded() / 100
    }

    static func position(of speed: Double) -> Double {
        for index in 0..<(marks.count - 1) where speed <= marks[index + 1] {
            return Double(index) + (speed - marks[index]) / (marks[index + 1] - marks[index])
        }
        return Double(marks.count - 1)
    }
}

struct SpeedView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @State private var position = SpeedScale.position(of: 1)

    private var speed: Double { SpeedScale.speed(at: position) }

    var body: some View {
        ToolScaffold(title: media.info.hasVideo ? "Change Video Speed" : "Change Speed", onClose: close) {
            VStack(alignment: .leading, spacing: 12) {
                if media.info.hasVideo { VideoStage(media: media) } else { TimelineStrip(media: media, height: 90) { _ in EmptyView() } }
                HStack {
                    Text(L10n.t("Playback speed")).font(.system(size: 12))
                    Spacer()
                    Text(String(format: "%g×", speed)).font(.system(size: 13, weight: .semibold)).monospacedDigit()
                }
                Slider(value: $position, in: 0...Double(SpeedScale.marks.count - 1))
                    .onChange(of: position) { _, _ in if media.isPlaying { media.player?.rate = Float(speed) } }
                HStack {
                    ForEach(SpeedScale.marks, id: \.self) { mark in
                        Button(String(format: "%g×", mark)) { position = SpeedScale.position(of: mark) }
                            .buttonStyle(.plain)
                            .font(.system(size: 10, weight: abs(speed - mark) < 0.001 ? .bold : .regular))
                            .foregroundStyle(abs(speed - mark) < 0.001 ? Theme.ink : Theme.muted)
                        if mark != SpeedScale.marks.last { Spacer() }
                    }
                }
                HStack {
                    Button { media.play(rate: Float(speed)) } label: { Image(systemName: media.isPlaying ? "pause.fill" : "play.fill") }
                        .buttonStyle(.plain).disabled(media.player == nil).help(L10n.t("Preview at this speed"))
                    Spacer()
                    Text(L10n.t("\(TimeText.string(media.current)) / \(TimeText.string(media.duration))"))
                        .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                }
                Text(L10n.t("Audio keeps its pitch. The new length is \(TimeText.string(media.duration / speed))."))
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Apply"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(speed == 1)
        }
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func apply() {
        let (file, speed) = (media.file, speed)
        close()
        OperationRunner.run("Change Speed", on: [file]) { files, context in
            [try await MediaOps.changeSpeed(files[0], speed: speed, context: context)]
        }
    }
}

@MainActor
enum SpeedWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Change Video Speed"), width: 540, show: show) { SpeedView(media: $0, handle: $1) }
    }
}

// MARK: - Crop Video

struct CropVideoView: View {
    @ObservedObject var media: MediaPlayerModel
    @ObservedObject var crop: CropModel
    let handle: ToolWindowHandle
    @State private var scrub: Double = 0

    private var displaySize: CGSize {
        let scale = min(480 / crop.pixelWidth, 380 / crop.pixelHeight)
        return CGSize(width: crop.pixelWidth * scale, height: crop.pixelHeight * scale)
    }

    var body: some View {
        ToolScaffold(title: L10n.t("Crop Video"), onClose: handle.close) {
            VStack(spacing: 12) {
                CropCanvas(model: crop, size: displaySize).frame(maxWidth: .infinity)
                ToolRow(title: L10n.t("Aspect ratio")) {
                    Picker("", selection: $crop.ratio) {
                        ForEach(CropRatio.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                }
                HStack(spacing: 8) {
                    Text("W").foregroundStyle(Theme.muted)
                    TextField("", value: $crop.widthPixels, format: .number.grouping(.never)).textFieldStyle(.roundedBorder).frame(width: 70)
                    Text("H").foregroundStyle(Theme.muted)
                    TextField("", value: $crop.heightPixels, format: .number.grouping(.never)).textFieldStyle(.roundedBorder).frame(width: 70)
                    Text("px").foregroundStyle(Theme.muted)
                    Spacer()
                }
                .font(.system(size: 12))
                Slider(value: $scrub, in: 0...max(media.duration, 0.01))
                    .onChange(of: scrub) { _, time in Task { await showFrame(at: time) } }
                HStack(spacing: 14) {
                    Button { scrub = max(0, scrub - media.frameStep) } label: { Image(systemName: "backward.frame") }.buttonStyle(.plain)
                    Button { scrub = min(media.duration, scrub + media.frameStep) } label: { Image(systemName: "forward.frame") }.buttonStyle(.plain)
                    Spacer()
                    Text(L10n.t("\(TimeText.string(scrub)) / \(TimeText.string(media.duration))"))
                        .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                }
            }
        } leading: {
            Button(L10n.t("Reset"), action: crop.reset).buttonStyle(.tool)
        } trailing: {
            Button(L10n.t("Apply"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
    }

    private func showFrame(at time: Double) async {
        if let image = await media.frame(at: time, maxSize: 1200) { crop.preview = image }
    }

    private func apply() {
        let (file, rect) = (media.file, crop.rect)
        handle.close()
        OperationRunner.run("Crop Video", on: [file]) { files, context in
            try await MediaOps.cropVideo(files, rect: rect, context: context)
        }
    }
}

@MainActor
enum CropVideoWindow {
    private(set) static var lastWindow: NSWindow?

    static func open(_ files: [URL]) { open(files, show: true) }

    static func open(_ files: [URL], show: Bool) {
        guard let file = files.first else { return }
        Task { @MainActor in
            guard let media = await MediaPlayerModel.load(file), let first = await media.frame(at: 0, maxSize: 1200) else {
                HUDController.shared.show(L10n.t("Couldn't read a frame from \(file.lastPathComponent)"), style: .failure)
                return
            }
            // The crop is typed in the video's upright pixels; the preview frame keeps the same shape.
            let scale = Double(max(media.info.width, media.info.height)) / Double(max(first.width, first.height))
            let crop = CropModel(files: [file], preview: first, pixelWidth: (CGFloat(first.width) * scale).rounded(),
                                 pixelHeight: (CGFloat(first.height) * scale).rounded())
            lastWindow = ToolWindows.present(L10n.t("Crop Video"), width: 520, show: show) { handle in
                CropVideoView(media: media, crop: crop, handle: handle)
            }
        }
    }
}
