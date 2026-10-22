import AppKit
import PDFKit
import SwiftUI
import UniformTypeIdentifiers

/// One page slot in the organizer: which source page it shows and how far it is turned.
struct PageSlot: Identifiable, Equatable {
    let id = UUID()
    let source: Int
    var rotation: Int = 0
}

@MainActor
extension PDFOps {
    /// Writes the pages in `slots` order, each turned by its extra rotation. With `equalWidths`, pages are
    /// scaled down (vectors intact) to the narrowest page's width.
    static func organize(_ file: URL, slots: [PageSlot], equalWidths: Bool, context: OperationContext) async throws -> URL {
        let document = try open(file)
        guard !slots.isEmpty else { throw AdawatError.message("Keep at least one page.") }
        let out = Output.url(for: file, extension: "pdf", suffix: "organized")
        func uprightSize(_ page: PDFPage, extra: Int) -> CGSize {
            let box = page.bounds(for: .mediaBox)
            return ((page.rotation + extra) / 90) % 2 != 0 ? CGSize(width: box.height, height: box.width) : box.size
        }
        let sizes = slots.compactMap { slot in document.page(at: slot.source).map { uprightSize($0, extra: slot.rotation) } }
        let narrowest = sizes.map(\.width).min() ?? 612
        guard let ctx = CGContext(out as CFURL, mediaBox: nil, [kCGPDFContextCreator: "Adawat"] as CFDictionary) else {
            throw AdawatError.message("Could not create the organized PDF.")
        }
        for (index, slot) in slots.enumerated() {
            context.progress(Double(index) / Double(slots.count))
            guard let page = document.page(at: slot.source), let pageRef = page.pageRef else { continue }
            var size = uprightSize(page, extra: slot.rotation)
            if equalWidths, size.width > narrowest {
                size = CGSize(width: narrowest, height: size.height * narrowest / size.width)
            }
            var box = CGRect(origin: .zero, size: size)
            ctx.beginPage(mediaBox: &box)
            // The drawing transform adds the extra turn and only ever scales down, which is all equal widths needs.
            ctx.concatenate(pageRef.getDrawingTransform(.mediaBox, rect: box, rotate: Int32((page.rotation + slot.rotation) % 360),
                                                        preserveAspectRatio: true))
            ctx.drawPDFPage(pageRef)
            ctx.endPage()
        }
        ctx.closePDF()
        // Annotations, links and text stay selectable because pages are drawn as PDF content, not images.
        return out
    }
}

@MainActor
final class OrganizeModel: ObservableObject {
    let file: URL
    let document: PDFDocument
    @Published var slots: [PageSlot]
    @Published var thumbnails: [Int: NSImage] = [:]
    @Published var equalWidths = false
    @Published var dragging: UUID?
    private let original: [PageSlot]

    init(file: URL) throws {
        self.file = file
        document = try PDFOps.open(file)
        original = (0..<document.pageCount).map { PageSlot(source: $0) }
        slots = original
        for index in 0..<min(document.pageCount, 300) {
            thumbnails[index] = document.page(at: index)?.thumbnail(of: CGSize(width: 220, height: 280), for: .mediaBox)
        }
    }

    var isReordered: Bool { slots.map(\.source) != original.map(\.source) }
    var isChanged: Bool { slots.map(\.source) != original.map(\.source) || slots.contains { $0.rotation != 0 } || slots.count != original.count }

    func resetOrder() { slots = original }

    func rotate(_ id: UUID) {
        if let index = slots.firstIndex(where: { $0.id == id }) { slots[index].rotation = (slots[index].rotation + 90) % 360 }
    }

    func duplicate(_ id: UUID) {
        guard let index = slots.firstIndex(where: { $0.id == id }) else { return }
        var copy = PageSlot(source: slots[index].source)
        copy.rotation = slots[index].rotation
        slots.insert(copy, at: index + 1)
    }

    func remove(_ id: UUID) {
        guard slots.count > 1 else { return }
        slots.removeAll { $0.id == id }
    }

    func move(_ id: UUID, before target: UUID) {
        guard id != target, let from = slots.firstIndex(where: { $0.id == id }) else { return }
        let slot = slots.remove(at: from)
        let to = slots.firstIndex(where: { $0.id == target }) ?? slots.count
        slots.insert(slot, at: to)
    }
}

struct OrganizeView: View {
    @ObservedObject var model: OrganizeModel
    let handle: ToolWindowHandle

