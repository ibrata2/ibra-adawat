import AppKit
import Combine
import ImageIO
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class QRModel: ObservableObject {
    enum Kind: String, CaseIterable, Identifiable {
        case text, wifi
        var id: String { rawValue }
        var title: String { self == .text ? "Text or Link" : "Wi-Fi" }
    }

    enum FileFormat: String, CaseIterable, Identifiable {
        case png = "PNG", svg = "SVG"
        var id: String { rawValue }
    }

    let source: URL?
    @Published var kind: Kind = .text
    @Published var text: String
    @Published var network = ""
    @Published var password = ""
    @Published var security: QRContent.Security = .wpa
    @Published var hidden = false
    @Published var correction: QRMatrix.Correction = .medium
    @Published var module: QRDesign.ModuleShape = .square
    @Published var eye: QRDesign.EyeShape = .square
    @Published var foreground = Color.black {
        // Corners follow the code color until they are given their own.
        didSet { if !separateEyeColor { eyeColor = foreground } }
    }
    @Published var separateEyeColor = false { didSet { if !separateEyeColor { eyeColor = foreground } } }
    @Published var eyeColor = Color.black
    @Published var useGradient = false
    @Published var gradientEnd = Theme.accent
    @Published var background = Color.white
    @Published var transparent = false
    @Published var margin = 4.0
    @Published var logo: CGImage? {
        // A logo hides part of the code; the highest error correction lets phones read around it.
        didSet { if logo != nil, oldValue == nil { correction = .high } }
    }
    @Published var logoScale = 0.22
    @Published var plate: QRDesign.Plate = .rounded
    @Published var pixels = 1024
    @Published var format: FileFormat = .png

    let preview = QRPreview()
    private var watcher: AnyCancellable?

    init(source: URL?, text: String) {
        self.source = source
        self.text = text
        watcher = objectWillChange
            .debounce(for: .milliseconds(60), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.refresh() }
        refresh()
    }

    var payload: String {
        kind == .text ? text : network.isEmpty ? "" : QRContent.wifi(name: network, password: password, security: security, hidden: hidden)
    }

    var matrix: QRMatrix? { QRMatrix(payload, correction: correction) }

    var design: QRDesign {
        var design = QRDesign()
        design.module = module
        design.eye = eye
        design.foreground = NSColor(foreground)
        design.gradientEnd = useGradient ? NSColor(gradientEnd) : nil
        design.eyeColor = separateEyeColor ? NSColor(eyeColor) : design.foreground
        design.background = transparent ? nil : NSColor(background)
        design.margin = Int(margin)
        design.logo = logo
        design.logoScale = logoScale
        design.plate = plate
        return design
    }

    /// Why there is nothing to save yet, if there isn't.
    var problem: String? {
        if payload.isEmpty { return kind == .text ? "Type the text or link to encode." : "Enter the network name." }
        if matrix == nil { return "This is too long for a QR code. Shorten it or lower the error correction." }
        return nil
    }

    private func refresh() {
        preview.update(matrix: matrix, design: design, payload: payload)
    }

    func chooseLogo() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.message = L10n.t("Choose a logo for the middle of the code")
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let url = panel.url else { return }
        loadLogo(url)
    }

    func loadLogo(_ url: URL) {
        // Logos are kept to 1024 px, which is plenty for the center of any code and keeps SVGs small.
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: 1024,
                  kCGImageSourceCreateThumbnailWithTransform: true,
              ] as CFDictionary) else {
            HUDController.shared.show(L10n.t("Could not read \(url.lastPathComponent)"), style: .failure)
            return
        }
        logo = image
    }

    func copyImage() {
        guard let matrix, let image = design.image(matrix, pixels: pixels) else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setData(NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]), forType: .png)
        HUDController.shared.show(L10n.t("QR code copied"), style: .success)
        Feedback.success()
    }

    /// Writes the code next to the source file, or to Downloads or the Desktop when made from the menu.
    func write() throws -> URL {
        guard let matrix else { throw AdawatError.message("This is too long for a QR code. Shorten it or lower the error correction.") }
        let ext = format.rawValue.lowercased()
        let url: URL
        if let source {
            url = Output.url(for: source, extension: ext, suffix: "QR")
        } else {
            let folder = FileManager.default.urls(for: AppSettings.shared.outputLocation == .desktop ? .desktopDirectory : .downloadsDirectory,
                                                  in: .userDomainMask)[0]
            url = Output.unique(folder.appendingPathComponent("QR Code").appendingPathExtension(ext))
        }
        switch format {
        case .png:
            guard let image = design.image(matrix, pixels: pixels),
                  let data = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
                throw AdawatError.message("Could not encode the image")
            }
            try data.write(to: url)
        case .svg:
            try design.svg(matrix).write(to: url, atomically: true, encoding: .utf8)
        }
        return url
    }
}

