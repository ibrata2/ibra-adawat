import AppKit
import AVKit
import SwiftUI

/// Playback state shared by the video and audio tool windows: an AVPlayer when AVFoundation can play
/// the file, the current time, frame stepping and range playback.
@MainActor
final class MediaPlayerModel: ObservableObject {
    let file: URL
    let info: MediaInfo
    let frameStep: Double
    let player: AVPlayer?

    @Published var current: Double = 0
    @Published var isPlaying = false
    @Published var thumbnails: [CGImage] = []
    @Published var waveform: [Float] = []

    private var observer: Any?
    private var stopAt: Double?

    var duration: Double { info.duration }

    init(file: URL, info: MediaInfo, frameRate: Double) {
        self.file = file
        self.info = info
        frameStep = 1 / max(frameRate, 1)
        let playable = ["mov", "mp4", "m4v", "m4a", "mp3", "wav", "aiff", "aif", "caf", "flac", "aac"]
        player = playable.contains(file.pathExtension.lowercased()) ? AVPlayer(url: file) : nil
        observer = player?.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] time in
            MainActor.assumeIsolated { self?.tick(time.seconds) }
        }
    }

    /// Probes the file and reads its frame rate before building the model.
    static func load(_ file: URL) async -> MediaPlayerModel? {
        let info = await MediaProbe.info(file)
        guard info.duration > 0 else {
            HUDController.shared.show(L10n.t("Couldn't read the length of \(file.lastPathComponent)"), style: .failure)
            return nil
        }
        var frameRate = 30.0
        if let track = try? await AVURLAsset(url: file).loadTracks(withMediaType: .video).first,
           let rate = try? await track.load(.nominalFrameRate), rate > 0 {
            frameRate = Double(rate)
        }
        return MediaPlayerModel(file: file, info: info, frameRate: frameRate)
    }

    func stop() {
        player?.pause()
        if let observer { player?.removeTimeObserver(observer) }
        observer = nil
    }

    private func tick(_ seconds: Double) {
        guard seconds.isFinite else { return }
        current = seconds
        if let stopAt, seconds >= stopAt {
            player?.pause()
            isPlaying = false
            self.stopAt = nil
        }
        if isPlaying, player?.rate == 0 { isPlaying = false }
    }

    func seek(_ seconds: Double) {
        let time = min(max(0, seconds), duration)
        current = time
        player?.seek(to: CMTime(seconds: time, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }

    func step(_ frames: Int) {
        pause()
        seek(current + Double(frames) * frameStep)
    }

    func pause() {
        player?.pause()
        isPlaying = false
        stopAt = nil
    }

    /// Plays from `start` (or the current time) and stops at `end`, at `rate`.
    func play(from start: Double? = nil, to end: Double? = nil, rate: Float = 1) {
        guard let player else { return }
        if isPlaying { pause(); return }
        if let start { seek(start) } else if current >= duration - 0.05 { seek(0) }
        stopAt = end
        player.currentItem?.audioTimePitchAlgorithm = .spectral
        player.rate = rate
        isPlaying = true
    }

    func loadStrip(count: Int = 10) async {
        if info.hasVideo, player != nil {
            let generator = AVAssetImageGenerator(asset: AVURLAsset(url: file))
            generator.appliesPreferredTrackTransform = true
            generator.maximumSize = CGSize(width: 200, height: 200)
            var images: [CGImage] = []
            for index in 0..<count {
                let time = CMTime(seconds: duration * (Double(index) + 0.5) / Double(count), preferredTimescale: 600)
                if let (image, _) = try? await generator.image(at: time) { images.append(image) }
            }
            thumbnails = images
        }
        if info.hasAudio, !info.hasVideo {
            waveform = await MediaOps.waveform(file, bars: 240)
        }
    }

    /// A still at `seconds`, for frame pickers.
    func frame(at seconds: Double, maxSize: CGFloat = 400) async -> CGImage? {
        let generator = AVAssetImageGenerator(asset: AVURLAsset(url: file))
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: maxSize, height: maxSize)
        generator.requestedTimeToleranceBefore = .zero
        generator.requestedTimeToleranceAfter = .zero
        return try? await generator.image(at: CMTime(seconds: seconds, preferredTimescale: 600)).image
    }
}

/// Thumbnails or a waveform across the whole file, with a playhead; clicking seeks.
struct TimelineStrip<Overlay: View>: View {
    @ObservedObject var media: MediaPlayerModel
    var height: CGFloat = 56
    @ViewBuilder var overlay: (CGFloat) -> Overlay

