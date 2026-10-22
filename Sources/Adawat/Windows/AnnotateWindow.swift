import AppKit
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class AnnotateModel: ObservableObject {
    let file: URL
    let preview: CGImage
    /// Width over height of the picture.
    let aspect: CGFloat

    @Published var annotations: [Annotation] = []
    @Published var tool: AnnotationTool = .arrow
    @Published var color = AnnotationColor.palette[0]
    @Published var selection: UUID?

    private var history: [[Annotation]] = []
    private var drag: (index: Int, original: Annotation, start: CGPoint)?
    private var creating = false

    init(file: URL) throws {
        self.file = file
        preview = try ImageFile(file).upright(maxPixels: 1400)
        aspect = CGFloat(preview.width) / CGFloat(max(preview.height, 1))
    }

    var selectedText: String? {
        guard let selection, let item = annotations.first(where: { $0.id == selection }),
              case .text(_, let string) = item.kind else { return nil }
        return string
    }

    func setSelectedText(_ text: String) {
        guard let index = annotations.firstIndex(where: { $0.id == selection }),
              case .text(let origin, _) = annotations[index].kind else { return }
        annotations[index].kind = .text(origin: origin, string: text)
    }

    func setColor(_ color: AnnotationColor) {
        self.color = color
        if let index = annotations.firstIndex(where: { $0.id == selection }) {
            checkpoint()
            annotations[index].color = color
        }
    }

    private func checkpoint() {
        history.append(annotations)
        if history.count > 60 { history.removeFirst() }
    }

    var canUndo: Bool { !history.isEmpty }

    func undo() {
        guard let previous = history.popLast() else { return }
        annotations = previous
        selection = nil
    }

    func deleteSelected() {
        guard let selection else { return }
        checkpoint()
        annotations.removeAll { $0.id == selection }
        self.selection = nil
    }

    func reset() {
        checkpoint()
        annotations = []
        selection = nil
    }

    private var nextCalloutNumber: Int {
        annotations.reduce(0) { number, item in
            if case .callout(_, let n) = item.kind { return max(number, n) }
            return number
        } + 1
    }

    private func clamp(_ p: CGPoint) -> CGPoint { CGPoint(x: min(max(p.x, 0), 1), y: min(max(p.y, 0), 1)) }

    private func rect(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }

    /// A drag on the picture: picks up an existing mark, or starts a new one with the current tool.
    func dragChanged(start: CGPoint, current: CGPoint) {
        let start = clamp(start), current = clamp(current)
        if drag == nil, !creating {
            if tool != .draw, let index = annotations.lastIndex(where: { $0.bounds(aspect: aspect).contains(start) }) {
                checkpoint()
                drag = (index, annotations[index], start)
                selection = annotations[index].id
            } else {
                checkpoint()
                creating = true
                let kind: Annotation.Kind? = switch tool {
                case .arrow: .arrow(from: start, to: current)
                case .highlight: .highlight(rect(start, current))
                case .rectangle: .rectangle(rect(start, current))
                case .ellipse: .ellipse(rect(start, current))
                case .draw: .draw([start])
                case .magnifier: .magnifier(center: start, radius: 0.08)
                case .text, .callout, .image: nil
                }
                if let kind {
                    let item = Annotation(kind: kind, color: color)
                    annotations.append(item)
                    selection = item.id
                } else {
                    creating = false
                    history.removeLast()
                }
            }
        }
        if let drag {
            annotations[drag.index] = drag.original.moved(by: CGSize(width: current.x - drag.start.x, height: current.y - drag.start.y))
            return
        }
        guard creating, let index = annotations.indices.last else { return }
        switch annotations[index].kind {
        case .arrow(let from, _): annotations[index].kind = .arrow(from: from, to: current)
        case .highlight: annotations[index].kind = .highlight(rect(start, current))
        case .rectangle: annotations[index].kind = .rectangle(rect(start, current))
        case .ellipse: annotations[index].kind = .ellipse(rect(start, current))
        case .draw(let points): annotations[index].kind = .draw(points + [current])
        case .magnifier(let center, _):
            let dx = current.x - center.x, dy = (current.y - center.y) / aspect
            annotations[index].kind = .magnifier(center: center, radius: max(0.04, min(0.3, hypot(dx, dy))))
        default: break
        }
    }

    func dragEnded(start: CGPoint, end: CGPoint) {
        defer {
            drag = nil
            creating = false
        }
        if drag != nil { return }
        let tap = hypot(end.x - start.x, end.y - start.y) < 0.006
        if creating, let last = annotations.last {
            // Throw away accidental clicks for shapes that need a drag.
            let tooSmall: Bool = switch last.kind {
            case .arrow, .highlight, .rectangle, .ellipse: tap
            case .draw(let points): points.count < 3
            default: false
            }
            if tooSmall {
                annotations.removeLast()
                history.removeLast()
            } else {
                return
            }
        }
        guard tap else { return }
        let point = clamp(start)
        if let index = annotations.lastIndex(where: { $0.bounds(aspect: aspect).contains(point) }) {
            selection = annotations[index].id
            return
        }
        switch tool {
        case .text:
            checkpoint()
            let item = Annotation(kind: .text(origin: point, string: "Text"), color: color)
            annotations.append(item)
            selection = item.id
        case .callout:
            checkpoint()
            let item = Annotation(kind: .callout(center: point, number: nextCalloutNumber), color: color)
            annotations.append(item)
            selection = item.id
        default:
            selection = nil
        }
    }

    /// Adds an image from the clipboard, or asks for a file when the clipboard has none.
    func addImage() {
        var image: CGImage?
        if let pasted = NSImage(pasteboard: .general) {
            image = pasted.cgImage(forProposedRect: nil, context: nil, hints: nil)
        } else {
            let panel = NSOpenPanel()
            panel.allowedContentTypes = [.image]
            if panel.runModal() == .OK, let url = panel.url { image = try? ImageFile(url).upright(maxPixels: 2000) }
        }
        guard let image else { return }
        checkpoint()
        let width: CGFloat = 0.35
        let height = width * CGFloat(image.height) / CGFloat(image.width) * aspect
        let item = Annotation(kind: .image(rect: CGRect(x: 0.5 - width / 2, y: 0.5 - height / 2, width: width, height: height), image: image),
                              color: color)
        annotations.append(item)
        selection = item.id
    }
}

