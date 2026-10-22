import AppKit

@MainActor
final class StatusBarController: NSObject, NSMenuDelegate {
    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let menu = NSMenu()

    override init() {
        super.init()
        if let button = item.button {
            button.image = Self.menuBarIcon
            button.toolTip = "Adawat — drag files, then press a modifier key"
        }
        menu.delegate = self
        item.menu = menu
    }

    /// The app icon's citrus slice as a template: a rind, eight segment lines and a hub.
    /// A template version of the app icon: six petals around a hub, the top-right one lifted.
    static let menuBarIcon: NSImage = {
        let image = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { rect in
            let center = NSPoint(x: rect.midX, y: rect.midY)
            NSColor.black.setFill()
            let inner: CGFloat = 3.0, outer: CGFloat = 8.2, gap: CGFloat = 0.75
            for index in 0..<6 {
                let step = CGFloat.pi / 3
                let mid = CGFloat.pi / 2 - step / 2 - CGFloat(index) * step
                let lift: CGFloat = index == 0 ? 0.9 : 0
                let c = NSPoint(x: center.x + cos(mid) * lift, y: center.y + sin(mid) * lift)
                let r = outer - (index == 0 ? 0.9 : 0)
                let a0 = mid - step / 2, a1 = mid + step / 2
                let path = NSBezierPath()
                path.appendArc(withCenter: c, radius: r, startAngle: (a0 + asin(gap / r)) * 180 / .pi,
                               endAngle: (a1 - asin(gap / r)) * 180 / .pi)
                path.appendArc(withCenter: c, radius: inner, startAngle: (a1 - asin(gap / inner)) * 180 / .pi,
                               endAngle: (a0 + asin(gap / inner)) * 180 / .pi, clockwise: true)
                path.close()
                path.fill()
            }
            NSBezierPath(ovalIn: NSRect(x: center.x - 1.5, y: center.y - 1.5, width: 3, height: 3)).fill()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "Adawat"
        return image
    }()

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let settings = AppSettings.shared

        let hint = NSMenuItem(title: L10n.t("Drag files, then press \(settings.convertModifier.symbol) \(settings.toolsModifier.symbol) \(settings.actionsModifier.symbol)"),
                              action: nil, keyEquivalent: "")
        hint.isEnabled = false
        menu.addItem(hint)
        menu.addItem(.separator())

        menu.addItem(action("New Float", #selector(newFloat), key: "n"))
        let close = action("Close All Floats", #selector(closeFloats), key: "")
        close.isEnabled = FloatManager.shared.hasFloats
        menu.addItem(close)
        menu.addItem(action("Create QR Code…", #selector(createQR), key: ""))
        menu.addItem(.separator())
        menu.addItem(action("Welcome Guide…", #selector(showWelcome), key: ""))
        menu.addItem(action("Settings…", #selector(showSettings), key: ","))
        menu.addItem(.separator())
        menu.addItem(action("Quit Adawat", #selector(quit), key: "q"))
    }

    private func action(_ title: String, _ selector: Selector, key: String) -> NSMenuItem {
        let item = NSMenuItem(title: L10n.t(title), action: selector, keyEquivalent: key)
        item.target = self
        return item
    }

    @objc private func newFloat() { FloatManager.shared.createFloat() }
    @objc private func closeFloats() { FloatManager.shared.closeAll() }
    @objc private func createQR() { QRWindow.openFromMenu() }
    @objc private func showWelcome() { WelcomeController.shared.show() }
    @objc private func showSettings() { SettingsController.shared.show() }
    @objc private func quit() { NSApp.terminate(nil) }
}
