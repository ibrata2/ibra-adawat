import AppKit

@MainActor
final class FloatManager {
    static let shared = FloatManager()

    private(set) var floats: [FloatWindowController] = []

    var hasFloats: Bool { !floats.isEmpty }

    @discardableResult
    func createFloat(with files: [URL] = [], near point: NSPoint = NSEvent.mouseLocation) -> FloatWindowController {
        let controller = FloatWindowController(files: files)
        controller.onClose = { [weak self, weak controller] in
            self?.floats.removeAll { $0 === controller }
        }
        floats.append(controller)
        controller.show(near: point)
        return controller
    }

    /// Puts results into the newest open Float, or a new one.
    func showResults(_ files: [URL]) {
        if let latest = floats.last {
            latest.add(files)
            latest.bringToFront()
        } else {
            createFloat(with: files)
        }
    }

    func closeAll() {
        floats.forEach { $0.close() }
    }
}
