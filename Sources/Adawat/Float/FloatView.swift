import AppKit
import SwiftUI
import UniformTypeIdentifiers

struct FloatView: View {
    @ObservedObject var model: FloatModel
    let controller: FloatWindowController
    @State private var isTargeted = false

    var body: some View {
        Group {
            if let edge = model.dockedEdge {
                FloatTab(model: model, edge: edge, controller: controller)
            } else {
                expanded
            }
        }
        .environment(\.colorScheme, .light)
        .onDrop(of: [.fileURL], isTargeted: $isTargeted) { providers in
            loadURLs(providers) { controller.add($0) }
            return true
        }
    }

    private var expanded: some View {
        VStack(spacing: 0) {
            header
            Rectangle().fill(Theme.petalStroke).frame(height: 0.5).padding(.horizontal, 12)
            if model.items.isEmpty {
                emptyState
            } else {
                grid
            }
        }
        .background(LightGlass(cornerRadius: 16, highlighted: isTargeted))
        .overlay(alignment: .topLeading) {
            PaperclipView()
                .frame(width: 22, height: 54)
                .rotationEffect(.degrees(-8))
                .offset(x: 22, y: -16)
        }
        .padding(.top, 26)
        .padding([.leading, .trailing, .bottom], 8)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Text(model.items.isEmpty ? "FLOAT" : "FLOAT · \(model.items.count)")
                .font(Theme.rounded(10, .semibold))
                .tracking(0.35)
                .foregroundStyle(Theme.ink)
                .padding(.leading, 44)
            Spacer()
            if !model.items.isEmpty {
                FileDragSource(urls: { model.dragTargets }) {
                    Image(systemName: "square.on.square")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(Theme.muted)
                        .help(model.selection.isEmpty ? "Drag to carry all files" : "Drag to carry the selection")
                }
                .frame(width: 22, height: 22)
                headerButton("dot.radiowaves.left.and.right", help: "Share with AirDrop") { controller.airDrop() }
                headerButton("eye", help: "Quick Look (Space)") { controller.quickLook() }
            }
            headerButton("sidebar.left", help: "Attach to the screen edge") { controller.dock(to: .right) }
            headerButton("xmark", help: "Close Float without deleting files") { controller.close() }
        }
        .padding(.horizontal, 10)
        .frame(height: 34)
        .background(WindowDragHandle(menuItems: [
            ("Attach to Screen Edge", { controller.dock(to: .right) }),
            ("Close Float", { controller.close() }),
        ]))
    }

    private func headerButton(_ symbol: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 11, weight: .semibold))
                .frame(width: 22, height: 22)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(Theme.muted)
        .help(L10n.t(help))
    }

    private var emptyState: some View {
        VStack(spacing: 8) {
            Image(systemName: "tray.and.arrow.down")
                .font(.system(size: 26, weight: .light))
                .foregroundStyle(Theme.muted)
            Text(L10n.t("Drop files here"))
                .font(Theme.rounded(12, .medium))
                .foregroundStyle(Theme.muted)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(WindowDragHandle())
    }

    private var grid: some View {
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 82), spacing: 8)], spacing: 10) {
                ForEach(model.items, id: \.self) { url in
                    FloatItem(url: url, model: model, controller: controller)
                }
            }
            .padding(10)
        }
    }

    private func loadURLs(_ providers: [NSItemProvider], completion: @escaping ([URL]) -> Void) {
        var urls: [URL?] = Array(repeating: nil, count: providers.count)
        let lock = NSLock()
        let group = DispatchGroup()
        for (index, provider) in providers.enumerated() {
            group.enter()
            _ = provider.loadObject(ofClass: URL.self) { url, _ in
                lock.lock()
                urls[index] = url
                lock.unlock()
                group.leave()
            }
        }
        group.notify(queue: .main) { completion(urls.compactMap { $0 }) }
    }
}

private struct FloatItem: View {
    let url: URL
    @ObservedObject var model: FloatModel
    let controller: FloatWindowController

    private var isSelected: Bool { model.selection.contains(url) }

