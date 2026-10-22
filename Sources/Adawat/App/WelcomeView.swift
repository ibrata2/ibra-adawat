import AppKit
import ServiceManagement
import SwiftUI

extension Notification.Name {
    /// Posted when the wheel opens; the welcome guide uses it to tick off the practice step.
    static let adawatWheelShown = Notification.Name("AdawatWheelShown")
}

@MainActor
final class WelcomeController {
    static let shared = WelcomeController()
    private var window: NSWindow?

    func show() {
        if window == nil { window = Self.makeWindow { [weak self] in self?.close() } }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    /// The guide's window, opened on `page`; snapshots use the later pages.
    static func makeWindow(page: Int = 0, onDone: @escaping () -> Void) -> NSWindow {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 500),
                              styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.isMovableByWindowBackground = true
        window.isReleasedWhenClosed = false
        window.appearance = NSAppearance(named: .aqua)
        window.backgroundColor = NSColor(white: 0.975, alpha: 1)
        let model = WelcomeModel()
        model.page = page
        window.contentView = NSHostingView(rootView: WelcomeView(model: model, onDone: onDone).environment(\.layoutDirection, L10n.direction))
        window.center()
        return window
    }

    func close() {
        AppSettings.shared.hasSeenWelcome = true
        window?.close()
        window = nil
    }
}

@MainActor
final class WelcomeModel: ObservableObject {
    @Published var page = 0
    @Published var pressed: NSEvent.ModifierFlags = []
    @Published var practiced = false
    @Published var launchAtLogin = SMAppService.mainApp.status == .enabled
    let practiceFile: URL?
    private var timer: Timer?
    private var observer: Any?

    static let pageCount = 4

    init() {
        practiceFile = try? Self.makePracticeFile()
        // Light the keycaps while their keys are held, so the shortcut is easy to find.
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.pressed = NSEvent.modifierFlags.intersection(ModifierKey.trackedFlags) }
        }
        observer = NotificationCenter.default.addObserver(forName: .adawatWheelShown, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { withAnimation { self?.practiced = true } }
        }
    }

    func stop() {
        timer?.invalidate()
        if let observer { NotificationCenter.default.removeObserver(observer) }
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            HUDController.shared.show(L10n.t("Allow Adawat in macOS Login Items to finish setup."), style: .failure)
        }
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    /// A small colorful photo to practice on, kept in Application Support so results land beside it.
    private static func makePracticeFile() throws -> URL {
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Adawat/Practice", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let url = folder.appendingPathComponent("Practice Photo.png")
        if FileManager.default.fileExists(atPath: url.path) { return url }
        let size = 600
        let space = CGColorSpace(name: CGColorSpace.sRGB)!
        guard let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0, space: space,
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return url }
        let gradient = CGGradient(colorsSpace: space, colors: [CGColor(srgbRed: 0.50, green: 0.44, blue: 1, alpha: 1),
                                                               CGColor(srgbRed: 0.23, green: 0.16, blue: 0.66, alpha: 1)] as CFArray,
                                  locations: [0, 1])!
        ctx.drawLinearGradient(gradient, start: .zero, end: CGPoint(x: size, y: size), options: [])
        ctx.setFillColor(CGColor(srgbRed: 0.49, green: 0.95, blue: 0.78, alpha: 0.95))
        ctx.fillEllipse(in: CGRect(x: 170, y: 170, width: 260, height: 260))
        if let image = ctx.makeImage() { try ImageEngine.write(image, as: .png, to: url, quality: 1, metadata: [:]) }
        return url
    }
}

