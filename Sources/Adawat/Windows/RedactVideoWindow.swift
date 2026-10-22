import AppKit
import SwiftUI

@MainActor
final class VideoRedactModel: ObservableObject {
    @Published var boxes: [VideoRedaction] = []
    @Published var selection: UUID?
    @Published var blur = false
    @Published var color = Color.black

    private var drag: (index: Int, original: CGRect, mode: Mode)?
    private enum Mode { case move, resize(Int), create(CGPoint) }

    private func clamp(_ p: CGPoint) -> CGPoint { CGPoint(x: min(max(p.x, 0), 1), y: min(max(p.y, 0), 1)) }

    /// Boxes visible at `time`.
    func visible(at time: Double) -> [VideoRedaction] { boxes.filter { $0.start <= time && time <= $0.end } }

    func dragChanged(start: CGPoint, current: CGPoint, time: Double, duration: Double) {
        let start = clamp(start), current = clamp(current)
        if drag == nil {
            let shown = Set(visible(at: time).map(\.id))
            if let index = boxes.firstIndex(where: { $0.id == selection }), shown.contains(boxes[index].id),
               let corner = corner(of: boxes[index].rect, near: start) {
                drag = (index, boxes[index].rect, .resize(corner))
            } else if let index = boxes.lastIndex(where: { shown.contains($0.id) && $0.rect.contains(start) }) {
                drag = (index, boxes[index].rect, .move)
                selection = boxes[index].id
            } else {
                // New boxes cover the whole video; shorten them on the timeline below.
                boxes.append(VideoRedaction(rect: CGRect(origin: start, size: .zero), start: 0, end: duration))
                selection = boxes.last?.id
                drag = (boxes.count - 1, .zero, .create(start))
            }
        }
        guard let drag else { return }
        let dx = current.x - start.x, dy = current.y - start.y
        switch drag.mode {
        case .move:
            var r = drag.original.offsetBy(dx: dx, dy: dy)
            r.origin.x = min(max(r.minX, 0), 1 - r.width)
            r.origin.y = min(max(r.minY, 0), 1 - r.height)
            boxes[drag.index].rect = r
        case .create(let origin):
            boxes[drag.index].rect = CGRect(x: min(origin.x, current.x), y: min(origin.y, current.y),
                                            width: abs(current.x - origin.x), height: abs(current.y - origin.y))
        case .resize(let corner):
            var minX = drag.original.minX, maxX = drag.original.maxX, minY = drag.original.minY, maxY = drag.original.maxY
            if corner % 2 == 0 { minX = min(minX + dx, maxX - 0.01) } else { maxX = max(maxX + dx, minX + 0.01) }
            if corner < 2 { minY = min(minY + dy, maxY - 0.01) } else { maxY = max(maxY + dy, minY + 0.01) }
            boxes[drag.index].rect = CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
        }
    }

    func dragEnded() {
        if let drag, case .create = drag.mode, boxes[drag.index].rect.width < 0.01 || boxes[drag.index].rect.height < 0.01 {
            boxes.remove(at: drag.index)
            selection = nil
        }
        drag = nil
    }

    private func corner(of rect: CGRect, near point: CGPoint) -> Int? {
        let corners = [CGPoint(x: rect.minX, y: rect.minY), CGPoint(x: rect.maxX, y: rect.minY),
                       CGPoint(x: rect.minX, y: rect.maxY), CGPoint(x: rect.maxX, y: rect.maxY)]
        return corners.firstIndex { hypot($0.x - point.x, $0.y - point.y) < 0.03 }
    }

    func setRange(_ id: UUID, start: Double? = nil, end: Double? = nil, duration: Double) {
        guard let index = boxes.firstIndex(where: { $0.id == id }) else { return }
        if let start { boxes[index].start = min(max(0, start), boxes[index].end - 0.1) }
        if let end { boxes[index].end = max(min(duration, end), boxes[index].start + 0.1) }
    }

    func deleteSelected() {
        boxes.removeAll { $0.id == selection }
        selection = nil
    }

    var rgb: RGB { RGB(NSColor(color).cgColor) }
}

struct RedactVideoView: View {
    @ObservedObject var media: MediaPlayerModel
    @StateObject private var model = VideoRedactModel()
    let handle: ToolWindowHandle

    private let stageHeight: CGFloat = 290

    /// Width over height of the upright picture.
    private var aspect: CGFloat {
        if let first = media.thumbnails.first { return CGFloat(first.width) / CGFloat(max(first.height, 1)) }
        return CGFloat(max(media.info.width, 1)) / CGFloat(max(media.info.height, 1))
    }

