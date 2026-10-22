import AppKit
import AVFoundation
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Normalize Volume

struct NormalizeView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @State private var target: Double = -16
    @State private var range: Double = 11
    @State private var peak: Double = -1.5
    @State private var measured: MediaOps.Loudness?

    var body: some View {
        ToolScaffold(title: L10n.t("Normalize Volume"), onClose: close) {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Text(media.file.lastPathComponent).lineLimit(1).truncationMode(.middle)
                    Spacer()
                    Text(TimeText.string(media.duration)).monospacedDigit()
                }
                .font(.system(size: 11)).foregroundStyle(Theme.muted)
                TimelineStrip(media: media, height: 150) { _ in EmptyView() }
                HStack {
                    Button { media.play() } label: { Image(systemName: media.isPlaying ? "pause.fill" : "play.fill") }
                        .buttonStyle(.plain).disabled(media.player == nil)
                    Spacer()
                    Text(L10n.t("\(TimeText.string(media.current)) / \(TimeText.string(media.duration))"))
                        .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                }
                if let measured {
                    HStack(spacing: 6) {
                        Image(systemName: "waveform.badge.magnifyingglass").foregroundStyle(Theme.muted)
                        Text(L10n.t("Now: \(measured.i) LUFS · peak \(measured.tp) dBTP · range \(measured.lra) LU"))
                    }
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
                }
                slider("Target loudness", value: $target, range: -24...(-5), step: 1, unit: "LUFS")
                slider("Loudness range", value: $range, range: 1...20, step: 1, unit: "LU")
                slider("True peak limit", value: $peak, range: -9...0, step: 0.5, unit: "dBTP")
                Text("−16 LUFS is a balanced default for spoken audio and music. The true peak limit helps prevent clipping after encoding.")
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        } leading: {
            Button(L10n.t("Restore Defaults")) { target = -16; range = 11; peak = -1.5 }.buttonStyle(.tool)
        } trailing: {
            Button(L10n.t("Normalize Audio"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
        .task { measured = try? await MediaOps.measureLoudness(media.file, target: "I=-16:TP=-1.5:LRA=11") }
    }

    private func slider(_ title: String, value: Binding<Double>, range: ClosedRange<Double>, step: Double, unit: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(title).font(.system(size: 12))
                Spacer()
                Text("\(String(format: step < 1 ? "%.1f" : "%.0f", value.wrappedValue)) \(unit)")
                    .font(.system(size: 12)).monospacedDigit()
            }
            Slider(value: value, in: range, step: step).controlSize(.small)
        }
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func apply() {
        let (file, target, range, peak) = (media.file, target, range, peak)
        close()
        OperationRunner.run("Normalize Volume", on: [file]) { files, context in
            [try await MediaOps.normalize(files[0], target: target, range: range, peak: peak, context: context)]
        }
    }
}

@MainActor
enum NormalizeWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Normalize Volume"), width: 460, show: show) { NormalizeView(media: $0, handle: $1) }
    }
}

// MARK: - Audio Visualizer