    var body: some View {
        GeometryReader { geo in
            let width = geo.size.width
            ZStack(alignment: .leading) {
                strip(width: width, height: geo.size.height)
                    .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                overlay(width)
                Rectangle().fill(Theme.ink.opacity(0.85)).frame(width: 1.5)
                    .offset(x: CGFloat(media.current / max(media.duration, 0.001)) * width)
                    .allowsHitTesting(false)
            }
            .contentShape(Rectangle())
            .onTapGesture { location in media.seek(Double(location.x / width) * media.duration) }
        }
        .frame(height: height)
        // Time runs left to right in every language.
        .environment(\.layoutDirection, .leftToRight)
    }

    @ViewBuilder
    private func strip(width: CGFloat, height: CGFloat) -> some View {
        if !media.thumbnails.isEmpty {
            HStack(spacing: 0) {
                ForEach(Array(media.thumbnails.enumerated()), id: \.offset) { _, image in
                    Image(decorative: image, scale: 1).resizable().aspectRatio(contentMode: .fill)
                        .frame(width: width / CGFloat(media.thumbnails.count), height: height).clipped()
                }
            }
            .frame(width: width, height: height)
            .background(Color.black.opacity(0.8))
        } else if !media.waveform.isEmpty {
            WaveformShape(levels: media.waveform)
                .fill(Theme.accent.opacity(0.85))
                .background(Theme.accent.opacity(0.07))
        } else {
            Color.black.opacity(0.08)
        }
    }
}

/// Vertical bars for peak levels, centered on the midline.
struct WaveformShape: Shape {
    let levels: [Float]

    func path(in rect: CGRect) -> Path {
        var path = Path()
        guard !levels.isEmpty else { return path }
        let step = rect.width / CGFloat(levels.count)
        for (index, level) in levels.enumerated() {
            let h = max(1.5, CGFloat(level) * rect.height * 0.85)
            path.addRect(CGRect(x: rect.minX + CGFloat(index) * step, y: rect.midY - h / 2, width: max(1, step * 0.55), height: h))
        }
        return path
    }
}

/// ◁| ▶ |▷ plus "current / total".
struct TransportBar: View {
    @ObservedObject var media: MediaPlayerModel
    var playAction: (() -> Void)?

    var body: some View {
        HStack(spacing: 14) {
            button("backward.frame") { media.step(-1) }.help(L10n.t("Previous frame"))
            button(media.isPlaying ? "pause.fill" : "play.fill") { (playAction ?? { media.play() })() }.help(L10n.t("Play"))
            button("forward.frame") { media.step(1) }.help(L10n.t("Next frame"))
            Spacer()
            Text(L10n.t("\(TimeText.string(media.current)) / \(TimeText.string(media.duration))"))
                .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
        }
    }

    private func button(_ symbol: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 13, weight: .medium)).frame(width: 24, height: 22)
        }
        .buttonStyle(.plain)
        .disabled(media.player == nil)
    }
}

/// The black, rounded video area used by every video window.
struct VideoStage: View {
    @ObservedObject var media: MediaPlayerModel
    var height: CGFloat = 300

    var body: some View {
        PlayerSurface(player: media.player)
            .frame(height: height)
            .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

/// Opens a window for one media file after probing it, and stops playback when it closes.
@MainActor
enum MediaWindows {
    private(set) static var lastWindow: NSWindow?
    private static var models: [ObjectIdentifier: MediaPlayerModel] = [:]
    private static let delegate = Delegate()

    static func open<Content: View>(_ file: URL?, title: String, width: CGFloat, show: Bool,
                                    content: @escaping (MediaPlayerModel, ToolWindowHandle) -> Content) {
        guard let file else { return }
        Task { @MainActor in
            guard let media = await MediaPlayerModel.load(file) else { return }
            let window = ToolWindows.present(title, width: width, show: show) { handle in content(media, handle) }
            window.delegate = delegate
            models[ObjectIdentifier(window)] = media
            lastWindow = window
            await media.loadStrip()
        }
    }

    private final class Delegate: NSObject, NSWindowDelegate {
        func windowWillClose(_ notification: Notification) {
            guard let window = notification.object as? NSWindow else { return }
            MainActor.assumeIsolated { MediaWindows.models.removeValue(forKey: ObjectIdentifier(window))?.stop() }
        }
    }
}
