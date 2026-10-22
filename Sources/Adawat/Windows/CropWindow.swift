import AppKit
import SwiftUI

enum CropRatio: String, CaseIterable, Identifiable {
    case free = "Free", square = "1:1", wide = "16:9", tall = "9:16", classic = "4:3", portrait = "3:4"

    var id: String { rawValue }

    /// Width divided by height, or nil for a free crop.
    var value: CGFloat? {
        switch self {
        case .free: return nil
        case .square: return 1
        case .wide: return 16.0 / 9
        case .tall: return 9.0 / 16
        case .classic: return 4.0 / 3
        case .portrait: return 3.0 / 4
        }
    }
}

@MainActor
final class CropModel: ObservableObject {
    let files: [URL]
    @Published var preview: CGImage
    /// Upright pixel size of the first image or video frame; the crop is shown and typed in its pixels.
    let pixelWidth: CGFloat
    let pixelHeight: CGFloat

    /// Normalized (0…1), top-left origin.
    @Published var rect = CGRect(x: 0, y: 0, width: 1, height: 1)
    @Published var ratio: CropRatio = .free {
        didSet { applyRatio() }
    }
    var dragStart: CGRect?

    init(files: [URL], preview: CGImage, pixelWidth: CGFloat, pixelHeight: CGFloat) {
        self.files = files
        self.preview = preview
        self.pixelWidth = pixelWidth
        self.pixelHeight = pixelHeight
    }

    convenience init(files: [URL]) throws {
        let file = try ImageFile(files[0])
        self.init(files: files, preview: try file.upright(maxPixels: 1400),
                  pixelWidth: CGFloat(file.uprightSize.width), pixelHeight: CGFloat(file.uprightSize.height))
    }

    var widthPixels: Int {
        get { Int((rect.width * pixelWidth).rounded()) }
        set { setSize(width: CGFloat(newValue) / pixelWidth, height: nil) }
    }

    var heightPixels: Int {
        get { Int((rect.height * pixelHeight).rounded()) }
        set { setSize(width: nil, height: CGFloat(newValue) / pixelHeight) }
    }

    /// The normalized height that goes with `width` for the current ratio.
    private func height(forWidth width: CGFloat) -> CGFloat? {
        ratio.value.map { width * pixelWidth / ($0 * pixelHeight) }
    }

    private func setSize(width: CGFloat?, height: CGFloat?) {
        var w = width ?? rect.width
        var h = height ?? rect.height
        if let ratio = ratio.value {
            if width != nil { h = w * pixelWidth / (ratio * pixelHeight) } else { w = h * ratio * pixelHeight / pixelWidth }
        }
        let minimum = 2 / max(pixelWidth, pixelHeight)
        w = min(max(w, minimum), 1)
        h = min(max(h, minimum), 1)
        rect = centered(width: w, height: h, around: CGPoint(x: rect.midX, y: rect.midY))
    }

    private func centered(width: CGFloat, height: CGFloat, around center: CGPoint) -> CGRect {
        let x = min(max(center.x - width / 2, 0), 1 - width)
        let y = min(max(center.y - height / 2, 0), 1 - height)
        return CGRect(x: x, y: y, width: width, height: height)
    }

    /// The largest crop of the chosen shape that fits, centered on the current one.
    func applyRatio() {
        guard let value = ratio.value else { return }
        var w: CGFloat = 1
        var h = w * pixelWidth / (value * pixelHeight)
        if h > 1 { h = 1; w = h * value * pixelHeight / pixelWidth }
        rect = centered(width: w, height: h, around: CGPoint(x: rect.midX, y: rect.midY))
    }

    func reset() {
        ratio = .free
        rect = CGRect(x: 0, y: 0, width: 1, height: 1)
    }

    func move(by delta: CGSize, in size: CGSize) {
        guard let start = dragStart else { return }
        rect.origin.x = min(max(start.minX + delta.width / size.width, 0), 1 - start.width)
        rect.origin.y = min(max(start.minY + delta.height / size.height, 0), 1 - start.height)
    }

    /// Drags one handle. `edges` names the sides it moves: x -1 left / 1 right, y -1 top / 1 bottom, 0 none.
    func resize(edges: (x: Int, y: Int), by delta: CGSize, in size: CGSize) {
        guard let start = dragStart else { return }
        let minimum: CGFloat = 0.03
        var minX = start.minX, maxX = start.maxX, minY = start.minY, maxY = start.maxY
        let dx = delta.width / size.width, dy = delta.height / size.height
        if edges.x < 0 { minX = min(max(start.minX + dx, 0), start.maxX - minimum) }
        if edges.x > 0 { maxX = max(min(start.maxX + dx, 1), start.minX + minimum) }
        if edges.y < 0 { minY = min(max(start.minY + dy, 0), start.maxY - minimum) }
        if edges.y > 0 { maxY = max(min(start.maxY + dy, 1), start.minY + minimum) }

        if let value = ratio.value {
            // Keep the shape: height follows width, anchored at the opposite corner, shrinking to fit.
            var w = maxX - minX
            var h = w * pixelWidth / (value * pixelHeight)
            let room = edges.y < 0 ? start.maxY : 1 - start.minY
            if h > room { h = room; w = h * value * pixelHeight / pixelWidth }
            if edges.x < 0 { minX = maxX - w } else { maxX = minX + w }
            if edges.y < 0 { minY = maxY - h } else { maxY = minY + h }
        }
        rect = CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
    }
}

