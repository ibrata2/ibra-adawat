import SwiftUI

enum Theme {
    /// The single accent: the icon's violet, used for the highlighted wheel petal and active states.
    static let accent = Color(red: 91 / 255, green: 71 / 255, blue: 230 / 255)
    static let accentStroke = Color(red: 62 / 255, green: 46 / 255, blue: 170 / 255).opacity(0.44)
    static let accentDeep = Color(red: 0.25, green: 0.18, blue: 0.70)
    static let accentGradient = LinearGradient(colors: [Color(red: 0.50, green: 0.44, blue: 1), accent],
                                               startPoint: .top, endPoint: .bottom)

    /// Near-black text used on light glass.
    static let ink = Color(red: 40 / 255, green: 43 / 255, blue: 41 / 255)
    static let muted = Color(red: 117 / 255, green: 117 / 255, blue: 117 / 255)

    /// Light, slightly cool glass fill for wheel petals.
    static let petalFill = RadialGradient(
        stops: [
            .init(color: .white.opacity(0.92), location: 0),
            .init(color: Color(red: 0.953, green: 0.965, blue: 0.969).opacity(0.86), location: 0.68),
            .init(color: Color(red: 0.957, green: 0.965, blue: 0.965).opacity(0.92), location: 1),
        ],
        center: .center, startRadius: 0, endRadius: 140)
    static let petalStroke = Color(red: 93 / 255, green: 102 / 255, blue: 102 / 255).opacity(0.22)
    static let discTint = Color(red: 192 / 255, green: 201 / 255, blue: 205 / 255).opacity(0.25)
    static let shadow = Color(red: 38 / 255, green: 52 / 255, blue: 56 / 255).opacity(0.18)

    static func rounded(_ size: CGFloat, _ weight: Font.Weight) -> Font {
        .system(size: size, weight: weight, design: .rounded)
    }
}
