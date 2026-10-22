import AppKit
import Quartz
import QuickLookThumbnailing
import SwiftUI

enum DockEdge { case left, right }

@MainActor
final class FloatModel: ObservableObject {
    @Published var items: [URL] = []
    @Published var selection: Set<URL> = []
    @Published var thumbnails: [URL: NSImage] = [:]
    @Published var dockedEdge: DockEdge?

    func add(_ urls: [URL]) {
        let fresh = urls.filter { !items.contains($0) }
        items.append(contentsOf: fresh)
        fresh.forEach(loadThumbnail)
    }

    func remove(_ urls: Set<URL>) {
        items.removeAll { urls.contains($0) }
        selection.subtract(urls)
    }

    /// The selected items in display order, or every item when nothing is selected.
    var dragTargets: [URL] {
        selection.isEmpty ? items : items.filter(selection.contains)
    }

    private func loadThumbnail(_ url: URL) {
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 128, height: 128),
                                                   scale: NSScreen.main?.backingScaleFactor ?? 2,
                                                   representationTypes: .all)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { [weak self] rep, _ in
            let image = rep?.nsImage
            DispatchQueue.main.async {
                self?.thumbnails[url] = image ?? NSWorkspace.shared.icon(forFile: url.path)
            }
        }
    }
}

@MainActor
final class FloatWindowController: NSObject, NSWindowDelegate {
    let model = FloatModel()
    var onClose: (() -> Void)?

    private let panel: FloatPanel
    private var expandedSize = NSSize(width: 300, height: 330)
    private let tabSize = NSSize(width: 48, height: 156)
    private var isAdjustingFrame = false
    private var moveCheck: DispatchWorkItem?

    init(files: [URL]) {
        panel = FloatPanel(contentRect: NSRect(origin: .zero, size: expandedSize),
                           styleMask: [.borderless, .nonactivatingPanel, .resizable],
                           backing: .buffered, defer: false)
        super.init()
        model.add(files)

        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.minSize = NSSize(width: 220, height: 200)
        panel.delegate = self
        panel.controller = self
        panel.contentView = NSHostingView(rootView: FloatView(model: model, controller: self).environment(\.layoutDirection, L10n.direction))
    }

