import AppKit

@main
enum AdawatApp {
    /// NSApplication holds its delegate weakly, so keep a strong reference for the app's lifetime.
    private static var delegate: AppDelegate?

    static func main() {
        let arguments = CommandLine.arguments
        if arguments.count >= 2, ["--list", "--run", "--snapshot", "--snapshot-window", "--doctor", "--compress", "--trim", "--annotate-sample", "--edit-sample", "--metadata-sample", "--media", "--qr-sample", "--demo-gif"].contains(arguments[1]) {
            MainActor.assumeIsolated { CommandLineRunner.run(Array(arguments.dropFirst())) }
            return
        }

        let app = NSApplication.shared
        let delegate = MainActor.assumeIsolated { AppDelegate() }
        Self.delegate = delegate
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        app.run()
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusBar: StatusBarController?
    private let dragMonitor = DragMonitor()

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusBar = StatusBarController()

        dragMonitor.onModifierTap = { modifier, files, location in
            guard let mode = AppSettings.shared.mode(for: modifier) else { return }
            WheelController.shared.show(mode: mode, files: files, at: location)
        }
        dragMonitor.onDragEnded = {
            WheelController.shared.dragDidEnd()
        }
        dragMonitor.start()

        let arguments = CommandLine.arguments
        if arguments.count >= 3, arguments[1] == "--preview" {
            showPreview(mode: WheelMode(rawValue: arguments[2]) ?? .convert,
                        files: arguments.dropFirst(3).map { URL(fileURLWithPath: $0) })
        } else if !AppSettings.shared.hasSeenWelcome {
            WelcomeController.shared.show()
        }
    }

    /// Shows the wheel and a Float on the main screen without a drag, for screenshots.
    private func showPreview(mode: WheelMode, files: [URL]) {
        guard let screen = NSScreen.main?.visibleFrame else { return }
        WheelController.shared.show(mode: mode, files: files, at: NSPoint(x: screen.midX - 200, y: screen.midY))
        WheelController.shared.previewHover(index: 1)
        FloatManager.shared.createFloat(with: files, near: NSPoint(x: screen.midX + 60, y: screen.midY))
        HUDController.shared.show(L10n.t("Saved photo.webp"), style: .success)
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        SettingsController.shared.show()
        return true
    }
}
