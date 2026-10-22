import AppKit
import AVKit
import SwiftUI

@MainActor
final class TrimModel: ObservableObject {
    let file: URL
    let isVideo: Bool
    let duration: Double
    let frameStep: Double
    let player: AVPlayer?

    @Published var start: Double = 0
    @Published var end: Double
    @Published var current: Double = 0
    @Published var isPlaying = false
    @Published var thumbnails: [CGImage] = []
    @Published var waveform: [Float] = []
    @Published var trimSilence = false

    private var timeObserver: Any?
    /// Where playback should stop: the end of the selection while previewing it.
    private var stopAt: Double?

    init(file: URL, info: MediaInfo, frameRate: Double) {
        self.file = file
        isVideo = info.hasVideo
        duration = info.duration
        end = info.duration
        frameStep = 1 / max(frameRate, 1)
        let playable = ["mov", "mp4", "m4v", "m4a", "mp3", "wav", "aiff", "aif", "caf", "flac", "aac"]
        player = playable.contains(file.pathExtension.lowercased()) ? AVPlayer(url: file) : nil
        timeObserver = player?.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] time in
            MainActor.assumeIsolated { self?.tick(time.seconds) }
        }
    }

    func stop() {
        player?.pause()
        if let timeObserver { player?.removeTimeObserver(timeObserver) }
        timeObserver = nil
    }

    var selectedLength: Double { max(0, end - start) }

    private func tick(_ seconds: Double) {
        guard seconds.isFinite else { return }
        current = seconds
        if let stopAt, seconds >= stopAt {
            player?.pause()
            isPlaying = false
            self.stopAt = nil
        }
    }

    func seek(_ seconds: Double) {
        let time = min(max(0, seconds), duration)
        current = time
        player?.seek(to: CMTime(seconds: time, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }

    func step(_ frames: Int) {
        player?.pause()
        isPlaying = false
        seek(current + Double(frames) * frameStep)
    }

    /// Plays the selected range from its start and stops at its end.
    func playSelection() {
        guard let player else { return }
        if isPlaying {
            player.pause()
            isPlaying = false
            return
        }
        if current < start || current >= end - 0.05 { seek(start) }
        stopAt = end
        player.play()
        isPlaying = true
    }

    func setStart(_ value: Double) {
        start = min(max(0, value), end - 0.1)
        seek(start)
    }

    func setEnd(_ value: Double) {
        end = max(min(duration, value), start + 0.1)
        seek(end)
    }

    func loadStrip() async {
        if isVideo {
            let generator = AVAssetImageGenerator(asset: AVURLAsset(url: file))
            generator.appliesPreferredTrackTransform = true
            generator.maximumSize = CGSize(width: 200, height: 200)
            var images: [CGImage] = []
            for index in 0..<10 {
                let time = CMTime(seconds: duration * (Double(index) + 0.5) / 10, preferredTimescale: 600)
                if let (image, _) = try? await generator.image(at: time) { images.append(image) }
            }
            thumbnails = images
        } else {
            waveform = await MediaOps.waveform(file, bars: 220)
        }
    }
}

struct TrimView: View {
    @ObservedObject var model: TrimModel
    let handle: ToolWindowHandle
    @State private var dragStart: (start: Double, end: Double)?

    var body: some View {
        ToolScaffold(title: model.isVideo ? "Trim Video" : "Trim Audio", onClose: close) {
            VStack(alignment: .leading, spacing: 10) {
                if model.isVideo {
                    PlayerSurface(player: model.player)
                        .frame(height: 300)
                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                }
                HStack {
                    Text(L10n.t("Drag the ends to trim")).foregroundStyle(Theme.muted)
                    Spacer()
                    Text(L10n.t("\(TimeText.string(model.selectedLength)) selected")).foregroundStyle(Theme.muted).monospacedDigit()
                }
                .font(.system(size: 11))
                timeline
                    .frame(height: model.isVideo ? 64 : 96)
                HStack {
                    timeField("Start", value: model.start, set: model.setStart)
                    Spacer()
                    timeField("End", value: model.end, set: model.setEnd)
                }
                if model.isVideo {
                    HStack(spacing: 14) {
                        controlButton("backward.frame") { model.step(-1) }.help(L10n.t("Previous frame"))
                        controlButton(model.isPlaying ? "pause.fill" : "play.fill") { model.playSelection() }.help(L10n.t("Play the selection"))
                        controlButton("forward.frame") { model.step(1) }.help(L10n.t("Next frame"))
                        Spacer()
                        Text(L10n.t("\(TimeText.string(model.current)) / \(TimeText.string(model.duration))"))
                            .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                    }
                } else {
                    Toggle(L10n.t("Trim silent ends automatically"), isOn: $model.trimSilence).toggleStyle(.checkbox)
                        .font(.system(size: 13))
                    HStack {
                        Spacer()
                        Button { model.playSelection() } label: {
                            Label(model.isPlaying ? "Stop" : "Play Selected Range",
                                  systemImage: model.isPlaying ? "stop.fill" : "play.fill")
                        }
                        .buttonStyle(.tool)
                        .disabled(model.player == nil)
                    }
                }
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Apply"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
        .task { await model.loadStrip() }
    }

    private var timeline: some View {
        GeometryReader { geo in
            let width = geo.size.width
            let x1 = CGFloat(model.start / model.duration) * width
            let x2 = CGFloat(model.end / model.duration) * width
            let playhead = CGFloat(model.current / model.duration) * width
            ZStack(alignment: .leading) {
                strip(width: width, height: geo.size.height)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                // Dim what will be cut.
                Rectangle().fill(Color.white.opacity(0.65)).frame(width: max(0, x1))
                Rectangle().fill(Color.white.opacity(0.65)).frame(width: max(0, width - x2)).offset(x: x2)
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .strokeBorder(Theme.accent, lineWidth: 3)
                    .frame(width: max(24, x2 - x1))
                    .offset(x: x1)
                    .allowsHitTesting(false)
                Rectangle().fill(Theme.ink.opacity(0.8)).frame(width: 1.5).offset(x: playhead).allowsHitTesting(false)
                trimHandle.offset(x: x1)
                    .gesture(handleDrag(width) { model.setStart($0) })
                trimHandle.offset(x: x2 - 12)
                    .gesture(handleDrag(width) { model.setEnd($0) })
            }
            .contentShape(Rectangle())
            .onTapGesture { location in model.seek(Double(location.x / width) * model.duration) }
        }
        .environment(\.layoutDirection, .leftToRight)
    }

    @ViewBuilder
    private func strip(width: CGFloat, height: CGFloat) -> some View {
        if model.isVideo {
            HStack(spacing: 0) {
                ForEach(Array(model.thumbnails.enumerated()), id: \.offset) { _, image in
                    Image(decorative: image, scale: 1).resizable().aspectRatio(contentMode: .fill)
                        .frame(width: width / CGFloat(max(model.thumbnails.count, 1)), height: height).clipped()
                }
            }
            .frame(width: width, height: height)
            .background(Color.black.opacity(0.8))
        } else {
            Canvas { context, size in
                let bars = model.waveform
                guard !bars.isEmpty else { return }
                let step = size.width / CGFloat(bars.count)
                for (index, level) in bars.enumerated() {
                    let h = max(1.5, CGFloat(level) * size.height * 0.85)
                    let rect = CGRect(x: CGFloat(index) * step, y: (size.height - h) / 2, width: max(1, step * 0.55), height: h)
                    context.fill(Path(rect), with: .color(Theme.accent.opacity(0.9)))
                }
            }
            .background(Theme.accent.opacity(0.08))
        }
    }

    private var trimHandle: some View {
        RoundedRectangle(cornerRadius: 4, style: .continuous)
            .fill(Theme.accent)
            .overlay(Capsule().fill(Color.white).frame(width: 2.5, height: 18))
            .frame(width: 12)
            .contentShape(Rectangle().inset(by: -6))
    }

    private func handleDrag(_ width: CGFloat, _ set: @escaping (Double) -> Void) -> some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .local)
            .onChanged { value in set(Double(value.location.x / width) * model.duration) }
    }

    private func timeField(_ label: String, value: Double, set: @escaping (Double) -> Void) -> some View {
        HStack(spacing: 6) {
            Text(label).foregroundStyle(Theme.muted)
            TextField("", text: Binding(get: { TimeText.string(value) },
                                        set: { if let seconds = TimeText.parse($0) { set(seconds) } }))
                .textFieldStyle(.plain)
                .font(.system(size: 12, design: .monospaced))
                .frame(width: 70)
        }
        .font(.system(size: 12))
    }

    private func controlButton(_ symbol: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 13, weight: .medium)).frame(width: 24, height: 22)
        }
        .buttonStyle(.plain)
        .disabled(model.player == nil)
    }

    private func close() {
        model.stop()
        handle.close()
    }

    private func apply() {
        let (start, end, silence) = (model.start, model.end, model.trimSilence)
        let file = model.file
        close()
        OperationRunner.run("Trim", on: [file]) { files, context in
            [try await MediaOps.trim(files[0], start: start, end: end, trimSilence: silence, context: context)]
        }
    }
}