    var body: some View {
        FileDragSource(
            urls: { isSelected ? model.dragTargets : [url] },
            onClick: { modifiers in select(extending: modifiers.contains(.command) || modifiers.contains(.shift)) },
            onDoubleClick: { controller.open(url) }
        ) {
            VStack(spacing: 4) {
                Group {
                    if let image = model.thumbnails[url] {
                        Image(nsImage: image).resizable().aspectRatio(contentMode: .fit)
                    } else {
                        ProgressView().controlSize(.small)
                    }
                }
                .frame(width: 60, height: 60)
                Text(url.lastPathComponent)
                    .font(.system(size: 10))
                    .lineLimit(2)
                    .multilineTextAlignment(.center)
                    .truncationMode(.middle)
            }
            .padding(6)
            .frame(width: 82)
            .foregroundStyle(Theme.ink)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(isSelected ? Theme.accent.opacity(0.14) : .clear)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .strokeBorder(isSelected ? Theme.accent.opacity(0.55) : .clear, lineWidth: 1)
                    )
            )
        }
        .contextMenu {
            Button(L10n.t("Open")) { controller.open(url) }
            Button(L10n.t("Quick Look")) { model.selection = [url]; controller.quickLook() }
            Button(L10n.t("Show in Finder")) { NSWorkspace.shared.activateFileViewerSelecting([url]) }
            Button(L10n.t("Copy Path")) { Clipboard.copy(text: url.path) }
            Divider()
            Button(L10n.t("Remove from Float")) { controller.remove(isSelected ? model.selection : [url]) }
        }
        .help(L10n.t("\(url.lastPathComponent). Double-click to open; drag to carry the file."))
    }

    private func select(extending: Bool) {
        if extending {
            if isSelected { model.selection.remove(url) } else { model.selection.insert(url) }
        } else {
            model.selection = [url]
        }
    }
}

/// The collapsed Float attached to a screen edge.
private struct FloatTab: View {
    @ObservedObject var model: FloatModel
    let edge: DockEdge
    let controller: FloatWindowController
    @State private var isHovering = false

    var body: some View {
        let shape = UnevenRoundedRectangle(
            topLeadingRadius: edge == .right ? 14 : 0, bottomLeadingRadius: edge == .right ? 14 : 0,
            bottomTrailingRadius: edge == .left ? 14 : 0, topTrailingRadius: edge == .left ? 14 : 0,
            style: .continuous)

        ZStack {
            shape.fill(.white.opacity(isHovering ? 0.86 : 0.72)).background(.ultraThinMaterial, in: shape)
            shape.strokeBorder(.white.opacity(0.7))
            shape.stroke(Theme.petalStroke, lineWidth: 0.5)

            VStack(spacing: 10) {
                PaperclipView().frame(width: 12, height: 30)
                Text(L10n.t("\(model.items.count)"))
                    .font(Theme.rounded(13, .semibold))
                    .foregroundStyle(Theme.ink)
                Color.clear.frame(width: 24, height: 24)
            }
            .allowsHitTesting(false)

            // Above the artwork so a click anywhere on the tab opens it, and a drag moves it.
            WindowDragHandle(onClick: { controller.undock() }, menuItems: [
                ("Open Float", { controller.undock() }),
                ("Close Float", { controller.close() }),
            ])

            VStack {
                if isHovering {
                    Button { controller.close() } label: {
                        Image(systemName: "xmark")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundStyle(Theme.muted)
                            .frame(width: 18, height: 18)
                            .background(Circle().fill(.white.opacity(0.9)))
                            .overlay(Circle().stroke(Theme.petalStroke, lineWidth: 0.5))
                    }
                    .buttonStyle(.plain)
                    .help(L10n.t("Close Float without deleting files"))
                    .transition(.opacity)
                }
                Spacer()
                if !model.items.isEmpty {
                    FileDragSource(urls: { model.items }) {
                        Image(systemName: "square.on.square")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundStyle(Theme.muted)
                            .frame(width: 24, height: 24)
                    }
                    .frame(width: 24, height: 24)
                    .help(L10n.t("Drag to carry all files"))
                }
            }
            .padding(.vertical, 10)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onHover { hovering in withAnimation(.easeOut(duration: 0.12)) { isHovering = hovering } }
        .help(L10n.t("Click to open the Float. Drag away from the edge to detach. Right-click to close."))
    }
}

// MARK: - Styling

/// Frosted white panel with a hairline edge, matching the wheel's center label.
struct LightGlass: View {
    var cornerRadius: CGFloat
    var highlighted = false

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
        shape
            .fill(.white.opacity(0.74))
            .background(.ultraThinMaterial, in: shape)
            .overlay(shape.strokeBorder(.white.opacity(0.7), lineWidth: 1))
            .overlay(shape.stroke(highlighted ? Theme.accent : Theme.petalStroke, lineWidth: highlighted ? 2 : 0.75))
            .shadow(color: Theme.shadow, radius: 14, y: 6)
    }
}