struct AnnotateView: View {
    @ObservedObject var model: AnnotateModel
    let handle: ToolWindowHandle
    @State private var dragOrigin: CGPoint?

    private var displaySize: CGSize {
        let scale = min(480 / CGFloat(model.preview.width), 420 / CGFloat(model.preview.height))
        return CGSize(width: CGFloat(model.preview.width) * scale, height: CGFloat(model.preview.height) * scale)
    }

    var body: some View {
        ToolScaffold(title: L10n.t("Annotate Photo"), onClose: handle.close) {
            VStack(spacing: 10) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color(white: 0.1))
                    canvas
                }
                .frame(height: displaySize.height + 24)

                HStack(spacing: 4) {
                    ForEach(AnnotationTool.allCases) { tool in
                        Button {
                            if tool == .image { model.addImage() } else { model.tool = tool }
                        } label: {
                            Image(systemName: tool.symbol)
                                .font(.system(size: 13, weight: .medium))
                                .frame(width: 44, height: 28)
                                .background(RoundedRectangle(cornerRadius: 7, style: .continuous)
                                    .fill(model.tool == tool ? Theme.accent : Color.black.opacity(0.07)))
                                .foregroundStyle(model.tool == tool ? Color.white : Theme.ink)
                        }
                        .buttonStyle(.plain)
                        .help(L10n.t(tool.title))
                    }
                }
                HStack(spacing: 6) {
                    ForEach(AnnotationColor.palette, id: \.self) { color in
                        Button { model.setColor(color) } label: {
                            Circle()
                                .fill(Color(cgColor: color.cgColor))
                                .overlay(Circle().strokeBorder(Color.black.opacity(0.18)))
                                .overlay(Circle().strokeBorder(Theme.accent, lineWidth: model.color == color ? 2.5 : 0).padding(-3))
                                .frame(width: 18, height: 18)
                        }
                        .buttonStyle(.plain)
                    }
                    Spacer()
                    if let text = model.selectedText {
                        TextField(L10n.t("Text"), text: Binding(get: { text }, set: model.setSelectedText))
                            .textFieldStyle(.roundedBorder).frame(width: 150)
                    }
                    Button(action: model.undo) { Image(systemName: "arrow.uturn.backward") }
                        .buttonStyle(.tool).disabled(!model.canUndo).help(L10n.t("Undo last annotation"))
                    Button(action: model.deleteSelected) { Image(systemName: "trash") }
                        .buttonStyle(.tool).disabled(model.selection == nil).help(L10n.t("Delete selected annotation"))
                        .keyboardShortcut(.delete, modifiers: [])
                }
                Text(L10n.t(model.tool.hint)).font(.system(size: 11)).foregroundStyle(Theme.muted)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        } leading: {
            Button(L10n.t("Reset"), action: model.reset).buttonStyle(.tool).disabled(model.annotations.isEmpty)
        } trailing: {
            Button(L10n.t("Save Annotated Copy"), action: save)
                .buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(model.annotations.isEmpty)
        }
        .onPasteCommand(of: [.image, .png, .tiff, .jpeg]) { _ in model.addImage() }
    }

    private var canvas: some View {
        let size = displaySize
        return ZStack(alignment: .topLeading) {
            Image(decorative: model.preview, scale: 1).resizable().frame(width: size.width, height: size.height)
            Canvas { context, canvasSize in
                context.withCGContext { cg in
                    AnnotationRenderer.draw(model.annotations, in: cg, size: canvasSize, base: model.preview)
                }
                if let selection = model.selection, let item = model.annotations.first(where: { $0.id == selection }) {
                    let b = item.bounds(aspect: model.aspect)
                    let rect = CGRect(x: b.minX * canvasSize.width, y: b.minY * canvasSize.height,
                                      width: b.width * canvasSize.width, height: b.height * canvasSize.height)
                    context.stroke(Path(rect), with: .color(.white.opacity(0.9)), style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                }
            }
            .frame(width: size.width, height: size.height)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0)
                .onChanged { value in
                    model.dragChanged(start: normalized(value.startLocation, size), current: normalized(value.location, size))
                }
                .onEnded { value in
                    model.dragEnded(start: normalized(value.startLocation, size), end: normalized(value.location, size))
                })
        }
        .frame(width: size.width, height: size.height)
        .clipped()
        .environment(\.layoutDirection, .leftToRight)
    }

    private func normalized(_ point: CGPoint, _ size: CGSize) -> CGPoint {
        CGPoint(x: point.x / size.width, y: point.y / size.height)
    }

    private func save() {
        let annotations = model.annotations
        let file = model.file
        handle.close()
        OperationRunner.run("Annotate", on: [file]) { files, context in
            context.report("Saving annotated copy")
            let source = try ImageFile(files[0])
            guard let image = AnnotationRenderer.render(annotations, on: try source.upright()) else {
                throw AdawatError.message("Could not prepare the annotated image.")
            }
            let format = ImageEngine.editedFormat(for: files[0])
            let out = Output.url(for: files[0], extension: format.ext, suffix: "annotated")
            try await ImageEngine.saveDrawn(source, image: image, as: format, to: out, quality: 0.92)
            return [out]
        }
    }
}