struct WelcomeView: View {
    @ObservedObject var model: WelcomeModel
    let onDone: () -> Void
    @ObservedObject private var settings = AppSettings.shared

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch model.page {
                case 0: intro
                case 1: practice
                case 2: preferences
                default: finish
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .transition(.opacity)
            footer
        }
        .frame(width: 560, height: 500)
        .environment(\.colorScheme, .light)
        .foregroundStyle(Theme.ink)
        .tint(Theme.accent)
        .animation(.easeOut(duration: 0.2), value: model.page)
    }

    // MARK: Pages

    private var intro: some View {
        VStack(spacing: 18) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 128, height: 128)
                .shadow(color: Theme.accent.opacity(0.25), radius: 16, y: 8)
            Text(L10n.t("Welcome to Adawat")).font(.system(size: 28, weight: .bold))
            Text(L10n.t("Convert, compress and edit any file while you drag it. Everything stays on your Mac."))
                .font(.system(size: 14)).foregroundStyle(Theme.muted).multilineTextAlignment(.center).frame(width: 380)
        }
    }

    private var practice: some View {
        VStack(spacing: 22) {
            Text(L10n.t("Try it now")).font(.system(size: 22, weight: .bold))
            HStack(alignment: .top, spacing: 26) {
                VStack(spacing: 10) {
                    stepBadge(1)
                    Text(L10n.t("Step 1. Drag the photo.")).font(.system(size: 12, weight: .medium))
                    if let file = model.practiceFile {
                        FileDragSource(urls: { [file] }) {
                            VStack(spacing: 6) {
                                Image(nsImage: NSImage(contentsOf: file) ?? NSImage()).resizable().frame(width: 84, height: 84)
                                    .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                                    .shadow(color: .black.opacity(0.18), radius: 6, y: 3)
                                Text(file.lastPathComponent).font(.system(size: 10)).foregroundStyle(Theme.muted)
                            }
                            .padding(8)
                        }
                        .frame(width: 120, height: 120)
                        .help(L10n.t("Drag this file"))
                    }
                }
                VStack(spacing: 10) {
                    stepBadge(2)
                    Text(L10n.t("Step 2. While dragging, press a key.")).font(.system(size: 12, weight: .medium))
                    HStack(spacing: 10) {
                        ForEach(WheelMode.allCases) { mode in
                            let key = settings.modifier(for: mode)
                            VStack(spacing: 6) {
                                keycap(key, lit: model.pressed.contains(key.flag))
                                Text(L10n.t(mode.title)).font(.system(size: 10)).foregroundStyle(Theme.muted)
                            }
                        }
                    }
                }
            }
            HStack(spacing: 8) {
                Image(systemName: model.practiced ? "checkmark.circle.fill" : "circle.dashed")
                    .foregroundStyle(model.practiced ? Theme.accent : Theme.muted)
                Text(model.practiced ? L10n.t("That's it. Drop the photo on a format to convert it.")
                                     : L10n.t("The wheel stays open until you drop."))
                    .font(.system(size: 12)).foregroundStyle(model.practiced ? Theme.ink : Theme.muted)
            }
            .padding(.horizontal, 14).padding(.vertical, 8)
            .background(Capsule().fill(model.practiced ? Theme.accent.opacity(0.12) : Color.black.opacity(0.04)))
        }
    }

    private var preferences: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text(L10n.t("Make it yours")).font(.system(size: 22, weight: .bold)).frame(maxWidth: .infinity)
            VStack(alignment: .leading, spacing: 14) {
                ToolRow(title: L10n.t("Language")) {
                    Picker("", selection: $settings.language) {
                        ForEach(AppLanguage.allCases) { Text($0.title).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                if settings.language != AppLanguage.current {
                    HStack {
                        Text(L10n.t("Adawat restarts to change the language.")).font(.system(size: 11)).foregroundStyle(Theme.muted)
                        Spacer()
                        Button(L10n.t("Restart Now")) { L10n.relaunch() }.buttonStyle(.tool)
                    }
                }
                Divider()
                switchRow("Open Adawat when you log in", isOn: Binding(get: { model.launchAtLogin },
                                                                     set: { model.setLaunchAtLogin($0) }))
                Text(L10n.t("Adawat lives in your menu bar, so it is ready whenever you drag a file."))
                    .font(.system(size: 11)).foregroundStyle(Theme.muted)
                Divider()
                switchRow("Sound and haptic feedback", isOn: $settings.playSounds)
                switchRow("Show results in a Float", isOn: $settings.showResultsInFloat)
            }
            .font(.system(size: 13))
            .padding(18)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color.white))
            .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.black.opacity(0.06)))
        }
        .frame(width: 420)
    }

    private var finish: some View {
        VStack(spacing: 18) {
            HStack(spacing: 6) {
                Image(nsImage: StatusBarController.menuBarIcon).renderingMode(.template)
                    .padding(6).background(RoundedRectangle(cornerRadius: 6).fill(Color.black.opacity(0.06)))
                Image(systemName: "arrow.up.right").foregroundStyle(Theme.muted)
            }
            Text(L10n.t("You're all set")).font(.system(size: 24, weight: .bold))
            Text(L10n.t("Find the Adawat icon near the clock in your menu bar for Floats, Settings and this guide."))
                .font(.system(size: 13)).foregroundStyle(Theme.muted).multilineTextAlignment(.center).frame(width: 380)
        }
    }

    // MARK: Pieces

    private var footer: some View {
        HStack {
            if model.page > 0 {
                Button(L10n.t("Back")) { model.page -= 1 }.buttonStyle(.tool)
            }
            Spacer()
            HStack(spacing: 6) {
                ForEach(0..<WelcomeModel.pageCount, id: \.self) { index in
                    Circle().fill(index == model.page ? Theme.accent : Color.black.opacity(0.15)).frame(width: 7, height: 7)
                }
            }
            Spacer()
            Button(model.page == WelcomeModel.pageCount - 1 ? L10n.t("Get Started") : L10n.t("Continue")) {
                if model.page == WelcomeModel.pageCount - 1 {
                    model.stop()
                    onDone()
                } else {
                    model.page += 1
                }
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.accent)
            .keyboardShortcut(.defaultAction)
        }
        .padding(.horizontal, 24)
        .frame(height: 64)
    }

    /// A label with its switch on the far edge, like System Settings.
    private func switchRow(_ title: String, isOn: Binding<Bool>) -> some View {
        HStack {
            Text(L10n.t(title))
            Spacer()
            Toggle(L10n.t(title), isOn: isOn).toggleStyle(.switch).labelsHidden()
        }
    }

    private func stepBadge(_ number: Int) -> some View {
        Text(L10n.t("\(number)")).font(.system(size: 12, weight: .bold)).foregroundStyle(.white)
            .frame(width: 24, height: 24).background(Circle().fill(Theme.accent))
    }

    private func keycap(_ key: ModifierKey, lit: Bool) -> some View {
        VStack(spacing: 2) {
            Text(key.symbol).font(.system(size: 18, weight: .medium))
            Text(key.name.lowercased()).font(.system(size: 9))
        }
        .frame(width: 62, height: 54)
        .foregroundStyle(lit ? Theme.accent : Theme.ink.opacity(0.75))
        .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(lit ? Theme.accent.opacity(0.12) : Color.white))
        .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(lit ? Theme.accent : Color.black.opacity(0.12), lineWidth: lit ? 1.5 : 1))
        .shadow(color: .black.opacity(lit ? 0 : 0.08), radius: 0, y: lit ? 0 : 2)
        .scaleEffect(lit ? 0.96 : 1)
        .animation(.easeOut(duration: 0.08), value: lit)
    }
}