/// A steel paperclip drawn as one continuous wire with three nested U-turns.
struct PaperclipShape: Shape {
    func path(in rect: CGRect) -> Path {
        let w = rect.width
        let h = rect.height
        let x = rect.minX
        let y = rect.minY
        var path = Path()
        // Inner leg, starting low on the right of the middle loop.
        path.move(to: CGPoint(x: x + w * 0.62, y: y + h * 0.30))
        path.addLine(to: CGPoint(x: x + w * 0.62, y: y + h * 0.80))
        path.addArc(center: CGPoint(x: x + w * 0.45, y: y + h * 0.80), radius: w * 0.17,
                    startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
        path.addLine(to: CGPoint(x: x + w * 0.28, y: y + h * 0.17))
        // Top turn of the outer loop.
        path.addArc(center: CGPoint(x: x + w * 0.55, y: y + h * 0.17), radius: w * 0.27,
                    startAngle: .degrees(180), endAngle: .degrees(0), clockwise: false)
        path.addLine(to: CGPoint(x: x + w * 0.82, y: y + h * 0.86))
        // Bottom turn back up the left side.
        path.addArc(center: CGPoint(x: x + w * 0.47, y: y + h * 0.86), radius: w * 0.35,
                    startAngle: .degrees(0), endAngle: .degrees(180), clockwise: false)
        path.addLine(to: CGPoint(x: x + w * 0.12, y: y + h * 0.36))
        return path
    }
}

struct PaperclipView: View {
    var body: some View {
        let steel = LinearGradient(colors: [Color(white: 0.92), Color(white: 0.62), Color(white: 0.85)],
                                   startPoint: .leading, endPoint: .trailing)
        ZStack {
            PaperclipShape()
                .stroke(Color.black.opacity(0.18), style: StrokeStyle(lineWidth: 3.2, lineCap: .round, lineJoin: .round))
                .offset(x: 0.6, y: 1.2)
                .blur(radius: 0.8)
            PaperclipShape()
                .stroke(steel, style: StrokeStyle(lineWidth: 2.4, lineCap: .round, lineJoin: .round))
            PaperclipShape()
                .stroke(.white.opacity(0.7), style: StrokeStyle(lineWidth: 0.6, lineCap: .round, lineJoin: .round))
                .offset(x: -0.4, y: -0.3)
        }
        .allowsHitTesting(false)
    }
}

// MARK: - AppKit bridges

/// Starts a multi-file drag on mouse drag and reports plain clicks.
struct FileDragSource<Content: View>: NSViewRepresentable {
    let urls: () -> [URL]
    var onClick: (NSEvent.ModifierFlags) -> Void = { _ in }
    var onDoubleClick: () -> Void = {}
    @ViewBuilder let content: () -> Content

    func makeNSView(context: Context) -> DragSourceNSView {
        let view = DragSourceNSView()
        let hosting = NSHostingView(rootView: content())
        hosting.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(hosting)
        NSLayoutConstraint.activate([
            hosting.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            hosting.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            hosting.topAnchor.constraint(equalTo: view.topAnchor),
            hosting.bottomAnchor.constraint(equalTo: view.bottomAnchor),
        ])
        update(view)
        return view
    }