@MainActor
enum AnnotateWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool, sample: Bool = false) -> NSWindow? {
        guard let file = files.first else { return nil }
        do {
            let model = try AnnotateModel(file: file)
            if sample { addSample(to: model) }
            return ToolWindows.present(L10n.t("Annotate Photo"), width: 520, show: show) { handle in
                AnnotateView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }

    /// A few marks of every kind, for snapshot checks of the renderer.
    static func addSample(to model: AnnotateModel) {
        let palette = AnnotationColor.palette
        model.annotations = [
            Annotation(kind: .arrow(from: CGPoint(x: 0.1, y: 0.15), to: CGPoint(x: 0.35, y: 0.4)), color: palette[0]),
            Annotation(kind: .highlight(CGRect(x: 0.45, y: 0.1, width: 0.3, height: 0.12)), color: palette[2]),
            Annotation(kind: .rectangle(CGRect(x: 0.55, y: 0.55, width: 0.3, height: 0.25)), color: palette[4]),
            Annotation(kind: .ellipse(CGRect(x: 0.08, y: 0.6, width: 0.22, height: 0.22)), color: palette[3]),
            Annotation(kind: .text(origin: CGPoint(x: 0.4, y: 0.42), string: "مرحبا Adawat"), color: palette[5]),
            Annotation(kind: .callout(center: CGPoint(x: 0.85, y: 0.2), number: 1), color: palette[1]),
            Annotation(kind: .magnifier(center: CGPoint(x: 0.42, y: 0.75), radius: 0.09), color: palette[5]),
            Annotation(kind: .draw((0..<30).map { CGPoint(x: 0.6 + Double($0) * 0.01, y: 0.35 + sin(Double($0) / 3) * 0.04) }),
                       color: palette[0]),
        ]
    }
}
