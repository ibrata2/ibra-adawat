import AppKit
import QuickLookThumbnailing
import SwiftUI

@MainActor
final class OrderModel: ObservableObject {
    @Published var items: [URL]
    @Published var thumbnails: [URL: NSImage] = [:]

    init(items: [URL]) {
        self.items = items
        items.forEach(loadThumbnail)
    }

    func move(_ index: Int, by offset: Int) {
        let target = index + offset
        guard items.indices.contains(index), items.indices.contains(target) else { return }
        items.swapAt(index, target)
    }

    private func loadThumbnail(_ url: URL) {
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 64, height: 64),
                                                   scale: NSScreen.main?.backingScaleFactor ?? 2, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { [weak self] representation, _ in
            let image = representation?.nsImage ?? NSWorkspace.shared.icon(forFile: url.path)
            DispatchQueue.main.async { self?.thumbnails[url] = image }
        }
    }
}

/// A numbered list the user reorders before files are combined, starting in Finder's selection order.
struct OrderView: View {
    @ObservedObject var model: OrderModel
    let title: String
    let caption: String
    let action: String
    let handle: ToolWindowHandle
    let onDone: ([URL]) -> Void

    var body: some View {
        ToolScaffold(title: title, onClose: handle.close) {
            VStack(alignment: .leading, spacing: 8) {
                Text(caption).font(.system(size: 12)).foregroundStyle(Theme.muted)
                ScrollView {
                    VStack(spacing: 6) {
                        ForEach(Array(model.items.enumerated()), id: \.element) { index, url in
                            row(index: index, url: url)
                        }
                    }
                }
                .frame(minHeight: 120, maxHeight: 340)
                .fixedSize(horizontal: false, vertical: model.items.count <= 5)
            }
        } leading: {
            EmptyView()
        } trailing: {
            Button(action) {
                let order = model.items
                handle.close()
                onDone(order)
            }
            .buttonStyle(.tool)
            .keyboardShortcut(.defaultAction)
        }
    }

    private func row(index: Int, url: URL) -> some View {
        HStack(spacing: 12) {
            Text(L10n.t("\(index + 1)")).font(.system(size: 12)).monospacedDigit().foregroundStyle(Theme.muted).frame(width: 18)
            Group {
                if let image = model.thumbnails[url] {
                    Image(nsImage: image).resizable().aspectRatio(contentMode: .fit)
                } else {
                    Color.black.opacity(0.06)
                }
            }
            .frame(width: 34, height: 26)
            Text(url.lastPathComponent).font(.system(size: 12)).lineLimit(1).truncationMode(.middle)
            Spacer()
            VStack(spacing: 2) {
                arrow("chevron.up", enabled: index > 0) { model.move(index, by: -1) }
                arrow("chevron.down", enabled: index < model.items.count - 1) { model.move(index, by: 1) }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Color.black.opacity(0.045)))
    }

    private func arrow(_ symbol: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 9, weight: .bold))
                .frame(width: 32, height: 14)
                .background(RoundedRectangle(cornerRadius: 4).fill(Color.black.opacity(enabled ? 0.08 : 0.03)))
        }
        .buttonStyle(.plain)
        .foregroundStyle(Theme.ink.opacity(enabled ? 0.8 : 0.25))
        .disabled(!enabled)
    }
}

@MainActor
enum OrderWindow {
    static func mergePDFs(_ files: [URL]) { mergePDFs(files, show: true) }
    static func joinVideos(_ files: [URL]) { joinVideos(files, show: true) }

    @discardableResult
    static func mergePDFs(_ files: [URL], show: Bool) -> NSWindow {
        present("Merge PDFs", caption: "Document order", action: "Merge PDFs", files: files, show: show) { order in
            OperationRunner.run("Merge PDFs", on: order) { try await PDFOps.merge($0, context: $1) }
        }
    }

    @discardableResult
    static func joinVideos(_ files: [URL], show: Bool) -> NSWindow {
        present("Join Videos", caption: "Playback order", action: "Join Videos", files: files, show: show) { order in
            OperationRunner.run("Join Videos", on: order) { try await MediaOps.join($0, context: $1) }
        }
    }

    private static func present(_ title: String, caption: String, action: String, files: [URL], show: Bool,
                                onDone: @escaping ([URL]) -> Void) -> NSWindow {
        let model = OrderModel(items: files)
        return ToolWindows.present(title, width: 380, show: show) { handle in
            OrderView(model: model, title: title, caption: caption, action: action, handle: handle, onDone: onDone)
        }
    }
}