    var body: some View {
        ToolScaffold(title: L10n.t("Organize PDF"), onClose: handle.close) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text("\(model.slots.count) page\(model.slots.count == 1 ? "" : "s")").font(.system(size: 11)).foregroundStyle(Theme.muted)
                    Spacer()
                    Button(L10n.t("Reset Order"), action: model.resetOrder).buttonStyle(.tool).controlSize(.small).disabled(!model.isChanged)
                }
                ScrollView {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 120), spacing: 14)], spacing: 14) {
                        ForEach(Array(model.slots.enumerated()), id: \.element.id) { index, slot in
                            card(index: index, slot: slot)
                        }
                    }
                    .padding(4)
                }
                .frame(height: 380)
                Divider()
                Toggle(L10n.t("Make all pages the same width"), isOn: $model.equalWidths).toggleStyle(.checkbox).font(.system(size: 13))
                Text(L10n.t("Pages are scaled proportionally to match the narrowest page; their heights may still differ. Drag pages to reorder them; right-click for more."))
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(L10n.t("Save Organized PDF"), action: save).buttonStyle(.tool).keyboardShortcut(.defaultAction)
                .disabled(!model.isChanged && !model.equalWidths)
        }
    }

    private func card(index: Int, slot: PageSlot) -> some View {
        ZStack(alignment: .topLeading) {
            RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.white)
                .shadow(color: .black.opacity(0.12), radius: 3, y: 1)
            Group {
                if let image = model.thumbnails[slot.source] {
                    Image(nsImage: image).resizable().aspectRatio(contentMode: .fit)
                        .rotationEffect(.degrees(Double(slot.rotation)))
                        .padding(10)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            HStack {
                Text(L10n.t("\(index + 1)")).font(.system(size: 10, weight: .bold)).foregroundStyle(.white)
                    .frame(width: 20, height: 20).background(Circle().fill(Theme.accent))
                Spacer()
                Button { model.rotate(slot.id) } label: {
                    Image(systemName: "rotate.right").font(.system(size: 10, weight: .semibold)).foregroundStyle(.white)
                        .frame(width: 20, height: 20).background(Circle().fill(Color.black.opacity(0.6)))
                }
                .buttonStyle(.plain).help(L10n.t("Rotate clockwise"))
            }
            .padding(6)
        }
        .frame(height: 150)
        .opacity(model.dragging == slot.id ? 0.4 : 1)
        .onDrag {
            model.dragging = slot.id
            return NSItemProvider(object: slot.id.uuidString as NSString)
        }
        .onDrop(of: [.text], delegate: PageDropDelegate(target: slot.id, model: model))
        .contextMenu {
            Button(L10n.t("Rotate Clockwise")) { model.rotate(slot.id) }
            Button(L10n.t("Duplicate Page")) { model.duplicate(slot.id) }
            Divider()
            Button(L10n.t("Delete Page")) { model.remove(slot.id) }.disabled(model.slots.count == 1)
        }
    }

    private func save() {
        let (file, slots, equal) = (model.file, model.slots, model.equalWidths)
        handle.close()
        OperationRunner.run("Organize PDF", on: [file]) { files, context in
            [try await PDFOps.organize(files[0], slots: slots, equalWidths: equal, context: context)]
        }
    }
}

/// Moves the dragged page in front of the page it hovers over.
private struct PageDropDelegate: DropDelegate {
    let target: UUID
    let model: OrganizeModel

    func dropEntered(info: DropInfo) {
        MainActor.assumeIsolated {
            if let dragging = model.dragging { withAnimation(.easeOut(duration: 0.15)) { model.move(dragging, before: target) } }
        }
    }

    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }

    func performDrop(info: DropInfo) -> Bool {
        MainActor.assumeIsolated { model.dragging = nil }
        return true
    }
}

@MainActor
enum OrganizePDFWindow {
    static func open(_ files: [URL]) { open(files, show: true) }

    @discardableResult
    static func open(_ files: [URL], show: Bool) -> NSWindow? {
        guard let file = files.first else { return nil }
        do {
            let model = try OrganizeModel(file: file)
            return ToolWindows.present(L10n.t("Organize PDF"), width: 560, show: show) { handle in OrganizeView(model: model, handle: handle) }
        } catch {
            HUDController.shared.show(error.localizedDescription, style: .failure)
            return nil
        }
    }
}
