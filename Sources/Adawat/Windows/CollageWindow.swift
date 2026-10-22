import AppKit
import SwiftUI

enum CollageStyle: String, CaseIterable, Identifiable {
    case grid = "Grid", row = "Row", column = "Column", featured = "Featured"
    var id: String { rawValue }
}

enum CollageCanvas: String, CaseIterable, Identifiable {
    case square = "Square · 1:1", portrait = "Portrait · 4:5", story = "Story · 9:16"
    case landscape = "Landscape · 16:9", classic = "Classic · 4:3"

    var id: String { rawValue }

    /// Width over height.
    var ratio: CGFloat {
        switch self {
        case .square: return 1
        case .portrait: return 4.0 / 5
        case .story: return 9.0 / 16
        case .landscape: return 16.0 / 9
        case .classic: return 4.0 / 3
        }
    }
}

struct CollageOptions: Equatable {
    var style: CollageStyle = .grid
    var columns = 2
    var canvas: CollageCanvas = .square
    var width = 2048
    var fill = true
    var spacing: Double = 16
    var corners: Double = 0
    var background = RGB(1, 1, 1)
}

/// Lays several photos out on one canvas.
enum CollageRenderer {
    static func cells(count: Int, options: CollageOptions, size: CGSize, spacing: CGFloat) -> [CGRect] {
        guard count > 0 else { return [] }
        func grid(columns: Int, rows: Int, in area: CGRect, count: Int) -> [CGRect] {
            let w = (area.width - spacing * CGFloat(columns + 1)) / CGFloat(columns)
            let h = (area.height - spacing * CGFloat(rows + 1)) / CGFloat(rows)
            return (0..<count).map { index in
                let column = index % columns, row = index / columns
                return CGRect(x: area.minX + spacing + CGFloat(column) * (w + spacing),
                              y: area.minY + spacing + CGFloat(row) * (h + spacing), width: w, height: h)
            }
        }
        let all = CGRect(origin: .zero, size: size)
        switch options.style {
        case .grid:
            let columns = min(max(1, options.columns), count)
            return grid(columns: columns, rows: Int(ceil(Double(count) / Double(columns))), in: all, count: count)
        case .row:
            return grid(columns: count, rows: 1, in: all, count: count)
        case .column:
            return grid(columns: 1, rows: count, in: all, count: count)
        case .featured:
            guard count > 1 else { return grid(columns: 1, rows: 1, in: all, count: 1) }
            // The first photo takes the top three fifths; the rest share a row underneath.
            let split = size.height * 0.6
            let top = grid(columns: 1, rows: 1, in: CGRect(x: 0, y: 0, width: size.width, height: split + spacing / 2), count: 1)
            let bottom = grid(columns: count - 1, rows: 1,
                              in: CGRect(x: 0, y: split - spacing / 2, width: size.width, height: size.height - split + spacing / 2),
                              count: count - 1)
            return top + bottom
        }
    }

    /// `scale` shrinks the canvas and every pixel measure for previews.
    static func render(_ images: [CGImage], options: CollageOptions, scale: CGFloat = 1) throws -> CGImage {
        let width = CGFloat(options.width) * scale
        let size = CGSize(width: width.rounded(), height: (width / options.canvas.ratio).rounded())
        let space = CGColorSpace(name: CGColorSpace.displayP3)!
        guard let ctx = CGContext(data: nil, width: Int(size.width), height: Int(size.height), bitsPerComponent: 8,
                                  bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
            throw AdawatError.message("Could not create the collage canvas.")
        }
        ctx.setFillColor(options.background.cgColor)
        ctx.fill(CGRect(origin: .zero, size: size))
        // Cells are laid out top-down; CGContext counts from the bottom.
        ctx.translateBy(x: 0, y: size.height)
        ctx.scaleBy(x: 1, y: -1)
        let spacing = CGFloat(options.spacing) * scale
        let radius = CGFloat(options.corners) * scale
        for (image, cell) in zip(images, cells(count: images.count, options: options, size: size, spacing: spacing)) {
            ctx.saveGState()
            ctx.addPath(CGPath(roundedRect: cell, cornerWidth: min(radius, cell.width / 2), cornerHeight: min(radius, cell.height / 2),
                               transform: nil))
            ctx.clip()
            let imageSize = CGSize(width: image.width, height: image.height)
            let fit = options.fill
                ? max(cell.width / imageSize.width, cell.height / imageSize.height)
                : min(cell.width / imageSize.width, cell.height / imageSize.height)
            let drawn = CGSize(width: imageSize.width * fit, height: imageSize.height * fit)
            let rect = CGRect(x: cell.midX - drawn.width / 2, y: cell.midY - drawn.height / 2, width: drawn.width, height: drawn.height)
            // Flip each photo back upright inside the top-down context.
            ctx.translateBy(x: rect.minX, y: rect.maxY)
            ctx.scaleBy(x: 1, y: -1)
            ctx.interpolationQuality = .high
            ctx.draw(image, in: CGRect(origin: .zero, size: drawn))
            ctx.restoreGState()
        }
        guard let image = ctx.makeImage() else { throw AdawatError.message("Could not render the collage.") }
        return image
    }
}

@MainActor
final class CollageModel: ObservableObject {
    @Published var files: [URL]
    @Published var options = CollageOptions()
    @Published var preview: CGImage?
    @Published var backgroundColor = Color.white
    private var thumbnails: [URL: CGImage] = [:]

