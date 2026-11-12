import SwiftUI

@main
struct IBMCApp: App {
    @StateObject private var store = Store()
    @AppStorage("language") private var language = "system"

    var body: some Scene {
        WindowGroup {
            HomeView()
                .environmentObject(store)
                .environment(\.layoutDirection, L.isArabic ? .rightToLeft : .leftToRight)
                .id(language)
                .onOpenURL { url in store.receive([url]) }
        }
    }
}
