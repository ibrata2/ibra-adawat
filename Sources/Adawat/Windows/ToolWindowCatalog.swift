import AppKit

/// Every tool window by name, for the command-line snapshot mode.
@MainActor
enum ToolWindowCatalog {
    static let names = ["compress", "crop", "trim", "merge", "join", "edit", "annotate", "frame", "redact", "collage", "metadata", "speed", "crop-video", "split", "frames", "redact-video", "normalize", "visualizer", "channels", "bleep", "organize", "qr"]

    static func make(_ name: String, files: [URL], show: Bool) -> NSWindow? {
        if name.hasPrefix("welcome") {
            return WelcomeController.makeWindow(page: Int(name.dropFirst(7)) ?? 0) {}
        }
        switch name {
        case "compress": return CompressWindow.open(files, show: show)
        case "crop": return CropWindow.open(files, show: show)
        case "redact": return RedactWindow.open(files, show: show, sample: true)
        case "organize": return OrganizePDFWindow.open(files, show: show)
        case "qr": return QRWindow.open(files, show: show)
        case "crop-video":
            CropVideoWindow.open(files, show: show)
            RunLoop.main.run(until: Date().addingTimeInterval(1.5))
            return CropVideoWindow.lastWindow
        case "speed", "split", "frames", "redact-video", "normalize", "visualizer", "channels", "bleep":
            let open: [String: ([URL], Bool) -> Void] = [
                "speed": SpeedWindow.open, "split": SplitWindow.open, "frames": FramesWindow.open,
                "redact-video": RedactVideoWindow.open, "normalize": NormalizeWindow.open,
                "visualizer": VisualizerWindow.open, "channels": ChannelsWindow.open, "bleep": BleepWindow.open,
            ]
            open[name]?(files, show)
            RunLoop.main.run(until: Date().addingTimeInterval(1.5))
            return MediaWindows.lastWindow
        case "metadata": return MetadataWindow.open(files, show: show)
        case "collage": return CollageWindow.open(files, show: show)
        case "frame": return FrameWindow.open(files, show: show)
        case "edit": return EditPhotoWindow.open(files, show: show)
        case "annotate": return AnnotateWindow.open(files, show: show, sample: true)
        case "merge": return OrderWindow.mergePDFs(files, show: show)
        case "join": return OrderWindow.joinVideos(files, show: show)
        case "trim":
            TrimWindow.open(files, show: show)
            RunLoop.main.run(until: Date().addingTimeInterval(1))
            return TrimWindow.lastWindow
        default: return nil
        }
    }
}
