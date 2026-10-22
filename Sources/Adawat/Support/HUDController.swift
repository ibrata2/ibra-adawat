import AppKit
import SwiftUI

@MainActor
final class HUDModel: ObservableObject {
    enum Style { case working, success, failure }

    @Published var text = ""
    @Published var style: Style = .working
    @Published var progress: Double?
}

/// A small toast at the top of the screen that reports what Adawat is doing.
@MainActor
final class HUDController {
    static let shared = HUDController()

    private let model = HUDModel()
    private var panel: NSPanel?
    private var hideWork: DispatchWorkItem?

    func show(_ text: String, style: HUDModel.Style) {
        model.text = L10n.t(text)
        model.style = style
        model.progress = nil
        let panel = self.panel ?? makePanel()
        self.panel = panel
        position(panel)
        if !panel.isVisible {
            panel.alphaValue = 0
            panel.orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.15
            panel.animator().alphaValue = 1
        }

        hideWork?.cancel()
        guard style != .working else { return }
        let work = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + (style == .failure ? 4 : 2.2), execute: work)
    }

    func update(_ text: String) {
        guard model.style == .working else { return }
        model.text = L10n.t(text)
    }

    func setProgress(_ fraction: Double) {
        guard model.style == .working else { return }
        model.progress = min(1, max(0, fraction))
    }

    private func hide() {
        guard let panel else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            panel.animator().alphaValue = 0
        }, completionHandler: {
            MainActor.assumeIsolated { panel.orderOut(nil) }
        })
    }

    private func position(_ panel: NSPanel) {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main
        guard let visible = screen?.visibleFrame else { return }
        let size = NSSize(width: 440, height: 76)
        panel.setFrame(NSRect(x: visible.midX - size.width / 2, y: visible.maxY - size.height - 8,
                              width: size.width, height: size.height), display: true)
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 440, height: 76),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .statusBar
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.contentView = NSHostingView(rootView: HUDView(model: model).environment(\.layoutDirection, L10n.direction))
        return panel
    }
}

struct HUDView: View {
    @ObservedObject var model: HUDModel

    var body: some View {
        HStack(spacing: 9) {
            switch model.style {
            case .working:
                ProgressView().controlSize(.small).tint(Theme.accent)
            case .success:
                Image(systemName: "checkmark.circle.fill").foregroundStyle(Theme.accent)
            case .failure:
                Image(systemName: "exclamationmark.circle.fill").foregroundStyle(Theme.accent)
            }
            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 6) {
                    Text(model.text)
                        .font(Theme.rounded(12, .medium))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(2)
                    if let progress = model.progress, model.style == .working {
                        Text(L10n.t("\(Int(progress * 100))%"))
                            .font(Theme.rounded(11, .semibold))
                            .monospacedDigit()
                            .foregroundStyle(Theme.muted)
                    }
                }
                if let progress = model.progress, model.style == .working {
                    ProgressView(value: progress)
                        .progressViewStyle(.linear)
                        .tint(Theme.accent)
                        .frame(width: 220)
                        .animation(.easeOut(duration: 0.2), value: progress)
                }
            }
        }
        .font(.system(size: 13, weight: .semibold))
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
        .background(LightGlass(cornerRadius: 12))
        .environment(\.colorScheme, .light)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
