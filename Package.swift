// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "Adawat",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "Adawat",
            path: "Sources/Adawat",
            swiftSettings: [.unsafeFlags(["-parse-as-library"])]
        )
    ]
)