    init(files: [URL]) throws {
        self.files = files
        for url in files { thumbnails[url] = try ImageFile(url).upright(maxPixels: 700) }
        options.columns = files.count <= 4 ? 2 : 3
    }

    var renderKey: String { "\(files)\(options)" }

    func render() async {
        try? await Task.sleep(nanoseconds: 25_000_000)
        guard !Task.isCancelled else { return }
        preview = try? CollageRenderer.render(files.compactMap { thumbnails[$0] }, options: options,
                                              scale: 520 / CGFloat(options.width))
    }

    func move(_ index: Int, by offset: Int) {
        guard files.indices.contains(index), files.indices.contains(index + offset) else { return }
        files.swapAt(index, index + offset)
    }
}

struct CollageView: View {
    @ObservedObject var model: CollageModel
    let handle: ToolWindowHandle
    @State private var showSpacing = false
    @State private var showOrder = false

    var body: some View {
        ToolScaffold(title: L10n.t("Create Collage"), onClose: handle.close) {
            VStack(alignment: .leading, spacing: 10) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.black.opacity(0.05))
                    if let preview = model.preview {
                        Image(decorative: preview, scale: 1).resizable().aspectRatio(contentMode: .fit)
                            .shadow(color: .black.opacity(0.12), radius: 4, y: 2).padding(12)
                    }
                }
                .frame(height: 280)

                ToolRow(title: L10n.t("Style")) {
                    Picker("", selection: $model.options.style) {
                        ForEach(CollageStyle.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                if model.options.style == .grid {
                    ToolRow(title: L10n.t("Columns")) {
                        Stepper("\(model.options.columns)", value: $model.options.columns, in: 1...max(1, model.files.count))
                            .monospacedDigit()
                    }
                }
                ToolRow(title: L10n.t("Canvas")) {
                    Picker("", selection: $model.options.canvas) {
                        ForEach(CollageCanvas.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                ToolRow(title: L10n.t("Width")) {
                    Picker("", selection: $model.options.width) {
                        ForEach([1080, 1536, 2048, 3072, 4096], id: \.self) { Text(L10n.t("\($0.formatted()) px")).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                ToolRow(title: L10n.t("Image fit")) {
                    Picker("", selection: $model.options.fill) {
                        Text(L10n.t("Fill cells (crop edges)")).tag(true)
                        Text(L10n.t("Fit whole images")).tag(false)
                    }
                    .labelsHidden().fixedSize()
                }
                Divider()
                DisclosureGroup(L10n.t("Spacing & background"), isExpanded: $showSpacing) {
                    VStack(spacing: 8) {
                        slider("Spacing", value: $model.options.spacing, range: 0...120)
                        slider("Corners", value: $model.options.corners, range: 0...120)
                        ToolRow(title: L10n.t("Background")) {
                            ColorPicker("", selection: $model.backgroundColor, supportsOpacity: false).labelsHidden()
                                .onChange(of: model.backgroundColor) { _, color in
                                    model.options.background = RGB(NSColor(color).cgColor)
                                }
                        }
                    }
                    .padding(.top, 6)
                }
                .font(.system(size: 12))
                DisclosureGroup(L10n.t("Image order"), isExpanded: $showOrder) {
                    VStack(spacing: 4) {
                        ForEach(Array(model.files.enumerated()), id: \.element) { index, url in
                            HStack {
                                Text(L10n.t("\(index + 1)")).monospacedDigit().foregroundStyle(Theme.muted).frame(width: 16)
                                Text(url.lastPathComponent).lineLimit(1).truncationMode(.middle)
                                Spacer()
                                Button { model.move(index, by: -1) } label: { Image(systemName: "chevron.up") }
                                    .buttonStyle(.plain).disabled(index == 0)
                                Button { model.move(index, by: 1) } label: { Image(systemName: "chevron.down") }
                                    .buttonStyle(.plain).disabled(index == model.files.count - 1)
                            }
                            .font(.system(size: 11))
                        }
                    }
                    .padding(.top, 6)
                }
                .font(.system(size: 12))
            }
        } leading: {
            Button(L10n.t("Reset Layout")) {
                model.options = CollageOptions(columns: model.files.count <= 4 ? 2 : 3)
                model.backgroundColor = .white
            }
            .buttonStyle(.tool)
        } trailing: {
            Button(L10n.t("Create Collage"), action: create).buttonStyle(.tool).keyboardShortcut(.defaultAction)
        }
        .task(id: model.renderKey) { await model.render() }
    }

    private func slider(_ title: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        HStack(spacing: 10) {
            Text(title).frame(width: 70, alignment: .leading)
            Slider(value: value, in: range).controlSize(.small)
            Text(L10n.t("\(Int(value.wrappedValue)) px")).monospacedDigit().foregroundStyle(Theme.muted).frame(width: 50, alignment: .trailing)
        }
    }

    private func create() {
        let (files, options) = (model.files, model.options)
        handle.close()
        OperationRunner.run("Create Collage", on: files) { files, context in
            context.report("Creating collage")
            // Each photo only needs enough pixels for its cell.
            let images = try files.map { try ImageFile($0).upright(maxPixels: options.width) }
            let collage = try CollageRenderer.render(images, options: options)
            let out = Output.url(named: "Collage.jpg", near: files[0])
            try await ImageEngine.saveDrawn(nil, image: collage, as: .jpeg, to: out, quality: 0.92)
            return [out]
        }
    }
}

@MainActor
enum CollageWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        do {
            let model = try CollageModel(files: files)
            return ToolWindows.present(L10n.t("Create Collage"), width: 460, show: show) { handle in
                CollageView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
