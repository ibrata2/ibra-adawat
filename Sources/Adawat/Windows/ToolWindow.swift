import AppKit
import SwiftUI

/// Lets a tool's SwiftUI content close the window it lives in.
@MainActor
final class ToolWindowHandle {
    fileprivate weak var window: NSWindow?

    func close() {
        window?.close()
    }
}

/// Small light windows for tools that need choices before they run.
@MainActor
enum ToolWindows {
    private static var open: [NSWindow] = []

    @discardableResult
    static func present<Content: View>(_ title: String, width: CGFloat, show: Bool = true,
                                       @ViewBuilder content: (ToolWindowHandle) -> Content) -> NSWindow {
        let handle = ToolWindowHandle()
        let root = content(handle)
            .frame(width: width)

            .environment(\.colorScheme, .light)
            .environment(\.layoutDirection, L10n.direction)
            .tint(Theme.accent)
        let host = NSHostingController(rootView: root)
        host.sizingOptions = [.preferredContentSize]
        // The hidden title bar would otherwise inset the content and pad the window's height;
        // the scaffold draws its own header in that space.
        host.safeAreaRegions = []

        let window = NSWindow(contentViewController: host)
        window.styleMask = [.titled, .closable, .fullSizeContentView]
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.title = title
        [.closeButton, .miniaturizeButton, .zoomButton].forEach { window.standardWindowButton($0)?.isHidden = true }
        window.isMovableByWindowBackground = true
        window.backgroundColor = NSColor(white: 0.975, alpha: 1)
        window.appearance = NSAppearance(named: .aqua)
        window.level = .floating
        window.isReleasedWhenClosed = false
        window.center()
        handle.window = window

        guard show else { return window }
        open.append(window)
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { note in
            MainActor.assumeIsolated { open.removeAll { $0 === note.object as? NSWindow } }
        }
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        return window
    }

    /// Draws a tool window's content into an image, for checking layouts without a screen capture.
    static func snapshot(_ window: NSWindow) -> NSBitmapImageRep? {
        guard let view = window.contentView else { return nil }
        view.layoutSubtreeIfNeeded()
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return nil }
        view.cacheDisplay(in: view.bounds, to: rep)
        return rep
    }
}

/// The frame every tool window shares: a round close button and centered title on top, the tool's
/// controls in the middle, and a bar of actions at the bottom (secondary on the left, primary on the right).
struct ToolScaffold<Content: View, Leading: View, Trailing: View>: View {
    let title: String
    let onClose: () -> Void
    @ViewBuilder var content: Content
    @ViewBuilder var leading: Leading
    @ViewBuilder var trailing: Trailing

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                Text(title)
                    .font(.system(size: 15, weight: .semibold))
                    .foregroundStyle(Theme.ink)
                HStack {
                    Button(action: onClose) {
                        Image(systemName: "xmark")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundStyle(Theme.ink.opacity(0.7))
                            .frame(width: 24, height: 24)
                            .background(Circle().fill(Color.black.opacity(0.06)))
                    }
                    .buttonStyle(.plain)
                    .keyboardShortcut(.cancelAction)
                    .help(L10n.t("Close"))
                    Spacer()
                }
            }
            .padding(.horizontal, 12)
            .frame(height: 44)
            Divider()
            content
                .padding(16)
                .frame(maxWidth: .infinity, alignment: .leading)
            Divider()
            HStack(spacing: 8) {
                leading
                Spacer()
                trailing
            }
            .padding(.horizontal, 16)
            .frame(height: 52)
        }
        .foregroundStyle(Theme.ink)
    }
}

/// The soft gray buttons tool windows use.
struct ToolButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .medium))
            .padding(.horizontal, 12)
            .frame(height: 26)
            .background(
                RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(Color.black.opacity(configuration.isPressed ? 0.14 : 0.08))
            )
            .foregroundStyle(Theme.ink.opacity(isEnabled ? 1 : 0.35))
    }
}

extension ButtonStyle where Self == ToolButtonStyle {
    static var tool: ToolButtonStyle { ToolButtonStyle() }
}

/// A labeled row: the title on the left, its control on the right.
struct ToolRow<Control: View>: View {
    let title: String
    @ViewBuilder var control: Control

    var body: some View {
        HStack(spacing: 12) {
            Text(title).font(.system(size: 13))
            Spacer(minLength: 8)
            control
        }
    }
}

enum ByteText {
    static func string(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

enum TimeText {
    /// "1:04.25"
    static func string(_ seconds: Double) -> String {
        let s = max(0, seconds)
        let minutes = Int(s) / 60
        return String(format: "%d:%05.2f", minutes, s - Double(minutes * 60))
    }

    /// Parses "1:04.25", "64.25" or "0:01:04.25" back to seconds.
    static func parse(_ text: String) -> Double? {
        let parts = text.trimmingCharacters(in: .whitespaces).split(separator: ":").map { Double($0) }
        guard !parts.isEmpty, parts.allSatisfy({ $0 != nil }) else { return nil }
        return parts.compactMap { $0 }.reduce(0) { $0 * 60 + $1 }
    }
}