/// A bare AVPlayer surface on black, without the system's playback controls.
struct PlayerSurface: NSViewRepresentable {
    let player: AVPlayer?

    func makeNSView(context: Context) -> AVPlayerView {
        let view = AVPlayerView()
        view.player = player
        view.controlsStyle = .none
        view.videoGravity = .resizeAspect
        view.wantsLayer = true
        view.layer?.backgroundColor = NSColor.black.cgColor
        return view
    }

    func updateNSView(_ view: AVPlayerView, context: Context) {}
}

@MainActor
enum TrimWindow {
    /// The window opened most recently, for the snapshot mode (opening waits on an async probe).
    private(set) static var lastWindow: NSWindow?

    static func open(_ files: [URL]) { open(files, show: true) }

    static func open(_ files: [URL], show: Bool) {
        guard let file = files.first else { return }
        Task { @MainActor in
            let info = await MediaProbe.info(file)
            guard info.duration > 0 else {
                HUDController.shared.show(L10n.t("Couldn't read the length of \(file.lastPathComponent)"), style: .failure)
                return
            }
            var frameRate = 30.0
            if let track = try? await AVURLAsset(url: file).loadTracks(withMediaType: .video).first,
               let rate = try? await track.load(.nominalFrameRate), rate > 0 {
                frameRate = Double(rate)
            }
            let model = TrimModel(file: file, info: info, frameRate: frameRate)
            let window = ToolWindows.present(info.hasVideo ? "Trim Video" : "Trim Audio", width: info.hasVideo ? 560 : 480,
                                             show: show) { handle in
                TrimView(model: model, handle: handle)
            }
            window.delegate = TrimWindowDelegate.shared
            TrimWindowDelegate.shared.models[ObjectIdentifier(window)] = model
            lastWindow = window
        }
    }
}

/// Stops playback when a trim window closes by any route.
@MainActor
final class TrimWindowDelegate: NSObject, NSWindowDelegate {
    static let shared = TrimWindowDelegate()
    var models: [ObjectIdentifier: TrimModel] = [:]

    func windowWillClose(_ notification: Notification) {
        guard let window = notification.object as? NSWindow else { return }
        models.removeValue(forKey: ObjectIdentifier(window))?.stop()
    }
}