    func show(near point: NSPoint) {
        let origin = NSPoint(x: point.x + 24, y: point.y - expandedSize.height / 2)
        isAdjustingFrame = true
        panel.setFrame(clamped(NSRect(origin: origin, size: expandedSize)), display: true)
        isAdjustingFrame = false
        panel.alphaValue = 0
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.18
            panel.animator().alphaValue = 1
        }
    }

    func add(_ files: [URL]) {
        model.add(files)
    }

    /// Renders the Float's content view over a light backdrop, for checking its design.
    func snapshot() -> NSBitmapImageRep? {
        guard let view = panel.contentView else { return nil }
        view.layoutSubtreeIfNeeded()
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
        view.cacheDisplay(in: view.bounds, to: rep)
        return rep
    }

    func bringToFront() {
        panel.orderFrontRegardless()
    }

    func close() {
        panel.orderOut(nil)
        onClose?()
    }

    func remove(_ urls: Set<URL>) {
        model.remove(urls)
    }

    func open(_ url: URL) {
        NSWorkspace.shared.open(url)
    }

    func quickLook() {
        guard let preview = QLPreviewPanel.shared() else { return }
        if preview.isVisible {
            preview.orderOut(nil)
        } else {
            panel.makeKey()
            preview.makeKeyAndOrderFront(nil)
        }
    }

    func airDrop() {
        let targets = model.dragTargets
        guard !targets.isEmpty else { return }
        NSSharingService(named: .sendViaAirDrop)?.perform(withItems: targets)
    }

    // MARK: Docking

    func windowDidMove(_ notification: Notification) {
        guard !isAdjustingFrame else { return }
        moveCheck?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.settleAfterMove() }
        moveCheck = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: work)
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        if model.dockedEdge == nil { expandedSize = panel.frame.size }
    }

    private func settleAfterMove() {
        // Wait until the user lets go of the window.
        if NSEvent.pressedMouseButtons & 1 != 0 {
            windowDidMove(Notification(name: NSWindow.didMoveNotification))
            return
        }
        guard let visible = screenFrame() else { return }
        let frame = panel.frame
        let threshold: CGFloat = 28

        if let edge = model.dockedEdge {
            let distance = edge == .left ? frame.minX - visible.minX : visible.maxX - frame.maxX
            if distance > 60 { undock(at: frame.origin) } else { dock(to: edge) }
            return
        }
        if frame.minX <= visible.minX + threshold {
            dock(to: .left)
        } else if frame.maxX >= visible.maxX - threshold {
            dock(to: .right)
        }
    }

    func dock(to edge: DockEdge) {
        guard let visible = screenFrame() else { return }
        if model.dockedEdge == nil { expandedSize = panel.frame.size }
        let y = min(max(panel.frame.midY - tabSize.height / 2, visible.minY), visible.maxY - tabSize.height)
        let x = edge == .left ? visible.minX : visible.maxX - tabSize.width
        model.dockedEdge = edge
        setFrame(NSRect(x: x, y: y, width: tabSize.width, height: tabSize.height), animate: true)
    }

    func undock(at origin: NSPoint? = nil) {
        guard let visible = screenFrame(), let edge = model.dockedEdge else { return }
        let x = origin?.x ?? (edge == .left ? visible.minX + 8 : visible.maxX - expandedSize.width - 8)
        let y = (origin?.y ?? panel.frame.midY - expandedSize.height / 2)
        model.dockedEdge = nil
        setFrame(clamped(NSRect(origin: NSPoint(x: x, y: y), size: expandedSize)), animate: true)
    }

    private func setFrame(_ frame: NSRect, animate: Bool) {
        isAdjustingFrame = true
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = animate ? 0.2 : 0
            ctx.allowsImplicitAnimation = true
            panel.animator().setFrame(frame, display: true)
        }, completionHandler: {
            MainActor.assumeIsolated { self.isAdjustingFrame = false }
        })
    }

    private func screenFrame() -> NSRect? {
        (panel.screen ?? NSScreen.main)?.visibleFrame
    }

    private func clamped(_ rect: NSRect) -> NSRect {
        guard let visible = NSScreen.screens.first(where: { $0.frame.intersects(rect) })?.visibleFrame
                ?? NSScreen.main?.visibleFrame else { return rect }
        var rect = rect
        rect.origin.x = min(max(rect.minX, visible.minX + 8), visible.maxX - rect.width - 8)
        rect.origin.y = min(max(rect.minY, visible.minY + 8), visible.maxY - rect.height - 8)
        return rect
    }

    // MARK: Quick Look data

    fileprivate var previewItems: [URL] {
        model.selection.isEmpty ? model.items : model.items.filter(model.selection.contains)
    }
}

/// A borderless panel that can take key focus for Space (Quick Look) and Delete.
final class FloatPanel: NSPanel, QLPreviewPanelDataSource {
    weak var controller: FloatWindowController?

    override var canBecomeKey: Bool { true }

    override func keyDown(with event: NSEvent) {
        MainActor.assumeIsolated {
            guard let controller else { return super.keyDown(with: event) }
            switch event.keyCode {
            case 49: controller.quickLook()
            case 51, 117: controller.remove(controller.model.selection)
            case 53: controller.model.selection = []
            default:
                if event.modifierFlags.contains(.command), event.charactersIgnoringModifiers == "a" {
                    controller.model.selection = Set(controller.model.items)
                } else {
                    super.keyDown(with: event)
                }
            }
        }
    }

    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }

    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = self
    }

    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = nil
    }

    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int {
        MainActor.assumeIsolated { controller?.previewItems.count ?? 0 }
    }

    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! {
        MainActor.assumeIsolated { controller?.previewItems[index] as NSURL? }
    }
}