/// The live preview, and whether a phone camera would read it: every change is drawn and scanned again.
@MainActor
final class QRPreview: ObservableObject {
    enum Scan: Equatable { case checking, readable, unreadable }

    @Published private(set) var image: NSImage?
    @Published private(set) var scan: Scan = .checking
    @Published private(set) var modules = 0
    private var generation = 0

    func update(matrix: QRMatrix?, design: QRDesign, payload: String) {
        generation += 1
        let current = generation
        guard let matrix, let cg = design.image(matrix, pixels: 560) else {
            image = nil
            modules = 0
            return
        }
        image = NSImage(cgImage: cg, size: NSSize(width: 280, height: 280))
        modules = matrix.size
        scan = .checking
        // Scanned over a white backdrop, as a transparent code would be seen on a light page.
        let flat = Self.flatten(cg) ?? cg
        Task { @MainActor in
            let found = (try? await ImageOps.readQRCodes(in: flat)) ?? []
            guard current == self.generation else { return }
            self.scan = found.contains(payload) ? .readable : .unreadable
        }
    }

    private static func flatten(_ image: CGImage) -> CGImage? {
        guard let ctx = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
        ctx.setFillColor(.white)
        ctx.fill(CGRect(x: 0, y: 0, width: image.width, height: image.height))
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        return ctx.makeImage()
    }
}

/// Small drawings of each shape for the pickers, made with the same paths as the real code.
@MainActor
enum QRSwatches {
    private static let sample: QRMatrix = {
        let rows = ["11011", "10110", "01101", "11010", "10111"]
        return QRMatrix(size: 5, dark: rows.flatMap { $0.map { $0 == "1" } })
    }()

    static let modules: [QRDesign.ModuleShape: NSImage] = Dictionary(uniqueKeysWithValues: QRDesign.ModuleShape.allCases.map { shape in
        var design = QRDesign()
        design.module = shape
        design.margin = 0
        design.background = nil
        let image = draw(design.modulePath(sample), units: 5)
        return (shape, image)
    })

    static let eyes: [QRDesign.EyeShape: NSImage] = Dictionary(uniqueKeysWithValues: QRDesign.EyeShape.allCases.map { shape in
        let path = CGMutablePath()
        QRDesign.addEye(shape, outer: CGRect(x: 0, y: 0, width: 7, height: 7), to: path)
        return (shape, draw(path, units: 7, evenOdd: true))
    })

    private static func draw(_ path: CGPath, units: CGFloat, evenOdd: Bool = false) -> NSImage {
        let points: CGFloat = 26
        return NSImage(size: NSSize(width: points, height: points), flipped: true) { rect in
            guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
            ctx.scaleBy(x: rect.width / units, y: rect.height / units)
            ctx.addPath(path)
            ctx.setFillColor(NSColor(Theme.ink).cgColor)
            ctx.fillPath(using: evenOdd ? .evenOdd : .winding)
            return true
        }
    }
}

struct QRView: View {
    @ObservedObject var model: QRModel
    @ObservedObject var preview: QRPreview
    let handle: ToolWindowHandle
    @State private var dropTargeted = false

    init(model: QRModel, handle: ToolWindowHandle) {
        self.model = model
        self.preview = model.preview
        self.handle = handle
    }