struct CropView: View {
    @ObservedObject var model: CropModel
    let handle: ToolWindowHandle

    private var displaySize: CGSize {
        let scale = min(460 / model.pixelWidth, 440 / model.pixelHeight)
        return CGSize(width: model.pixelWidth * scale, height: model.pixelHeight * scale)
    }

    var body: some View {
        ToolScaffold(title: L10n.t("Crop Image"), onClose: handle.close) {
            VStack(spacing: 14) {
                canvas
                    .frame(maxWidth: .infinity)
                ToolRow(title: L10n.t("Aspect ratio")) {
                    Picker("", selection: $model.ratio) {
                        ForEach(CropRatio.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                }
                HStack(spacing: 8) {
                    Text("W").foregroundStyle(Theme.muted)
                    TextField("", value: $model.widthPixels, format: .number.grouping(.never))
                        .textFieldStyle(.roundedBorder).frame(width: 70)
                    Text("H").foregroundStyle(Theme.muted)
                    TextField("", value: $model.heightPixels, format: .number.grouping(.never))
                        .textFieldStyle(.roundedBorder).frame(width: 70)
                    Text("px").foregroundStyle(Theme.muted)
                    Spacer()
                }
                .font(.system(size: 12))
                if model.files.count > 1 {
                    Text(L10n.t("Uses this crop, as the same share of each image, for all \(model.files.count) images."))
                        .font(.system(size: 11)).foregroundStyle(Theme.muted)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        } leading: {
            Button(L10n.t("Reset"), action: model.reset).buttonStyle(.tool)
        } trailing: {
            Button(L10n.t("Apply"), action: apply).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
    }

    private var canvas: some View {
        CropCanvas(model: model, size: displaySize)
    }

    private func apply() {
        let rect = model.rect
        handle.close()
        OperationRunner.run("Crop", on: model.files) { files, context in
            try await ImageOps.crop(files, rect: rect, context: context)
        }
    }
}

/// The picture with an orange crop frame, eight handles and the outside dimmed.
struct CropCanvas: View {
    @ObservedObject var model: CropModel
    let size: CGSize

    var body: some View {
        let size = self.size
        let r = CGRect(x: model.rect.minX * size.width, y: model.rect.minY * size.height,
                       width: model.rect.width * size.width, height: model.rect.height * size.height)
        return ZStack(alignment: .topLeading) {
            Image(decorative: model.preview, scale: 1)
                .resizable()
                .frame(width: size.width, height: size.height)
            // Dim everything outside the crop.
            Path { path in
                path.addRect(CGRect(origin: .zero, size: size))
                path.addRect(r)
            }
            .fill(Color.black.opacity(0.45), style: FillStyle(eoFill: true))
            .allowsHitTesting(false)
            Rectangle()
                .strokeBorder(Theme.accent, lineWidth: 2)
                .contentShape(Rectangle())
                .frame(width: r.width, height: r.height)
                .offset(x: r.minX, y: r.minY)
                .gesture(DragGesture(minimumDistance: 1)
                    .onChanged { value in
                        if model.dragStart == nil { model.dragStart = model.rect }
                        model.move(by: value.translation, in: size)
                    }
                    .onEnded { _ in model.dragStart = nil })
                .help(L10n.t("Crop selection. Drag to reposition."))
            ForEach(handles, id: \.id) { spot in
                let point = CGPoint(x: spot.x < 0 ? r.minX : spot.x > 0 ? r.maxX : r.midX,
                                    y: spot.y < 0 ? r.minY : spot.y > 0 ? r.maxY : r.midY)
                let corner = spot.x != 0 && spot.y != 0
                RoundedRectangle(cornerRadius: 2)
                    .fill(Theme.accent)
                    .frame(width: corner ? 11 : (spot.x == 0 ? 20 : 6), height: corner ? 11 : (spot.y == 0 ? 20 : 6))
                    .padding(6)
                    .contentShape(Rectangle())
                    .position(point)
                    .gesture(DragGesture(minimumDistance: 0)
                        .onChanged { value in
                            if model.dragStart == nil { model.dragStart = model.rect }
                            model.resize(edges: (spot.x, spot.y), by: value.translation, in: size)
                        }
                        .onEnded { _ in model.dragStart = nil })
            }
        }
        .frame(width: size.width, height: size.height)
        .environment(\.layoutDirection, .leftToRight)
    }

    private struct Spot { let id: Int; let x: Int; let y: Int }

    /// Corners always; edge handles only when the shape is free.
    private var handles: [Spot] {
        var spots = [Spot(id: 0, x: -1, y: -1), Spot(id: 1, x: 1, y: -1), Spot(id: 2, x: -1, y: 1), Spot(id: 3, x: 1, y: 1)]
        if model.ratio == .free {
            spots += [Spot(id: 4, x: 0, y: -1), Spot(id: 5, x: 0, y: 1), Spot(id: 6, x: -1, y: 0), Spot(id: 7, x: 1, y: 0)]
        }
        return spots
    }
}

@MainActor
enum CropWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        do {
            let model = try CropModel(files: files)
            return ToolWindows.present(L10n.t("Crop Image"), width: 500, show: show) { handle in
                CropView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
