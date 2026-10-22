import AppKit
import ServiceManagement
import SwiftUI

@MainActor
final class SettingsController {
    static let shared = SettingsController()
    private var window: NSWindow?

    func show() {
        if window == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 520),
                                  styleMask: [.titled, .closable], backing: .buffered, defer: false)
            window.title = "Adawat Settings"
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: SettingsView().environment(\.layoutDirection, L10n.direction))
            window.center()
            self.window = window
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}

struct SettingsView: View {
    @ObservedObject private var settings = AppSettings.shared
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled
    @State private var loginError: String?

    var body: some View {
        Form {
            Section(L10n.t("Shortcuts while dragging")) {
                modifierPicker(L10n.t("Convert wheel"), selection: $settings.convertModifier)
                modifierPicker(L10n.t("Tools wheel"), selection: $settings.toolsModifier)
                modifierPicker(L10n.t("Actions wheel"), selection: $settings.actionsModifier)
                Text(L10n.t("Drag files anywhere, then press the key to open its wheel right away."))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section(L10n.t("Results")) {
                Picker(L10n.t("Save results"), selection: $settings.outputLocation) {
                    ForEach(OutputLocation.allCases) { Text(L10n.t($0.title)).tag($0) }
                }
                Toggle(L10n.t("Show results in a Float"), isOn: $settings.showResultsInFloat)
                Toggle(L10n.t("Sound and haptic feedback"), isOn: $settings.playSounds)
            }

            Section(L10n.t("Compression")) {
                Picker(L10n.t("Default preset"), selection: $settings.compressPreset) {
                    ForEach(CompressPreset.allCases) { Text(L10n.t($0.title)).tag($0) }
                }
                Picker(L10n.t("Resize longest edge"), selection: $settings.resizeMaxPixels) {
                    ForEach([640, 1024, 1280, 1920, 2560, 3840], id: \.self) { Text(L10n.t("\($0) px")).tag($0) }
                }
            }

            Section(L10n.t("Video")) {
                Picker(L10n.t("Quality"), selection: $settings.videoQuality) {
                    ForEach(VideoQuality.allCases) { Text(L10n.t($0.title)).tag($0) }
                }
                LabeledContent("ffmpeg") {
                    Text(ExternalTool.ffmpeg.url?.path ?? "Not found — brew install ffmpeg")
                        .font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
                }
            }

            Section(L10n.t("General")) {
                Toggle(L10n.t("Launch Adawat at login"), isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { _, enabled in setLaunchAtLogin(enabled) }
                if let loginError {
                    Text(loginError).font(.caption).foregroundStyle(.orange)
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 460, height: 520)
        .tint(Theme.accent)
    }

    private func modifierPicker(_ title: String, selection: Binding<ModifierKey>) -> some View {
        Picker(title, selection: selection) {
            ForEach(ModifierKey.allCases) { key in
                Text(L10n.t("\(key.symbol)  \(key.name)")).tag(key)
            }
        }
    }

    private func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            loginError = nil
        } catch {
            loginError = "Could not change login item: \(error.localizedDescription)"
            launchAtLogin = SMAppService.mainApp.status == .enabled
        }
    }
}
