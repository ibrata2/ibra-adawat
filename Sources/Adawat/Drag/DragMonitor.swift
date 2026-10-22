import AppKit

/// Watches for system-wide file drags and modifier presses during them.
///
/// It reads `NSEvent.pressedMouseButtons`, `NSEvent.modifierFlags` and the drag pasteboard, so it needs no
/// Accessibility or Input Monitoring permission. While the mouse is up it only glances at the button
/// a few times a second; once the button goes down it samples at 60 Hz until the button is released.
@MainActor
final class DragMonitor {
    var onModifierTap: ((ModifierKey, [URL], NSPoint) -> Void)?
    var onDragEnded: (() -> Void)?

    private let dragPasteboard = NSPasteboard(name: .drag)
    private var idleTimer: Timer?
    private var activeTimer: Timer?
    private var mouseMonitors: [Any] = []

    /// The drag pasteboard's change count the last time the mouse was up. A drag in progress always
    /// writes the pasteboard, so any newer count means a drag started; sampling while the mouse is up,
    /// rather than at mouse-down, keeps a drag that starts within one sample from being missed.
    private var idleChangeCount: Int
    private var dragActive = false
    private var lastFlags: NSEvent.ModifierFlags = []

    init() {
        idleChangeCount = dragPasteboard.changeCount
    }

    func start() {
        guard idleTimer == nil else { return }
        // Mouse events can be observed globally without any permission; they wake sampling instantly.
        if let global = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged], handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.beginActiveSampling() }
        }) {
            mouseMonitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged], handler: { [weak self] event in
            MainActor.assumeIsolated { self?.beginActiveSampling() }
            return event
        }) {
            mouseMonitors.append(local)
        }
        // A slow fallback in case a press slips past the monitors (for example over a secure input field).
        idleTimer = schedule(every: 0.2) { [weak self] in
            guard let self else { return }
            if NSEvent.pressedMouseButtons & 1 != 0 {
                self.beginActiveSampling()
            } else {
                self.idleChangeCount = self.dragPasteboard.changeCount
            }
        }
        DebugLog.write("drag monitor started, pasteboard count \(idleChangeCount)")
    }

    func stop() {
        idleTimer?.invalidate()
        activeTimer?.invalidate()
        idleTimer = nil
        activeTimer = nil
        mouseMonitors.forEach(NSEvent.removeMonitor)
        mouseMonitors = []
    }

    private func beginActiveSampling() {
        guard activeTimer == nil else { return }
        lastFlags = NSEvent.modifierFlags.intersection(ModifierKey.trackedFlags)
        activeTimer = schedule(every: 1.0 / 60.0) { [weak self] in self?.sample() }
        sample()
    }

    private func schedule(every interval: TimeInterval, _ body: @escaping @MainActor () -> Void) -> Timer {
        let timer = Timer(timeInterval: interval, repeats: true) { _ in MainActor.assumeIsolated { body() } }
        RunLoop.main.add(timer, forMode: .common)
        return timer
    }

    private func sample() {
        let mouseDown = NSEvent.pressedMouseButtons & 1 != 0
        guard mouseDown else {
            if dragActive {
                DebugLog.write("drag ended")
                onDragEnded?()
            }
            dragActive = false
            idleChangeCount = dragPasteboard.changeCount
            activeTimer?.invalidate()
            activeTimer = nil
            return
        }
        if !dragActive, dragPasteboard.changeCount != idleChangeCount,
           dragPasteboard.types?.contains(.fileURL) == true {
            dragActive = true
            DebugLog.write("drag started (count \(dragPasteboard.changeCount))")
        }
        trackModifiers()
    }

    /// Any modifier pressed during a drag opens (or switches to) its wheel at once, even while other
    /// modifiers are still held, so holding one key and pressing another changes wheels.
    private func trackModifiers() {
        let flags = NSEvent.modifierFlags.intersection(ModifierKey.trackedFlags)
        defer { lastFlags = flags }
        let pressed = flags.subtracting(lastFlags)
        guard dragActive, !pressed.isEmpty,
              let key = ModifierKey.allCases.last(where: { pressed.contains($0.flag) }) else { return }
        let files = readFiles()
        DebugLog.write("\(key.name) pressed during drag with \(files.count) file(s)")
        guard !files.isEmpty else { return }
        onModifierTap?(key, files, NSEvent.mouseLocation)
    }

    private func readFiles() -> [URL] {
        let options: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        return dragPasteboard.readObjects(forClasses: [NSURL.self], options: options) as? [URL] ?? []
    }
}
