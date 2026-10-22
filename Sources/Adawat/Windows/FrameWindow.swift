import AppKit
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class FrameModel: ObservableObject {
    let files: [URL]
    let photo: CGImage
    /// Preview pixels per output pixel.
    let scale: CGFloat

    @Published var options = FrameOptions()
    @Published var preview: CGImage?
    @Published var error: String?
    @Published var customColor = Color(red: 0.95, green: 0.6, blue: 0.3)

    init(files: [URL]) throws {
        self.files = files
        let file = try ImageFile(files[0])
        photo = try file.upright(maxPixels: 700)
        scale = CGFloat(photo.width) / CGFloat(max(file.uprightSize.width, 1))
    }

    func render() async {
        try? await Task.sleep(nanoseconds: 25_000_000)
        guard !Task.isCancelled else { return }
        do {
            preview = try FrameRenderer.render(photo, options: options, scale: scale)
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    func chooseImage() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image]
        panel.message = L10n.t("Choose a background image")
        if panel.runModal() == .OK, let url = panel.url { options.background = .image(url) }
    }
}

struct FrameView: View {
    @ObservedObject var model: FrameModel
    let handle: ToolWindowHandle

    var body: some View {
        ToolScaffold(title: L10n.t("Add BG"), onClose: handle.close) {
            VStack(alignment: .leading, spacing: 12) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.black.opacity(0.05))
                    if let preview = model.preview {
                        Image(decorative: preview, scale: 1).resizable().aspectRatio(contentMode: .fit).padding(14)
                    }
                }
                .frame(height: 300)

                HStack(alignment: .top, spacing: 12) {
                    Text(L10n.t("Background")).font(.system(size: 12)).frame(width: 80, alignment: .leading).padding(.top, 6)
                    VStack(alignment: .leading, spacing: 6) {
                        HStack(spacing: 6) {
                            ForEach(FrameBackground.gradients.indices, id: \.self) { index in
                                let stops = FrameBackground.gradients[index]
                                swatch(.gradient(index)) {
                                    LinearGradient(colors: stops.map { Color(cgColor: $0.cgColor) },
                                                   startPoint: .bottomLeading, endPoint: .topTrailing)
                                }
                            }
                            iconSwatch("photo", selected: isImage, help: "Choose an image background", action: model.chooseImage)
                        }
                        HStack(spacing: 6) {
                            ForEach(FrameBackground.solids, id: \.self) { color in
                                swatch(.solid(color)) { Color(cgColor: color.cgColor) }
                            }
                            iconSwatch("drop.halffull", selected: model.options.background == .blurred,
                                       help: "Blurred copy of the photo") { model.options.background = .blurred }
                        }
                        HStack(spacing: 8) {
                            ColorPicker(L10n.t("Custom color"), selection: $model.customColor, supportsOpacity: false)
                                .font(.system(size: 11))
                                .onChange(of: model.customColor) { _, color in
                                    if let cg = NSColor(color).cgColor as CGColor? { model.options.background = .solid(RGB(cg)) }
                                }
                        }
                    }
                }

                slider("Padding", value: $model.options.padding, range: 0...600)
                slider("Corners", value: $model.options.corners, range: 0...200)
                slider("Shadow", value: $model.options.shadow, range: 0...100)
                ToolRow(title: L10n.t("Ratio")) {
                    Picker("", selection: $model.options.ratio) {
                        ForEach(FrameRatio.allCases) { Text(L10n.t($0.rawValue)).tag($0) }
                    }
                    .pickerStyle(.segmented).labelsHidden().fixedSize()
                }
                if let error = model.error {
                    Text(error).font(.system(size: 11)).foregroundStyle(Theme.accent)
                }
                if model.files.count > 1 {
                    Text(L10n.t("The same background is added to all \(model.files.count) images."))
                        .font(.system(size: 11)).foregroundStyle(Theme.muted)
                }
            }
        } leading: {
            Button(L10n.t("Reset")) { model.options = FrameOptions() }.buttonStyle(.tool).disabled(model.options == FrameOptions())
        } trailing: {
            Button(L10n.t("Save with Background"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction)
                .disabled(model.error != nil)
        }
        .task(id: model.options) { await model.render() }
    }

    private var isImage: Bool {
        if case .image = model.options.background { return true }
        return false
    }

    private func swatch<Fill: View>(_ background: FrameBackground, @ViewBuilder fill: () -> Fill) -> some View {
        Button { model.options.background = background } label: {
            fill()
                .frame(width: 24, height: 24)
                .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.black.opacity(0.12)))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .strokeBorder(Theme.accent, lineWidth: model.options.background == background ? 2.5 : 0).padding(-3))
        }
        .buttonStyle(.plain)
    }

    private func iconSwatch(_ symbol: String, selected: Bool, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 11))
                .frame(width: 24, height: 24)
                .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.black.opacity(0.07)))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .strokeBorder(Theme.accent, lineWidth: selected ? 2.5 : 0).padding(-3))
        }
        .buttonStyle(.plain)
        .help(L10n.t(help))
    }

    private func slider(_ title: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        HStack(spacing: 10) {
            Text(title).font(.system(size: 12)).frame(width: 80, alignment: .leading)
            Slider(value: value, in: range).controlSize(.small)
            Text(L10n.t("\(Int(value.wrappedValue)) px")).font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                .frame(width: 54, alignment: .trailing)
        }
    }

    private func save() {
        let options = model.options
        let files = model.files
        handle.close()
        OperationRunner.run("Add Background", on: files) { files, context in
            var outputs: [URL] = []
            for (index, url) in files.enumerated() {
                context.report("Adding background \(index + 1) of \(files.count)")
                let source = try ImageFile(url)
                let full = try source.upright()
                let image = try FrameRenderer.render(full, options: options)
                let format = ImageEngine.editedFormat(for: url)
                let out = Output.url(for: url, extension: format.ext, suffix: "framed")
                try await ImageEngine.saveDrawn(source, image: image, as: format, to: out, quality: 0.92)
                outputs.append(out)
            }
            return outputs
        }
    }
}

@MainActor
enum FrameWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        do {
            let model = try FrameModel(files: files)
            return ToolWindows.present(L10n.t("Add BG"), width: 500, show: show) { handle in
                FrameView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
