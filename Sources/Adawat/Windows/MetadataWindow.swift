import AppKit
import ImageIO
import SwiftUI

struct MetadataField: Identifiable, Equatable {
    /// The ImageIO tag path, such as "exif:FNumber"; nil for read-only file facts.
    let path: String?
    let group: String
    let name: String
    let original: String
    let isNumber: Bool
    let editable: Bool
    var value: String
    var removed = false

    var id: String { path ?? "\(group).\(name)" }
    var isChanged: Bool { removed || value != original }
}

@MainActor
enum ImageMetadata {
    private static let groupNames: [String: String] = [
        "exif": "EXIF", "exifEX": "EXIF", "tiff": "TIFF", "xmp": "XMP", "dc": "Description", "photoshop": "Photoshop",
        "aux": "Camera", "iptc": "IPTC", "Iptc4xmpCore": "IPTC", "xmpRights": "Rights", "xmpMM": "History", "crs": "Camera Raw",
        "apple": "Apple", "HDRGainMap": "HDR",
    ]

    /// Every tag in the file, grouped, plus read-only facts about the file itself.
    static func fields(of file: ImageFile) -> [MetadataField] {
        var fields = fileFacts(file)
        guard let metadata = CGImageSourceCopyMetadataAtIndex(file.source, 0, nil) else { return fields }
        let options = [kCGImageMetadataEnumerateRecursively: false] as CFDictionary
        CGImageMetadataEnumerateTagsUsingBlock(metadata, nil, options) { path, tag in
            let path = path as String
            let prefix = (CGImageMetadataTagCopyPrefix(tag) as String?) ?? String(path.split(separator: ":").first ?? "")
            let name = (CGImageMetadataTagCopyName(tag) as String?) ?? path
            let type = CGImageMetadataTagGetType(tag)
            let raw = CGImageMetadataTagCopyValue(tag)
            var text = ""
            var isNumber = false
            if let string = raw as? String {
                text = string
            } else if let number = raw as? NSNumber {
                text = number.stringValue
                isNumber = true
            } else if let array = raw as? [CGImageMetadataTag] {
                text = array.compactMap { CGImageMetadataTagCopyValue($0) as? String }.joined(separator: ", ")
            } else if raw != nil {
                text = "…"
            }
            var group = groupNames[prefix] ?? prefix.uppercased()
            if name.hasPrefix("GPS") { group = "GPS" }
            let editable = type == .string || type == .default
            fields.append(MetadataField(path: path, group: group, name: pretty(name), original: text, isNumber: isNumber,
                                        editable: editable, value: text))
            return true
        }
        return fields
    }

    private static func fileFacts(_ file: ImageFile) -> [MetadataField] {
        let props = file.properties
        var facts: [(String, String)] = [
            ("Format", ImageFormat.of(file.url)?.name ?? file.url.pathExtension.uppercased()),
            ("Dimensions", "\(file.uprightSize.width) × \(file.uprightSize.height)"),
            ("File Size", ByteText.string(ImageOps.fileSize(file.url))),
        ]
        if let depth = props[kCGImagePropertyDepth] as? Int { facts.append(("Bits Per Sample", "\(depth)")) }
        if let model = props[kCGImagePropertyColorModel] as? String { facts.append(("Color Model", model)) }
        if let profile = props[kCGImagePropertyProfileName] as? String { facts.append(("Color Profile", profile)) }
        if let dpi = props[kCGImagePropertyDPIWidth] as? Double { facts.append(("Resolution", "\(Int(dpi)) DPI")) }
        if file.hasGainMap { facts.append(("HDR", "Gain map present")) }
        return facts.map { MetadataField(path: nil, group: "File", name: $0.0, original: $0.1, isNumber: false, editable: false, value: $0.1) }
    }

    /// "DateTimeOriginal" → "Date Time Original", "GPSLongitude" → "GPS Longitude"
    private static func pretty(_ name: String) -> String {
        let characters = Array(name)
        var result = ""
        for (index, character) in characters.enumerated() {
            if index > 0, character.isUppercase {
                let previous = characters[index - 1]
                let next = index + 1 < characters.count ? characters[index + 1] : " "
                // A new word starts after a lowercase letter, or where an acronym meets a capitalized word.
                if previous.isLowercase || previous.isNumber || (previous.isUppercase && next.isLowercase) { result.append(" ") }
            }
            result.append(character)
        }
        return result
    }

    /// Writes the edits without re-compressing the pixels.
    static func save(_ fields: [MetadataField], of file: ImageFile, to out: URL) throws {
        guard let type = file.typeIdentifier, ImageEngine.writableTypes.contains(type),
              let destination = CGImageDestinationCreateWithURL(out as CFURL, type as CFString, 1, nil) else {
            throw AdawatError.message("This file type's metadata cannot be edited here.")
        }
        let base = CGImageSourceCopyMetadataAtIndex(file.source, 0, nil)
        let metadata = base.flatMap { CGImageMetadataCreateMutableCopy($0) } ?? CGImageMetadataCreateMutable()
        for field in fields where field.isChanged {
            guard let path = field.path else { continue }
            if field.removed {
                CGImageMetadataRemoveTagWithPath(metadata, nil, path as CFString)
            } else if field.isNumber, let number = Double(field.value) {
                CGImageMetadataSetValueWithPath(metadata, nil, path as CFString, number as CFNumber)
            } else {
                CGImageMetadataSetValueWithPath(metadata, nil, path as CFString, field.value as CFString)
            }
        }
        let options: [CFString: Any] = [kCGImageDestinationMetadata: metadata, kCGImageDestinationMergeMetadata: false]
        var error: Unmanaged<CFError>?
        guard CGImageDestinationCopyImageSource(destination, file.source, options as CFDictionary, &error) else {
            try? FileManager.default.removeItem(at: out)
            let reason = error.map { CFErrorCopyDescription($0.takeRetainedValue()) as String } ?? "unknown error"
            throw AdawatError.message("Could not save the metadata: \(reason)")
        }
    }
}

