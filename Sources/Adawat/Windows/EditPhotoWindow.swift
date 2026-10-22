import AppKit
import CoreImage
import SwiftUI

@MainActor
final class EditPhotoModel: ObservableObject {
    let file: URL
    let source: CIImage
    let scale: Double
    @Published var adjustments = PhotoAdjustments()
    @Published var preview: CGImage?
    @Published var showOriginal = false

    init(file: URL) throws {
        self.file = file
        let image = try ImageFile(file).upright(maxPixels: 1100)
        source = CIImage(cgImage: image)
        let full = try ImageFile(file).uprightSize
        scale = Double(image.width) / Double(max(full.width, 1))
        preview = image
    }

    /// Renders the preview a moment after the sliders stop moving.
    func render() async {
        try? await Task.sleep(nanoseconds: 30_000_000)
        guard !Task.isCancelled else { return }
        let output = adjustments.apply(to: source, scale: scale)
        preview = ImageEngine.ciContext.createCGImage(output, from: source.extent)
    }

    func binding(_ key: Adjustment.Key) -> Binding<Double> {
        Binding(get: { self.adjustments[key] }, set: { self.adjustments[key] = $0 })
    }
}

struct EditPhotoView: View {
    @ObservedObject var model: EditPhotoModel
    let handle: ToolWindowHandle

    var body: some View {
        ToolScaffold(title: L10n.t("Edit Photo"), onClose: handle.close) {
            VStack(spacing: 12) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color(white: 0.1))
                    if let image = model.showOriginal ? model.source.cgImage ?? model.preview : model.preview {
                        Image(decorative: image, scale: 1).resizable().aspectRatio(contentMode: .fit).padding(8)
                    }
                    VStack {
                        HStack {
                            Spacer()
                            Text(model.showOriginal ? "Original" : "Edited")
                                .font(.system(size: 10, weight: .semibold))
                                .padding(.horizontal, 8).padding(.vertical, 3)
                                .background(Capsule().fill(.black.opacity(0.55)))
                                .foregroundStyle(.white)
                        }
                        Spacer()
                    }
                    .padding(12)
                }
                .frame(height: 280)
                .onLongPressGesture(minimumDuration: 0.1, pressing: { model.showOriginal = $0 }, perform: {})
                .help(L10n.t("Hold to compare with the original"))

                ScrollView {
                    VStack(alignment: .leading, spacing: 14) {
                        ForEach(Adjustment.sections, id: \.title) { section in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(section.title == "Light" && L10n.isArabic ? "الإضاءة" : L10n.t(section.title)).font(.system(size: 11, weight: .semibold)).foregroundStyle(Theme.muted)
                                ForEach(section.items) { item in slider(item) }
                            }
                        }
                    }
                    .padding(.trailing, 6)
                }
                .frame(height: 300)
            }
        } leading: {
            Button(L10n.t("Reset")) { model.adjustments = PhotoAdjustments() }
                .buttonStyle(.tool).disabled(model.adjustments.isNeutral)
        } trailing: {
            Button(L10n.t("Save Edited Copy"), action: save)
                .buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(model.adjustments.isNeutral)
        }
        .task(id: model.adjustments) { await model.render() }
    }

    private func slider(_ item: Adjustment) -> some View {
        let value = model.adjustments[item.key]
        return HStack(spacing: 10) {
            Text(L10n.t(item.title)).font(.system(size: 12)).frame(width: 104, alignment: .leading)
            Slider(value: model.binding(item.key), in: item.range)
                .controlSize(.small)
            Text(format(value, item))
                .font(.system(size: 11)).monospacedDigit().foregroundStyle(Theme.muted)
                .frame(width: 58, alignment: .trailing)
            Button { model.adjustments[item.key] = 0 } label: {
                Image(systemName: "arrow.counterclockwise").font(.system(size: 10))
            }
            .buttonStyle(.plain)
            .foregroundStyle(Theme.muted.opacity(value == 0 ? 0.4 : 1))
            .help(L10n.t("Reset \(item.title)"))
        }
    }

    private func format(_ value: Double, _ item: Adjustment) -> String {
        if item.key == .exposure { return String(format: "%.2f EV", value) }
        if item.key == .hue { return "\(Int(value.rounded()))°" }
        let rounded = Int(value.rounded())
        return item.range.lowerBound < 0 && rounded > 0 ? "+\(rounded)" : "\(rounded)"
    }

    private func save() {
        let adjustments = model.adjustments
        let file = model.file
        handle.close()
        OperationRunner.run("Edit Photo", on: [file]) { files, context in
            context.report("Saving edited copy")
            let url = files[0]
            let format = ImageEngine.editedFormat(for: url)
            let out = Output.url(for: url, extension: format.ext, suffix: "edited")
            try await ImageEngine.saveFiltered(try ImageFile(url), as: format, to: out, quality: 0.92) {
                adjustments.apply(to: $0)
            }
            return [out]
        }
    }
}

@MainActor
enum EditPhotoWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        guard let file = files.first else { return nil }
        do {
            let model = try EditPhotoModel(file: file)
            return ToolWindows.present(L10n.t("Edit Photo"), width: 520, show: show) { handle in
                EditPhotoView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