    func updateNSView(_ view: DragSourceNSView, context: Context) {
        (view.subviews.first as? NSHostingView<Content>)?.rootView = content()
        update(view)
    }

    private func update(_ view: DragSourceNSView) {
        view.urls = urls
        view.onClick = onClick
        view.onDoubleClick = onDoubleClick
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView: DragSourceNSView, context: Context) -> CGSize? {
        nsView.subviews.first?.fittingSize
    }
}

final class DragSourceNSView: NSView, NSDraggingSource {
    var urls: () -> [URL] = { [] }
    var onClick: (NSEvent.ModifierFlags) -> Void = { _ in }
    var onDoubleClick: () -> Void = {}
    private var mouseDownEvent: NSEvent?

    // Children must not swallow the mouse; this view handles every click and drag.
    override func hitTest(_ point: NSPoint) -> NSView? {
        frame.contains(point) ? self : nil
    }

    override func mouseDown(with event: NSEvent) {
        mouseDownEvent = event
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = mouseDownEvent else { return }
        let distance = hypot(event.locationInWindow.x - start.locationInWindow.x,
                             event.locationInWindow.y - start.locationInWindow.y)
        guard distance > 3 else { return }
        mouseDownEvent = nil
        let items = urls().map { url -> NSDraggingItem in
            let item = NSDraggingItem(pasteboardWriter: url as NSURL)
            let icon = NSWorkspace.shared.icon(forFile: url.path)
            icon.size = NSSize(width: 48, height: 48)
            let origin = convert(event.locationInWindow, from: nil)
            item.setDraggingFrame(NSRect(x: origin.x - 24, y: origin.y - 24, width: 48, height: 48), contents: icon)
            return item
        }
        guard !items.isEmpty else { return }
        beginDraggingSession(with: items, event: event, source: self)
    }

    override func mouseUp(with event: NSEvent) {
        guard mouseDownEvent != nil else { return }
        mouseDownEvent = nil
        if event.clickCount >= 2 { onDoubleClick() } else { onClick(event.modifierFlags) }
    }

    func draggingSession(_ session: NSDraggingSession,
                         sourceOperationMaskFor context: NSDraggingContext) -> NSDragOperation {
        context == .outsideApplication ? [.copy, .move, .link, .generic] : .copy
    }
}

/// Lets the user move the borderless Float by dragging this area.
struct WindowDragHandle: NSViewRepresentable {
    var onClick: (() -> Void)?
    var menuItems: [(String, () -> Void)] = []

    func makeNSView(context: Context) -> WindowDragNSView {
        let view = WindowDragNSView()
        updateNSView(view, context: context)
        return view
    }

    func updateNSView(_ view: WindowDragNSView, context: Context) {
        view.onClick = onClick
        view.menuItems = menuItems
    }
}

final class WindowDragNSView: NSView {
    var onClick: (() -> Void)?
    var menuItems: [(String, () -> Void)] = []

    override func mouseDown(with event: NSEvent) {
        guard let window else { return }
        if event.modifierFlags.contains(.control) {
            super.mouseDown(with: event)
            return
        }
        let start = window.frame.origin
        window.performDrag(with: event)
        if window.frame.origin == start { onClick?() }
    }

    override func menu(for event: NSEvent) -> NSMenu? {
        guard !menuItems.isEmpty else { return nil }
        let menu = NSMenu()
        for (title, action) in menuItems {
            let item = NSMenuItem(title: title, action: #selector(ClosureTarget.invoke), keyEquivalent: "")
            let target = ClosureTarget(action)
            item.target = target
            item.representedObject = target
            menu.addItem(item)
        }
        return menu
    }
}

/// Lets an NSMenuItem run a Swift closure; the item keeps it alive through `representedObject`.
final class ClosureTarget: NSObject {
    private let action: () -> Void

    init(_ action: @escaping () -> Void) {
        self.action = action
    }

    @objc func invoke() {
        action()
    }
}