    var body: some View {
        ToolScaffold(title: L10n.t("Redact Video"), onClose: close) {
            VStack(alignment: .leading, spacing: 10) {
                GeometryReader { geo in
                    let fit = fitted(in: geo.size)
                    ZStack(alignment: .topLeading) {
                        VideoStage(media: media, height: geo.size.height)
                        overlay(size: fit.size).offset(x: fit.minX, y: fit.minY)
                    }
                }
                .frame(height: stageHeight)
                HStack {
                    ToolRow(title: L10n.t("Style")) {
                        Picker("", selection: $model.blur) {
                            Text(L10n.t("Solid")).tag(false)
                            Text(L10n.t("Blur")).tag(true)
                        }
                        .pickerStyle(.segmented).labelsHidden().fixedSize()
                    }
                }
                if !model.blur {
                    ToolRow(title: L10n.t("Color")) { ColorPicker("", selection: $model.color, supportsOpacity: false).labelsHidden() }
                }
                Divider()
                HStack {
                    Text(L10n.t("Redactions")).font(.system(size: 12, weight: .semibold))
                    Spacer()
                    Button(L10n.t("Show for Entire Video")) {
                        if let id = model.selection { model.setRange(id, start: 0, end: media.duration, duration: media.duration) }
                    }
                    .buttonStyle(.tool).disabled(model.selection == nil)
                    Button(action: model.deleteSelected) { Image(systemName: "trash") }
                        .buttonStyle(.tool).disabled(model.selection == nil).keyboardShortcut(.delete, modifiers: [])
                        .help(L10n.t("Delete Redaction"))
                }
                if model.boxes.isEmpty {
                    Text(L10n.t("Draw rectangles on the video to cover areas. Each one gets its own time range here."))
                        .font(.system(size: 11)).foregroundStyle(Theme.muted)
                }
                ForEach(Array(model.boxes.enumerated()), id: \.element.id) { index, box in rangeRow(index: index, box: box) }
                TimelineStrip(media: media, height: 36) { _ in EmptyView() }
                TransportBar(media: media)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Save Redacted Copy"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(model.boxes.isEmpty)
        }
    }

    private func fitted(in size: CGSize) -> CGRect {
        let width = min(size.width, size.height * aspect)
        let height = width / aspect
        return CGRect(x: (size.width - width) / 2, y: (size.height - height) / 2, width: width, height: height)
    }

    private func overlay(size: CGSize) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(model.visible(at: media.current)) { box in
                let r = CGRect(x: box.rect.minX * size.width, y: box.rect.minY * size.height,
                               width: box.rect.width * size.width, height: box.rect.height * size.height)
                let selected = box.id == model.selection
                Rectangle()
                    .fill(model.blur ? Color.white.opacity(0.35) : Color(cgColor: model.rgb.cgColor))
                    .overlay(Rectangle().strokeBorder(selected ? Theme.accent : Color.white.opacity(0.6), lineWidth: selected ? 2 : 1))
                    .frame(width: r.width, height: r.height).offset(x: r.minX, y: r.minY)
                if selected {
                    ForEach(0..<4, id: \.self) { corner in
                        RoundedRectangle(cornerRadius: 2).fill(Theme.accent).frame(width: 9, height: 9)
                            .position(x: corner % 2 == 0 ? r.minX : r.maxX, y: corner < 2 ? r.minY : r.maxY)
                    }
                }
            }
        }
        .frame(width: size.width, height: size.height, alignment: .topLeading)
        .contentShape(Rectangle())
        .gesture(DragGesture(minimumDistance: 0)
            .onChanged { value in
                model.dragChanged(start: CGPoint(x: value.startLocation.x / size.width, y: value.startLocation.y / size.height),
                                  current: CGPoint(x: value.location.x / size.width, y: value.location.y / size.height),
                                  time: media.current, duration: media.duration)
            }
            .onEnded { _ in model.dragEnded() })
        .environment(\.layoutDirection, .leftToRight)
    }

    private func rangeRow(index: Int, box: VideoRedaction) -> some View {
        HStack(spacing: 8) {
            Text(L10n.t("\(index + 1)")).font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted).frame(width: 14)
            GeometryReader { geo in
                let width = geo.size.width
                let x1 = CGFloat(box.start / media.duration) * width, x2 = CGFloat(box.end / media.duration) * width
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 6).fill(Color.black.opacity(0.05))
                    RoundedRectangle(cornerRadius: 6)
                        .fill(Theme.accent.opacity(box.id == model.selection ? 0.75 : 0.45))
                        .overlay(Text(L10n.t("Box \(index + 1)")).font(.system(size: 10, weight: .semibold)).foregroundStyle(.white))
                        .frame(width: max(12, x2 - x1)).offset(x: x1)
                        .onTapGesture { model.selection = box.id; media.seek(box.start) }
                    edge(x: x1, space: box.id) { model.setRange(box.id, start: Double($0 / width) * media.duration, duration: media.duration) }
                    edge(x: x2 - 6, space: box.id) { model.setRange(box.id, end: Double($0 / width) * media.duration, duration: media.duration) }
                    Rectangle().fill(Theme.ink.opacity(0.7)).frame(width: 1)
                        .offset(x: CGFloat(media.current / media.duration) * width).allowsHitTesting(false)
                }
                .coordinateSpace(name: box.id)
            }
            .frame(height: 24)
            .environment(\.layoutDirection, .leftToRight)
        }
    }

    /// A range handle that follows the pointer within its row.
    private func edge(x: CGFloat, space: UUID, set: @escaping (CGFloat) -> Void) -> some View {
        Capsule().fill(Color.white).frame(width: 6, height: 16).offset(x: x)
            .gesture(DragGesture(minimumDistance: 0, coordinateSpace: .named(space))
                .onChanged { set($0.location.x) })
    }

    private func close() {
        media.stop()
        handle.close()
    }

    private func save() {
        let (file, boxes, blur, color) = (media.file, model.boxes, model.blur, model.rgb)
        close()
        OperationRunner.run("Redact Video", on: [file]) { files, context in
            [try await MediaOps.redactVideo(files[0], redactions: boxes, blur: blur, color: color, context: context)]
        }
    }
}

@MainActor
enum RedactVideoWindow {
    static func open(_ files: [URL]) { open(files, show: true) }
    static func open(_ files: [URL], show: Bool) {
        MediaWindows.open(files.first, title: L10n.t("Redact Video"), width: 560, show: show) { RedactVideoView(media: $0, handle: $1) }
    }
}