@MainActor
final class MetadataModel: ObservableObject {
    let file: URL
    let image: ImageFile
    @Published var fields: [MetadataField]
    @Published var search = ""

    init(file: URL) throws {
        self.file = file
        image = try ImageFile(file)
        fields = ImageMetadata.fields(of: image)
    }

    var groups: [(name: String, fields: [MetadataField])] {
        let query = search.trimmingCharacters(in: .whitespaces).lowercased()
        let visible = fields.filter {
            query.isEmpty || $0.name.lowercased().contains(query) || $0.value.lowercased().contains(query) || $0.group.lowercased().contains(query)
        }
        var order: [String] = []
        for field in visible where !order.contains(field.group) { order.append(field.group) }
        return order.map { name in (name, visible.filter { $0.group == name }) }
    }

    var hasChanges: Bool { fields.contains(where: \.isChanged) }

    func binding(_ id: String) -> Binding<String> {
        Binding(get: { self.fields.first { $0.id == id }?.value ?? "" },
                set: { value in if let i = self.fields.firstIndex(where: { $0.id == id }) { self.fields[i].value = value } })
    }

    func toggleRemoved(_ id: String) {
        if let i = fields.firstIndex(where: { $0.id == id }) { fields[i].removed.toggle() }
    }
}

struct MetadataView: View {
    @ObservedObject var model: MetadataModel
    let handle: ToolWindowHandle

    var body: some View {
        ToolScaffold(title: L10n.t("Metadata"), onClose: handle.close) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text(model.file.lastPathComponent).font(.system(size: 12)).foregroundStyle(Theme.muted)
                        .lineLimit(1).truncationMode(.middle)
                    Spacer()
                    TextField(L10n.t("Search metadata"), text: $model.search).textFieldStyle(.roundedBorder).frame(width: 180)
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 14) {
                        ForEach(model.groups, id: \.name) { group in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(L10n.t(group.name)).font(.system(size: 11, weight: .semibold)).foregroundStyle(Theme.muted)
                                ForEach(group.fields) { field in row(field) }
                            }
                            Divider()
                        }
                        if model.groups.isEmpty {
                            Text(L10n.t("No matching metadata.")).font(.system(size: 12)).foregroundStyle(Theme.muted)
                        }
                    }
                    .padding(.trailing, 6)
                }
                .frame(height: 380)
                Text(L10n.t("For photos, Remove All also clears hidden camera and processing data. The original stays unchanged."))
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Remove All"), action: removeAll).buttonStyle(.tool)
            Button(L10n.t("Save"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction).disabled(!model.hasChanges)
        }
    }

    private func row(_ field: MetadataField) -> some View {
        HStack(spacing: 10) {
            Text(L10n.t("\(field.name):")).font(.system(size: 12)).frame(width: 150, alignment: .trailing).lineLimit(1)
            Group {
                if field.editable {
                    TextField("", text: model.binding(field.id)).textFieldStyle(.roundedBorder)
                } else {
                    Text(field.value).textSelection(.enabled).lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            .font(.system(size: 12))
            .strikethrough(field.removed)
            .opacity(field.removed ? 0.4 : 1)
            if field.path != nil {
                Button { model.toggleRemoved(field.id) } label: {
                    Image(systemName: field.removed ? "arrow.uturn.backward.circle" : "minus.circle")
                }
                .buttonStyle(.plain)
                .foregroundStyle(field.removed ? Theme.accent : Theme.muted)
                .help(field.removed ? "Keep this field" : "Remove this field")
            } else {
                Color.clear.frame(width: 14)
            }
        }
    }

    private func save() {
        let (fields, file) = (model.fields, model.file)
        handle.close()
        OperationRunner.run("Metadata", on: [file]) { files, context in
            let out = Output.url(for: files[0], extension: files[0].pathExtension, suffix: "edited metadata")
            try ImageMetadata.save(fields, of: try ImageFile(files[0]), to: out)
            context.finish(with: "Metadata saved")
            return [out]
        }
    }

    private func removeAll() {
        handle.close()
        OperationRunner.run("Remove Metadata", on: [model.file]) { try await ImageOps.removeMetadata($0, context: $1) }
    }
}

@MainActor
enum MetadataWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        guard let file = files.first else { return nil }
        do {
            let model = try MetadataModel(file: file)
            return ToolWindows.present(L10n.t("Metadata"), width: 520, show: show) { handle in
                MetadataView(model: model, handle: handle)
            }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
