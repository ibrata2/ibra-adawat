import SwiftUI

/// The five looks, as on Windows: four glass styles and one that follows the phone.
enum Style: String, CaseIterable, Identifiable {
    case glassRedWhite = "glass-red-white", glassWhite = "glass-white", glassRedBlack = "glass-red-black", glassClear = "glass-clear", system

    var id: String { rawValue }

    var title: String {
        switch self {
        case .glassRedWhite: return L.t("Glass · white and red", "زجاجي أبيض وأحمر")
        case .glassWhite: return L.t("Glass · white", "زجاجي أبيض")
        case .glassRedBlack: return L.t("Glass · red and black", "زجاجي أحمر وأسود")
        case .glassClear: return L.t("Glass · clear", "زجاجي شفاف")
        case .system: return L.t("Match the phone", "متناسب مع ستايل الجهاز")
        }
    }

    static let brandRed = Color(red: 0.89, green: 0.10, blue: 0.17)

    var scheme: ColorScheme? {
        switch self {
        case .glassRedBlack: return .dark
        case .glassRedWhite, .glassWhite: return .light
        default: return nil
        }
    }

    var accent: Color {
        switch self {
        case .glassWhite: return Color(red: 0.23, green: 0.23, blue: 0.26)
        case .system: return .accentColor
        default: return Style.brandRed
        }
    }

    @ViewBuilder func background(dark: Bool) -> some View {
        switch self {
        case .glassRedWhite:
            LinearGradient(colors: [.white, Color(red: 1, green: 0.85, blue: 0.87), Color(red: 1, green: 0.93, blue: 0.94)], startPoint: .topLeading, endPoint: .bottomTrailing)
        case .glassWhite:
            LinearGradient(colors: [.white, Color(red: 0.93, green: 0.94, blue: 0.96)], startPoint: .top, endPoint: .bottom)
        case .glassRedBlack:
            LinearGradient(colors: [Color(red: 0.05, green: 0.03, blue: 0.04), Color(red: 0.25, green: 0.02, blue: 0.05), .black], startPoint: .topLeading, endPoint: .bottomTrailing)
        case .glassClear:
            LinearGradient(colors: dark ? [Color(white: 0.12), Color(white: 0.06)] : [Color(white: 0.97), Color(white: 0.9)], startPoint: .top, endPoint: .bottom)
        case .system:
            Color(uiColor: .systemGroupedBackground)
        }
    }

    /// The card fill: frosted glass for the glass styles, plain grouped cells for "system".
    var material: Material { self == .glassClear ? .ultraThinMaterial : .regularMaterial }
}

struct Glass: ViewModifier {
    @AppStorage("style") private var styleId = Style.glassRedWhite.rawValue
    var corner: CGFloat = 18

    func body(content: Content) -> some View {
        let style = Style(rawValue: styleId) ?? .glassRedWhite
        if style == .system {
            content.background(Color(uiColor: .secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: corner, style: .continuous))
        } else {
            content
                .background(style.material, in: RoundedRectangle(cornerRadius: corner, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: corner, style: .continuous).stroke(.white.opacity(0.35), lineWidth: 1))
                .shadow(color: .black.opacity(0.08), radius: 10, y: 4)
        }
    }
}

extension View {
    func glass(_ corner: CGFloat = 18) -> some View { modifier(Glass(corner: corner)) }
}