    var body: some View {
        ToolScaffold(title: L10n.t("Create QR Code"), onClose: handle.close) {
            HStack(alignment: .top, spacing: 20) {
                VStack(alignment: .leading, spacing: 10) {
                    content
                    Divider()
                    shapes
                    Divider()
                    colors
                    Divider()
                    logo
                    Divider()
                    output
                }
                .font(.system(size: 13))
                .frame(width: 340)

                previewColumn
            }
        } leading: {
            Button(L10n.t("Copy Image"), action: model.copyImage).buttonStyle(.tool).disabled(model.problem != nil)
        } trailing: {
            Button(L10n.t("Save QR Code"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction)
                .disabled(model.problem != nil)
        }
    }

    // MARK: Sections

    @ViewBuilder private var content: some View {
        Picker("", selection: $model.kind) {
            ForEach(QRModel.Kind.allCases) { Text(L10n.t($0.title)).tag($0) }
        }
        .pickerStyle(.segmented).labelsHidden()
        if model.kind == .text {
            TextEditor(text: $model.text)
                .font(.system(size: 13))
                .scrollContentBackground(.hidden)
                .padding(6)
                .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.black.opacity(0.045)))
                .frame(height: 70)
        } else {
            HStack {
                TextField(L10n.t("Network name"), text: $model.network).textFieldStyle(.roundedBorder)
                if model.security != .none {
                    TextField(L10n.t("Password"), text: $model.password).textFieldStyle(.roundedBorder)
                }
            }
            HStack {
                Picker("", selection: $model.security) {
                    ForEach(QRContent.Security.allCases) { Text(L10n.t($0.title)).tag($0) }
                }
                .labelsHidden().fixedSize()
                Spacer()
                Toggle(L10n.t("Hidden network"), isOn: $model.hidden).toggleStyle(.checkbox)
            }
        }
    }

    @ViewBuilder private var shapes: some View {
        ToolRow(title: L10n.t("Shape")) {
            HStack(spacing: 4) {
                ForEach(QRDesign.ModuleShape.allCases) { shape in
                    swatch(QRSwatches.modules[shape], title: shape.title, selected: model.module == shape) { model.module = shape }
                }
            }
        }
        ToolRow(title: L10n.t("Corners")) {
            HStack(spacing: 4) {
                ForEach(QRDesign.EyeShape.allCases) { shape in
                    swatch(QRSwatches.eyes[shape], title: shape.title, selected: model.eye == shape) { model.eye = shape }
                }
            }
        }
    }

    @ViewBuilder private var colors: some View {
        ToolRow(title: L10n.t("Code color")) {
            if model.useGradient {
                ColorPicker(L10n.t("Gradient end"), selection: $model.gradientEnd, supportsOpacity: false).labelsHidden()
                Image(systemName: "arrow.left").font(.system(size: 9, weight: .bold)).foregroundStyle(Theme.muted)
            }
            ColorPicker(L10n.t("Code color"), selection: $model.foreground, supportsOpacity: false).labelsHidden()
        }
        HStack(spacing: 14) {
            Toggle(L10n.t("Gradient"), isOn: $model.useGradient).toggleStyle(.checkbox)
            Toggle(L10n.t("Own corner color"), isOn: $model.separateEyeColor).toggleStyle(.checkbox)
            Spacer()
            if model.separateEyeColor {
                ColorPicker(L10n.t("Corner color"), selection: $model.eyeColor, supportsOpacity: false).labelsHidden()
            }
        }
        ToolRow(title: L10n.t("Background")) {
            Toggle(L10n.t("Transparent"), isOn: $model.transparent).toggleStyle(.checkbox)
            ColorPicker(L10n.t("Background"), selection: $model.background, supportsOpacity: false).labelsHidden()
                .disabled(model.transparent)
        }
    }

    @ViewBuilder private var logo: some View {
        ToolRow(title: L10n.t("Logo")) {
            if let logo = model.logo {
                Image(nsImage: NSImage(cgImage: logo, size: NSSize(width: 24, height: 24)))
                    .resizable().aspectRatio(contentMode: .fit).frame(width: 24, height: 24)
                Button(L10n.t("Remove")) { model.logo = nil }.buttonStyle(.tool).controlSize(.small)
            }
            Button(L10n.t(model.logo == nil ? "Choose Logo…" : "Change…"), action: model.chooseLogo).buttonStyle(.tool).controlSize(.small)
        }
        if model.logo != nil {
            ToolRow(title: L10n.t("Logo size")) {
                Slider(value: $model.logoScale, in: 0.12...0.32).frame(width: 150)
                Text(L10n.t("\(Int((model.logoScale * 100).rounded()))%")).monospacedDigit().foregroundStyle(Theme.muted).frame(width: 34)
            }
            ToolRow(title: L10n.t("Behind the logo")) {
                Picker("", selection: $model.plate) {
                    ForEach(QRDesign.Plate.allCases) { Text(L10n.t($0.title)).tag($0) }
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: 220)
            }
        } else {
            Text(L10n.t("Or drop an image on the preview."))
                .font(.system(size: 11)).foregroundStyle(Theme.muted)
        }
    }

    @ViewBuilder private var output: some View {
        ToolRow(title: L10n.t("Margin")) {
            Slider(value: $model.margin, in: 0...8, step: 1).frame(width: 150)
        }
        ToolRow(title: L10n.t("Error correction")) {
            Picker("", selection: $model.correction) {
                ForEach(QRMatrix.Correction.allCases) { Text(L10n.t($0.title)).tag($0) }
            }
            .labelsHidden().fixedSize()
        }
        ToolRow(title: L10n.t("Format")) {
            if model.format == .png {
                Picker("", selection: $model.pixels) {
                    ForEach([512, 1024, 2048, 4096], id: \.self) { Text(L10n.t("\($0) px")).tag($0) }
                }
                .labelsHidden().fixedSize()
            }
            Picker("", selection: $model.format) {
                ForEach(QRModel.FileFormat.allCases) { Text($0.rawValue).tag($0) }
            }
            .pickerStyle(.segmented).labelsHidden().frame(width: 110)
        }
    }

    private var previewColumn: some View {
        VStack(spacing: 10) {
            ZStack {
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(model.transparent ? AnyShapeStyle(.white) : AnyShapeStyle(Color.black.opacity(0.045)))
                if let image = preview.image {
                    Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit).padding(16)
                } else {
                    Image(systemName: "qrcode").font(.system(size: 64, weight: .light)).foregroundStyle(Theme.muted.opacity(0.5))
                }
                if dropTargeted {
                    RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Theme.accent, lineWidth: 3)
                }
            }
            .frame(width: 290, height: 290)
            .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
                providers.first?.loadObject(ofClass: NSURL.self) { url, _ in
                    guard let url = url as? URL else { return }
                    DispatchQueue.main.async { model.loadLogo(url) }
                }
                return true
            }
            if let problem = model.problem {
                Text(L10n.t(problem)).font(.system(size: 11)).foregroundStyle(Theme.muted)
                    .multilineTextAlignment(.center).frame(width: 280)
            } else {
                scanBadge
                Text(L10n.t("\(preview.modules) × \(preview.modules) modules")).font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        }
    }

    private var scanBadge: some View {
        let (symbol, text, color): (String, String, Color) = {
            switch preview.scan {
            case .checking: return ("ellipsis.circle", "Checking…", Theme.muted)
            case .readable: return ("checkmark.circle.fill", "Scans correctly", Color.green)
            case .unreadable: return ("exclamationmark.triangle.fill", "May not scan. Use a smaller logo, more contrast or higher correction.", Color.orange)
            }
        }()
        return Label(L10n.t(text), systemImage: symbol)
            .font(.system(size: 12, weight: .medium))
            .foregroundStyle(color)
            .multilineTextAlignment(.center)
            .frame(width: 280)
    }

    private func swatch(_ image: NSImage?, title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Group {
                if let image { Image(nsImage: image) }
            }
            .frame(width: 34, height: 34)
            .background(RoundedRectangle(cornerRadius: 7, style: .continuous).fill(Color.black.opacity(selected ? 0.1 : 0.04)))
            .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(selected ? Theme.accent : .clear, lineWidth: 2))
        }
        .buttonStyle(.plain)
        .help(L10n.t(title))
    }

    private func save() {
        let model = model
        handle.close()
        OperationRunner.run("Create QR Code", on: model.source.map { [$0] } ?? []) { _, _ in [try model.write()] }
    }
}

@MainActor
enum QRWindow {
    /// From the wheel: encodes the dropped text file or link.
    static func open(_ files: [URL]) { open(files, show: true) }

    /// From the menu bar: starts with the clipboard's text when it is short enough to be meant for a code.
    static func openFromMenu() {
        let clip = NSPasteboard.general.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        present(QRModel(source: nil, text: clip.count <= 800 ? clip : ""), show: true)
    }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        let file = files.first
        let text = file.flatMap(QRContent.text(from:)) ?? ""
        return present(QRModel(source: file, text: text), show: show)
    }

    @discardableResult
    private static func present(_ model: QRModel, show: Bool) -> NSWindow {
        ToolWindows.present(L10n.t("Create QR Code"), width: 690, show: show) { handle in QRView(model: model, handle: handle) }
    }

    /// Without the window: a 1024 px PNG with the default style, next to the file.
    static func quick(_ files: [URL], context: OperationContext) async throws -> [URL] {
        guard let file = files.first, let text = QRContent.text(from: file), !text.isEmpty else {
            throw AdawatError.message("No text found")
        }
        return [try QRModel(source: file, text: text).write()]
    }
}
