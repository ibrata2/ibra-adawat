import AppKit
import SwiftUI

enum AppLanguage: String, CaseIterable, Identifiable {
    case arabic = "ar", english = "en"

    var id: String { rawValue }

    /// Each language names itself, so the picker is readable whichever language is showing.
    var title: String { self == .arabic ? "العربية" : "English" }

    /// The language this run of the app is showing.
    static var current: AppLanguage { L10n.isArabic ? .arabic : .english }
}

/// Translations for every visible string. Keys are the English text; `ar.lproj/Localizable.strings` holds
/// exact matches, and `ar.lproj/Patterns.strings` holds templates for text with numbers and names in it,
/// such as "Converting 2 of 5", written with (.+) for each value and \1, \2… in the translation.
enum L10n {
    static let isArabic: Bool = {
        let preferred = (UserDefaults.standard.array(forKey: "AppleLanguages") as? [String])?.first ?? Locale.preferredLanguages.first ?? "en"
        return preferred.hasPrefix("ar")
    }()

    /// Arabic windows read right to left; timelines and canvases opt back into left to right.
    static var direction: LayoutDirection { isArabic ? .rightToLeft : .leftToRight }

    private static let strings: [String: String] = load("Localizable")
    private static let patterns: [(NSRegularExpression, String)] = load("Patterns").compactMap { key, value in
        (try? NSRegularExpression(pattern: "^" + key + "$", options: [.dotMatchesLineSeparators])).map { ($0, value) }
    }.sorted { $0.0.pattern.count > $1.0.pattern.count }

    private static func load(_ table: String) -> [String: String] {
        guard isArabic, let url = Bundle.main.url(forResource: table, withExtension: "strings", subdirectory: nil, localization: "ar"),
              let dictionary = NSDictionary(contentsOf: url) as? [String: String] else { return [:] }
        return dictionary
    }

    /// The text in the app's language. Unknown text comes back unchanged.
    static func t(_ english: String) -> String {
        guard isArabic, !english.isEmpty else { return english }
        if let exact = strings[english] { return exact }
        // Keep a trailing ellipsis or colon outside the lookup.
        for suffix in ["…", ":"] where english.hasSuffix(suffix) {
            let base = String(english.dropLast())
            if let exact = strings[base] { return exact + suffix }
        }
        let range = NSRange(english.startIndex..., in: english)
        for (regex, template) in patterns {
            guard let match = regex.firstMatch(in: english, range: range) else { continue }
            var result = template
            // Captured values (file names, counts, tool titles) are translated too when they are known words.
            for group in stride(from: match.numberOfRanges - 1, through: 1, by: -1) {
                guard let captured = Range(match.range(at: group), in: english) else { continue }
                result = result.replacingOccurrences(of: "\\\(group)", with: t(String(english[captured])))
            }
            return result
        }
        return english
    }

    /// Saves the language for this app and starts a fresh copy of it, since bundles pick their language at launch.
    static func apply(_ language: AppLanguage) {
        UserDefaults.standard.set([language.rawValue], forKey: "AppleLanguages")
    }

    static func relaunch() {
        let path = Bundle.main.bundlePath
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/bin/sh")
        task.arguments = ["-c", "sleep 0.6; /usr/bin/open \"\(path)\""]
        try? task.run()
        NSApp.terminate(nil)
    }
}
