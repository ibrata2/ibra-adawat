import AppKit
import SwiftUI

// MARK: - Split Video

struct SplitView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @State private var points: [Double] = []
    @State private var removed: Set<Int> = []
    @State private var selectedClip: Int?
    @State private var equalClips = 2
    @State private var joinKept = true

    private var bounds: [Double] { [0] + points.sorted() + [media.duration] }
    private var clipCount: Int { bounds.count - 1 }

    var body: some View {
        ToolScaffold(title: L10n.t("Split Video"), onClose: close) {
            VStack(alignment: .leading, spacing: 10) {
                VideoStage(media: media, height: 280)
                HStack {
                    Text("\(points.count) split point\(points.count == 1 ? "" : "s")").font(.system(size: 11)).foregroundStyle(Theme.muted)
                    Spacer()
                    Button { addSplit(at: media.current) } label: { Label(L10n.t("Add Split"), systemImage: "scissors") }
                        .buttonStyle(.tool).disabled(!canSplit(at: media.current))
                }
                TimelineStrip(media: media, height: 56) { width in
                    ZStack(alignment: .leading) {
                        ForEach(0..<clipCount, id: \.self) { index in
                            let x1 = CGFloat(bounds[index] / media.duration) * width
                            let x2 = CGFloat(bounds[index + 1] / media.duration) * width
                            Rectangle()
                                .fill(removed.contains(index) ? Color.white.opacity(0.75) : Color.clear)
                                .overlay(RoundedRectangle(cornerRadius: 6)
                                    .strokeBorder(selectedClip == index ? Theme.accent : .clear, lineWidth: 3))
                                .frame(width: max(2, x2 - x1))
                                .offset(x: x1)
                                .contentShape(Rectangle())
                                .onTapGesture { selectedClip = index; media.seek(bounds[index]) }
                        }
                        ForEach(points.sorted(), id: \.self) { point in
                            Capsule().fill(Color.white).frame(width: 3)
                                .overlay(Capsule().strokeBorder(Theme.accent, lineWidth: 1))
                                .offset(x: CGFloat(point / media.duration) * width - 1.5)
                                .allowsHitTesting(false)
                        }
                    }
                }
                TransportBar(media: media)
                Divider()
                HStack {
                    Text(L10n.t("Equal clips")).font(.system(size: 12))
                    Spacer()
                    Stepper("\(equalClips)", value: $equalClips, in: 2...50).monospacedDigit()
                    Button(L10n.t("Set")) { setEqual() }.buttonStyle(.tool)
                }
                if !removed.isEmpty {
                    Picker("", selection: $joinKept) {
                        Text(L10n.t("Join the rest into one video")).tag(true)
                        Text(L10n.t("Export separate clips")).tag(false)
                    }
                    .pickerStyle(.segmented).labelsHidden()
                }
                Text(selectedClip == nil
                     ? "Click a clip in the timeline, then press Delete to remove it. Space plays the video."
                     : removed.contains(selectedClip!) ? "Press Delete again to keep this clip." : "Press Delete to remove this clip.")
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        } leading: {
            Button(L10n.t("Reset")) { points = []; removed = []; selectedClip = nil }.buttonStyle(.tool).disabled(points.isEmpty)
        } trailing: {
            Button(L10n.t("Split Video"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
                .disabled(points.isEmpty || removed.count == clipCount)
        }
        .background {
            Button("") { toggleSelected() }.keyboardShortcut(.delete, modifiers: []).hidden()
            Button("") { media.play() }.keyboardShortcut(.space, modifiers: []).hidden()
        }
    }

    private func canSplit(at time: Double) -> Bool {
        time > 0.05 && time < media.duration - 0.05 && points.allSatisfy { abs($0 - time) >= 0.05 }
    }

    private func addSplit(at time: Double) {
        guard canSplit(at: time) else { return }
        points.append(time)
        removed = []
        selectedClip = nil
    }

    private func setEqual() {
        points = (1..<equalClips).map { media.duration * Double($0) / Double(equalClips) }
        removed = []
        selectedClip = nil
    }

    private func toggleSelected() {
        guard let clip = selectedClip else { return }
        if removed.contains(clip) { removed.remove(clip) } else { removed.insert(clip) }
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func apply() {
        let (file, points, removed, join) = (media.file, points, removed, joinKept && !removed.isEmpty)
        close()
        OperationRunner.run("Split Video", on: [file]) { files, context in
            try await MediaOps.split(files[0], points: points, removed: removed, joinKept: join, context: context)
        }
    }
}

@MainActor
enum SplitWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Split Video"), width: 560, show: show) { SplitView(media: $0, handle: $1) }
    }
}

// MARK: - Save Video Frames

struct FramesView: View {
    @ObservedObject var media: MediaPlayerModel
    let handle: ToolWindowHandle
    @State private var frames: [(time: Double, image: CGImage?)] = []

    var body: some View {
        ToolScaffold(title: L10n.t("Video Snapshots"), onClose: close) {
            VStack(alignment: .leading, spacing: 10) {
                VideoStage(media: media, height: 300)
                TimelineStrip(media: media, height: 44) { _ in EmptyView() }
                TransportBar(media: media)
                Divider()
                HStack {
                    Text(L10n.t("\(frames.count) selected")).font(.system(size: 11)).foregroundStyle(Theme.muted)
                    Spacer()
                    Button(L10n.t("Add Frame")) { Task { await addFrame() } }.buttonStyle(.tool)
                        .disabled(frames.contains { abs($0.time - media.current) < media.frameStep / 2 })
                }
                ScrollView(.horizontal) {
                    HStack(spacing: 8) {
                        ForEach(Array(frames.enumerated()), id: \.offset) { index, frame in
                            ZStack(alignment: .topTrailing) {
                                Group {
                                    if let image = frame.image {
                                        Image(decorative: image, scale: 1).resizable().aspectRatio(contentMode: .fill)
                                    } else {
                                        Color.black.opacity(0.7)
                                    }
                                }
                                .frame(width: 96, height: 60).clipped()
                                .clipShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
                                .overlay(alignment: .bottomLeading) {
                                    Text(TimeText.string(frame.time)).font(.system(size: 9, weight: .semibold)).monospacedDigit()
                                        .padding(.horizontal, 5).padding(.vertical, 2)
                                        .background(Capsule().fill(.black.opacity(0.6))).foregroundStyle(.white).padding(4)
                                }
                                .onTapGesture { media.seek(frame.time) }
                                Button { frames.remove(at: index) } label: {
                                    Image(systemName: "xmark").font(.system(size: 8, weight: .bold)).frame(width: 16, height: 16)
                                        .background(Circle().fill(.black.opacity(0.65))).foregroundStyle(.white)
                                }
                                .buttonStyle(.plain).padding(4).help(L10n.t("Remove selected frame"))
                            }
                        }
                    }
                }
                .frame(height: frames.isEmpty ? 0 : 64)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(frames.count == 1 ? "Save 1 Frame" : "Save \(frames.count) Frames", action: save)
                .buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(frames.isEmpty)
        }
    }

    private func addFrame() async {
        let time = media.current
        let image = await media.frame(at: time, maxSize: 200)
        frames.append((time, image))
        frames.sort { $0.time < $1.time }
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func save() {
        let (file, times) = (media.file, frames.map(\.time))
        close()
        OperationRunner.run("Save Frames", on: [file]) { files, context in
            try await MediaOps.saveFrames(files[0], times: times, context: context)
        }
    }
}

@MainActor
enum FramesWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Video Snapshots"), width: 560, show: show) { FramesView(media: $0, handle: $1) }
    }
}
