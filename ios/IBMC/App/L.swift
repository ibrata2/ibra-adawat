import Foundation

/// Arabic or English text, picked by the app's language setting ("system" follows the phone).
enum L {
    static var language: String { UserDefaults.standard.string(forKey: "language") ?? "system" }

    static var isArabic: Bool {
        switch language {
        case "ar": return true
        case "en": return false
        default: return (Locale.preferredLanguages.first ?? "en").hasPrefix("ar")
        }
    }

    static func t(_ english: String, _ arabic: String) -> String { isArabic ? arabic : english }
}