struct VisualizerView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @State private var stillImage = false
    @State private var image: URL?
    @State private var preview: CGImage?
    @State private var shape: VisualizerShape = .landscape
    @State private var color = Theme.accent

    var body: some View {
        ToolScaffold(title: L10n.t("Audio Visualizer"), onClose: close) {
            VStack(alignment: .leading, spacing: 12) {
                ZStack {
                    Color.clear
                    ZStack {
                        Color.black
                        if stillImage, let preview {
                            Image(decorative: preview, scale: 1).resizable().aspectRatio(contentMode: .fit)
                        } else if !stillImage {
                            WaveformShape(levels: Array(media.waveform.prefix(90))).fill(color)
                                .frame(height: 120).padding(.horizontal, 18)
                        }
                    }
                    .aspectRatio(CGFloat(shape.size.width) / CGFloat(shape.size.height), contentMode: .fit)
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                }
                .frame(height: 300)
                ToolRow(title: L10n.t("Style")) {
                    Picker("", selection: $stillImage) {
                        Text(L10n.t("Waveform")).tag(false)
                        Text(L10n.t("Still image")).tag(true)
                    }
                    .labelsHidden().fixedSize()
                }
                if stillImage {
                    ToolRow(title: L10n.t("Image")) {
                        Button(image?.lastPathComponent ?? "Choose Image…", action: chooseImage).buttonStyle(.tool)
                    }
                } else {
                    ToolRow(title: L10n.t("Color")) { ColorPicker("", selection: $color, supportsOpacity: false).labelsHidden() }
                }
                ToolRow(title: L10n.t("Shape")) {
                    Picker("", selection: $shape) {
                        ForEach(VisualizerShape.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Create Visualization"), action: create).buttonStyle(.tool).keyboardShortcut(.defaultAction)
                .disabled(stillImage && image == nil)
        }
    }

    private func chooseImage() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.message = L10n.t("Choose an image for the visualization.")
        guard panel.runModal() == .OK, let url = panel.url else { return }
        image = url
        preview = try? ImageFile(url).upright(maxPixels: 800)
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func create() {
        let (file, image, shape) = (media.file, stillImage ? image : nil, shape)
        let rgb = RGB(NSColor(color).cgColor)
        close()
        OperationRunner.run("Create Visualization", on: [file]) { files, context in
            [try await MediaOps.visualize(files[0], image: image, shape: shape, color: rgb, context: context)]
        }
    }
}

@MainActor
enum VisualizerWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Audio Visualizer"), width: 460, show: show) { VisualizerView(media: $0, handle: $1) }
    }
}

// MARK: - Preview playback

/// Plays short renderings (one channel, the bleeped result) made just for listening in a window.
@MainActor
final class PreviewPlayer: ObservableObject {
    @Published var playing: String?
    private var player: AVAudioPlayer?
    private var task: Task<Void, Never>?

    func toggle(_ key: String, render: @escaping () async throws -> URL) {
        if playing == key {
            stop()
            return
        }
        stop()
        playing = key
        task = Task {
            do {
                let url = try await render()
                guard !Task.isCancelled, playing == key else { return }
                player = try AVAudioPlayer(contentsOf: url)
                player?.play()
                let length = player?.duration ?? 0
                try? await Task.sleep(nanoseconds: UInt64(length * 1_000_000_000))
                if playing == key { playing = nil }
            } catch {
                playing = nil
                HUDController.shared.show(L10n.t("Could not play the preview. \(error.localizedDescription)"), style: .failure)
            }
        }
    }

    func stop() {
        task?.cancel()
        player?.stop()
        player = nil
        playing = nil
    }
}

// MARK: - Convert Audio Channels

struct ChannelsView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @StateObject private var preview = PreviewPlayer()
    @State private var mono = false
    @State private var left: Double = 1
    @State private var right: Double = 1
    @State private var waves: [[Float]] = [[], []]

    var body: some View {
        ToolScaffold(title: L10n.t("Convert Audio Channels"), onClose: close) {
            VStack(alignment: .leading, spacing: 12) {
                Text(L10n.t("Output channel preview")).font(.system(size: 11)).foregroundStyle(Theme.muted)
                ForEach(0..<(media.info.audioChannels >= 2 ? 2 : 1), id: \.self) { channel in
                    HStack(spacing: 10) {
                        Text(channel == 0 ? (media.info.audioChannels >= 2 ? "L" : "M") : "R")
                            .font(.system(size: 11, weight: .semibold))
                            .frame(width: 24, height: 24).background(Circle().fill(Theme.accent.opacity(0.15)))
                        WaveformShape(levels: waves[channel]).fill(Color.black.opacity(0.35))
                            .frame(height: 44).background(RoundedRectangle(cornerRadius: 8).fill(Color.black.opacity(0.04)))
                        Button {
                            let file = media.file, info = media.info
                            preview.toggle("solo\(channel)") {
                                try await MediaOps.previewAudio(file, filter: MediaOps.channelFilter(info, mono: true, left: 1, right: 1, solo: channel),
                                                                complex: false)
                            }
                        } label: { Image(systemName: preview.playing == "solo\(channel)" ? "stop.fill" : "play.fill") }
                            .buttonStyle(.plain).help(L10n.t("Play this channel on its own"))
                    }
                }
                Divider()
                ToolRow(title: L10n.t("Output")) {
                    Picker("", selection: $mono) {
                        Text(L10n.t("Mono")).tag(true)
                        Text(L10n.t("Stereo")).tag(false)
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                }
                volume("Left volume", value: $left)
                volume("Right volume", value: $right)
                ToolRow(title: L10n.t("Preview")) {
                    Button {
                        let (file, info, mono, left, right) = (media.file, media.info, mono, left, right)
                        preview.toggle("output") {
                            try await MediaOps.previewAudio(file, filter: MediaOps.channelFilter(info, mono: mono, left: left, right: right),
                                                            complex: false)
                        }
                    } label: {
                        Label(preview.playing == "output" ? "Stop" : "Play Output", systemImage: preview.playing == "output" ? "stop.fill" : "play.fill")
                    }
                    .buttonStyle(.tool)
                }
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Apply"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
        .task {
            let file = media.file
            let stereo = media.info.audioChannels >= 2
            waves[0] = await MediaOps.waveform(file, bars: 160, channel: stereo ? 0 : nil)
            if stereo { waves[1] = await MediaOps.waveform(file, bars: 160, channel: 1) }
        }
    }

    private func volume(_ title: String, value: Binding<Double>) -> some View {
        HStack(spacing: 10) {
            Text(title).font(.system(size: 12)).frame(width: 96, alignment: .leading)
            Slider(value: value, in: 0...1.5).controlSize(.small)
            Text(L10n.t("\(Int((value.wrappedValue * 100).rounded()))%")).font(.system(size: 12)).monospacedDigit().frame(width: 44, alignment: .trailing)
        }
    }

    private func close() {
        preview.stop()
        media.stop()
        handle.close()
    }

    private func apply() {
        let (file, mono, left, right) = (media.file, mono, left, right)
        close()
        OperationRunner.run("Convert Audio Channels", on: [file]) { files, context in
            [try await MediaOps.convertChannels(files[0], mono: mono, left: left, right: right, context: context)]
        }
    }
}

@MainActor
enum ChannelsWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Convert Audio Channels"), width: 460, show: show) { ChannelsView(media: $0, handle: $1) }
    }
}

// MARK: - Bleep Audio

struct BleepView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @StateObject private var preview = PreviewPlayer()
    @State private var ranges: [ClosedRange<Double>] = []
    @State private var selected: Int?
    @State private var dragStart: (index: Int?, origin: ClosedRange<Double>?, mode: Int)?

    var body: some View {
        ToolScaffold(title: L10n.t("Bleep Audio"), onClose: close) {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 6) {
                    Text(L10n.t("Bleeps")).font(.system(size: 12, weight: .semibold))
                    Text(L10n.t("\(ranges.count)")).font(.system(size: 12)).foregroundStyle(Theme.muted)
                    Spacer()
                    Button(L10n.t("Add bleep at playhead")) { add(around: media.current) }.buttonStyle(.tool)
                    Button(L10n.t("Bleep Entire Audio")) { ranges = [0...media.duration]; selected = 0 }.buttonStyle(.tool)
                }
                HStack(alignment: .top, spacing: 8) {
                    VStack(alignment: .leading, spacing: 0) {
                        Text(L10n.t("Bleep")).frame(height: 34)
                        Text(L10n.t("Audio")).frame(height: 74)
                    }
                    .font(.system(size: 11)).foregroundStyle(Theme.muted).frame(width: 40, alignment: .leading)
                    VStack(spacing: 6) {
                        bleepLayer.frame(height: 30)
                        TimelineStrip(media: media, height: 70) { _ in EmptyView() }
                        HStack {
                            Text("0:00.00")
                            Spacer()
                            Text(TimeText.string(media.duration))
                        }
                        .font(.system(size: 10, design: .monospaced)).foregroundStyle(Theme.muted)
                    }
                }
                Text(L10n.t("Drag across the Bleep layer to add a section, then drag a section or either edge to adjust it. Delete removes the selected one."))
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
                Divider()
                ToolRow(title: L10n.t("Preview")) {
                    HStack(spacing: 6) {
                        Button(preview.playing == "original" ? "Stop" : "Original") {
                            preview.toggle("original") { media.file }
                        }
                        .buttonStyle(.tool)
                        Button {
                            let (file, ranges, duration) = (media.file, ranges, media.duration)
                            preview.toggle("bleeped") {
                                try await MediaOps.previewAudio(file, filter: MediaOps.bleepFilter(ranges, duration: duration), complex: true)
                            }
                        } label: {
                            Label(preview.playing == "bleeped" ? "Stop" : "With Bleeps",
                                  systemImage: preview.playing == "bleeped" ? "stop.fill" : "play.fill")
                        }
                        .buttonStyle(.tool).disabled(ranges.isEmpty)
                    }
                }
            }
        } leading: {
            Button(L10n.t("Reset")) { ranges = []; selected = nil }.buttonStyle(.tool).disabled(ranges.isEmpty)
        } trailing: {
            Button(L10n.t("Save Bleeped Copy"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(ranges.isEmpty)
        }
        .background {
            Button("") { if let selected { ranges.remove(at: selected); self.selected = nil } }
                .keyboardShortcut(.delete, modifiers: []).hidden()
        }
    }

    /// Orange sections over a light track. Dragging empty space adds one; dragging a section moves it;
    /// dragging near an edge resizes.
    private var bleepLayer: some View {
        GeometryReader { geo in
            let width = geo.size.width
            let scale = media.duration / Double(width)
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 7).fill(Color.black.opacity(0.05))
                ForEach(Array(ranges.enumerated()), id: \.offset) { index, range in
                    let x1 = CGFloat(range.lowerBound / scale), x2 = CGFloat(range.upperBound / scale)
                    RoundedRectangle(cornerRadius: 6)
                        .fill(Theme.accent.opacity(selected == index ? 0.85 : 0.6))
                        .overlay(Text(L10n.t("Bleep")).font(.system(size: 10, weight: .semibold)).foregroundStyle(.white).lineLimit(1))
                        .overlay(alignment: .leading) { Capsule().fill(.white).frame(width: 2, height: 14).padding(.leading, 4) }
                        .overlay(alignment: .trailing) { Capsule().fill(.white).frame(width: 2, height: 14).padding(.trailing, 4) }
                        .frame(width: max(8, x2 - x1)).offset(x: x1)
                }
            }
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { value in drag(value, scale: scale) }
                .onEnded { _ in
                    if let index = dragStart?.index, ranges.indices.contains(index),
                       ranges[index].upperBound - ranges[index].lowerBound < 0.05 {
                        ranges.remove(at: index)
                        selected = nil
                    }
                    dragStart = nil
                })
        }
        .environment(\.layoutDirection, .leftToRight)
    }

    private func drag(_ value: DragGesture.Value, scale: Double) {
        let startTime = Double(value.startLocation.x) * scale
        let time = min(max(0, Double(value.location.x) * scale), media.duration)
        if dragStart == nil {
            let edge = 6 * scale
            if let index = ranges.firstIndex(where: { abs($0.lowerBound - startTime) < edge }) {
                dragStart = (index, ranges[index], 1)
            } else if let index = ranges.firstIndex(where: { abs($0.upperBound - startTime) < edge }) {
                dragStart = (index, ranges[index], 2)
            } else if let index = ranges.firstIndex(where: { $0.contains(startTime) }) {
                dragStart = (index, ranges[index], 0)
            } else {
                ranges.append(startTime...startTime)
                dragStart = (ranges.count - 1, nil, 3)
            }
            selected = dragStart?.index
        }
        guard let start = dragStart, let index = start.index, ranges.indices.contains(index) else { return }
        let delta = time - startTime
        switch start.mode {
        case 0:
            let origin = start.origin!
            let length = origin.upperBound - origin.lowerBound
            let lower = min(max(0, origin.lowerBound + delta), media.duration - length)
            ranges[index] = lower...(lower + length)
        case 1:
            ranges[index] = min(time, start.origin!.upperBound - 0.05)...start.origin!.upperBound
        case 2:
            ranges[index] = start.origin!.lowerBound...max(time, start.origin!.lowerBound + 0.05)
        default:
            ranges[index] = min(startTime, time)...max(startTime, time)
        }
    }

    private func add(around time: Double) {
        let lower = max(0, time - 0.25), upper = min(media.duration, time + 0.25)
        ranges.append(lower...upper)
        selected = ranges.count - 1
    }

    private func close() {
        preview.stop()
        media.stop()
        handle.close()
    }

    private func save() {
        let (file, ranges) = (media.file, ranges)
        close()
        OperationRunner.run("Bleep Audio", on: [file]) { files, context in
            [try await MediaOps.bleep(files[0], ranges: ranges, context: context)]
        }
    }
}

@MainActor
enum BleepWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Bleep Audio"), width: 520, show: show) { BleepView(media: $0, handle: $1) }
    }
}
