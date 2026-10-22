import AppKit
import Combine

/// A single modifier key the user can tap while dragging files.
enum ModifierKey: String, CaseIterable, Identifiable, Codable {
    case option, control, shift, command, function

    var id: String { rawValue }

    var flag: NSEvent.ModifierFlags {
        switch self {
        case .option: return .option
        case .control: return .control
        case .shift: return .shift
        case .command: return .command
        case .function: return .function
        }
    }

    var symbol: String {
        switch self {
        case .option: return "⌥"
        case .control: return "⌃"
        case .shift: return "⇧"
        case .command: return "⌘"
        case .function: return "fn"
        }
    }

    var name: String {
        switch self {
        case .option: return "Option"
        case .control: return "Control"
        case .shift: return "Shift"
        case .command: return "Command"
        case .function: return "Fn"
        }
    }

    static let trackedFlags: NSEvent.ModifierFlags = [.option, .control, .shift, .command, .function]

    init?(flags: NSEvent.ModifierFlags) {
        let relevant = flags.intersection(Self.trackedFlags)
        guard let match = Self.allCases.first(where: { relevant == $0.flag }) else { return nil }
        self = match
    }
}

enum WheelMode: String, CaseIterable, Identifiable {
    case convert, tools, actions

    var id: String { rawValue }

    var title: String {
        switch self {
        case .convert: return "Convert"
        case .tools: return "Tools"
        case .actions: return "Actions"
        }
    }

    var systemImage: String {
        switch self {
        case .convert: return "arrow.triangle.2.circlepath"
        case .tools: return "wrench.and.screwdriver"
        case .actions: return "bolt"
        }
    }
}

enum OutputLocation: String, CaseIterable, Identifiable {
    case nextToOriginal, downloads, desktop

    var id: String { rawValue }

    var title: String {
        switch self {
        case .nextToOriginal: return "Next to the original"
        case .downloads: return "Downloads folder"
        case .desktop: return "Desktop"
        }
    }
}

@MainActor
final class AppSettings: ObservableObject {
    static let shared = AppSettings()

    private let defaults = UserDefaults.standard

    @Published var convertModifier: ModifierKey { didSet { save(convertModifier.rawValue, "convertModifier") } }
    @Published var toolsModifier: ModifierKey { didSet { save(toolsModifier.rawValue, "toolsModifier") } }
    @Published var actionsModifier: ModifierKey { didSet { save(actionsModifier.rawValue, "actionsModifier") } }
    @Published var outputLocation: OutputLocation { didSet { save(outputLocation.rawValue, "outputLocation") } }
    @Published var showResultsInFloat: Bool { didSet { save(showResultsInFloat, "showResultsInFloat") } }
    @Published var playSounds: Bool { didSet { save(playSounds, "playSounds") } }
    @Published var imageQuality: Double { didSet { save(imageQuality, "imageQuality") } }
    @Published var resizeMaxPixels: Int { didSet { save(resizeMaxPixels, "resizeMaxPixels") } }
    @Published var videoQuality: VideoQuality { didSet { save(videoQuality.rawValue, "videoQuality") } }
    @Published var hasSeenWelcome: Bool { didSet { save(hasSeenWelcome, "hasSeenWelcome") } }
    /// Changing it takes effect after a restart (see L10n.relaunch).
    @Published var language: AppLanguage { didSet { L10n.apply(language) } }
    @Published var compressPreset: CompressPreset { didSet { save(compressPreset.rawValue, "compressPreset") } }
    /// 0 keeps the original dimensions.
    @Published var compressImageMaxPixelsRaw: Int { didSet { save(compressImageMaxPixelsRaw, "compressImageMaxPixels") } }
    @Published var compressVideoMaxHeightRaw: Int { didSet { save(compressVideoMaxHeightRaw, "compressVideoMaxHeight") } }

    var compressImageMaxPixels: Int? { compressImageMaxPixelsRaw > 0 ? compressImageMaxPixelsRaw : nil }
    var compressVideoMaxHeight: Int? { compressVideoMaxHeightRaw > 0 ? compressVideoMaxHeightRaw : nil }

    private init() {
        if defaults.integer(forKey: "settingsVersion") < 2 {
            for key in ["convertModifier", "toolsModifier", "actionsModifier"] { defaults.removeObject(forKey: key) }
            defaults.set(2, forKey: "settingsVersion")
        }
        convertModifier = ModifierKey(rawValue: defaults.string(forKey: "convertModifier") ?? "") ?? .shift
        toolsModifier = ModifierKey(rawValue: defaults.string(forKey: "toolsModifier") ?? "") ?? .command
        actionsModifier = ModifierKey(rawValue: defaults.string(forKey: "actionsModifier") ?? "") ?? .control
        outputLocation = OutputLocation(rawValue: defaults.string(forKey: "outputLocation") ?? "") ?? .nextToOriginal
        showResultsInFloat = defaults.object(forKey: "showResultsInFloat") as? Bool ?? true
        playSounds = defaults.object(forKey: "playSounds") as? Bool ?? true
        imageQuality = defaults.object(forKey: "imageQuality") as? Double ?? 0.75
        resizeMaxPixels = defaults.object(forKey: "resizeMaxPixels") as? Int ?? 1920
        videoQuality = VideoQuality(rawValue: defaults.string(forKey: "videoQuality") ?? "") ?? .balanced
        hasSeenWelcome = defaults.bool(forKey: "hasSeenWelcome")
        // Adawat speaks Arabic whenever Arabic is among the Mac's languages, unless the user picked English.
        let languages = (defaults.array(forKey: "AppleLanguages") as? [String]) ?? Locale.preferredLanguages
        if defaults.object(forKey: "languageChosen") == nil {
            defaults.set(true, forKey: "languageChosen")
            if languages.contains(where: { $0.hasPrefix("ar") }) { defaults.set(["ar"], forKey: "AppleLanguages") }
        }
        language = AppLanguage.current
        compressPreset = CompressPreset(rawValue: defaults.string(forKey: "compressPreset") ?? "") ?? .balanced
        compressImageMaxPixelsRaw = defaults.integer(forKey: "compressImageMaxPixels")
        compressVideoMaxHeightRaw = defaults.integer(forKey: "compressVideoMaxHeight")
    }

    private func save(_ value: Any, _ key: String) {
        defaults.set(value, forKey: key)
    }

    func mode(for modifier: ModifierKey) -> WheelMode? {
        if modifier == convertModifier { return .convert }
        if modifier == toolsModifier { return .tools }
        if modifier == actionsModifier { return .actions }
        return nil
    }

    func modifier(for mode: WheelMode) -> ModifierKey {
        switch mode {
        case .convert: return convertModifier
        case .tools: return toolsModifier
        case .actions: return actionsModifier
        }
    }
}

enum VideoQuality: String, CaseIterable, Identifiable {
    case high, balanced, small

    var id: String { rawValue }

    var title: String {
        switch self {
        case .high: return "High quality"
        case .balanced: return "Balanced"
        case .small: return "Smallest file"
        }
    }

    /// x264 constant rate factor used for re-encoding.
    var crf: Int {
        switch self {
        case .high: return 20
        case .balanced: return 24
        case .small: return 30
        }
    }
}
