import Foundation

/// Decides which wheel choices fit the dragged files.
@MainActor
enum OperationCatalog {
    static func operations(for mode: WheelMode, files: [URL]) -> [AdawatOperation] {
        guard !files.isEmpty else { return [] }
        switch mode {
        case .convert: return conversions(for: files)
        case .tools: return tools(for: files)
        case .actions: return actions(for: files)
        }
    }

    // MARK: Convert

    private static func conversions(for files: [URL]) -> [AdawatOperation] {
        switch files.commonKind {
        case .image:
            let sources = Set(files.compactMap(ImageFormat.of))
            var ops = ImageFormat.all
                .filter { !(sources.count == 1 && sources.contains($0)) && ImageEngine.canWrite($0) }
                .map { format in
                    AdawatOperation(id: "image.\(format.ext)", title: L10n.t("To \(format.name)"),
                                  systemImage: "photo", badge: format.name) { files, ctx in
                        try await ImageOps.convert(files, to: format, context: ctx)
                    }
                }
            ops.append(AdawatOperation(id: "image.pdf", title: L10n.t("To PDF"), systemImage: "doc.richtext", badge: "PDF") {
                try await ImageOps.makePDF($0, context: $1)
            })
            return ops
        case .pdf:
            var ops: [AdawatOperation] = [
                AdawatOperation(id: "pdf.docx", title: L10n.t("To Word"), systemImage: "doc.text", badge: "DOCX") {
                    try await PDFOps.toDocument($0, target: .docx, context: $1)
                },
                AdawatOperation(id: "pdf.pptx", title: L10n.t("To PowerPoint"), systemImage: "rectangle.on.rectangle", badge: "PPTX") {
                    try await SlideOps.pdfToPPTX($0, context: $1)
                },
                AdawatOperation(id: "pdf.epub", title: L10n.t("To EPUB"), systemImage: "book", badge: "EPUB") {
                    try await PDFOps.toDocument($0, target: .epub, context: $1)
                },
                AdawatOperation(id: "pdf.md", title: L10n.t("To Markdown"), systemImage: "text.alignleft", badge: "MD") {
                    try await PDFOps.toDocument($0, target: .md, context: $1)
                },
            ]
            ops += [ImageFormat.png, .jpeg].map { format in
                AdawatOperation(id: "pdf.\(format.ext)", title: L10n.t("Pages to \(format.name)"),
                              systemImage: "photo.on.rectangle", badge: format == .jpeg ? "JPG" : format.name) { files, ctx in
                    try await PDFOps.renderPages(files, format: format, context: ctx)
                }
            }
            ops.append(AdawatOperation(id: "pdf.txt", title: L10n.t("To Text"), systemImage: "text.alignleft", badge: "TXT") {
                try await PDFOps.toText($0, context: $1)
            })
            if IWorkBridge.isInstalled(.pages) {
                ops.append(AdawatOperation(id: "pdf.pages", title: L10n.t("To Pages"), systemImage: "doc.text", badge: "PAGES") {
                    try await PDFOps.toDocument($0, target: .pages, context: $1)
                })
            }
            return ops
        case .video:
            return MediaFormat.video
                .filter { $0.ext != files[0].pathExtension.lowercased() || files.count > 1 }
                .map(mediaConversion)
        case .audio:
            return MediaFormat.audio
                .filter { $0.ext != files[0].pathExtension.lowercased() || files.count > 1 }
                .map(mediaConversion)
        case .document:
            let sources = Set(files.map { $0.pathExtension.lowercased() == "markdown" ? "md" : $0.pathExtension.lowercased() })
            var targets: [DocumentOps.Target] = [.pdf, .docx, .pages, .epub, .md, .txt, .rtf, .html, .odt]
            if !IWorkBridge.isInstalled(.pages) { targets.removeAll { $0 == .pages } }
            return targets.filter { !(sources.count == 1 && sources.contains($0.rawValue)) }.map { target in
                AdawatOperation(id: "doc.\(target.rawValue)", title: L10n.t("To \(target.title)"),
                              systemImage: "doc.text", badge: target.title) { files, ctx in
                    try await DocumentOps.convert(files, to: target, context: ctx)
                }
            }
        case .spreadsheet:
            let sources = Set(files.map { $0.pathExtension.lowercased() })
            return SheetOps.Target.allCases.filter { !(sources.count == 1 && sources.contains($0.rawValue)) }.map { target in
                AdawatOperation(id: "sheet.\(target.rawValue)", title: L10n.t("To \(target.title)"),
                              systemImage: "tablecells", badge: target.title) { files, ctx in
                    try await SheetOps.convert(files, to: target, context: ctx)
                }
            }
        case .presentation:
            guard IWorkBridge.isInstalled(.keynote) else { return [] }
            let sources = Set(files.map { $0.pathExtension.lowercased() })
            let targets: [SlideOps.Target] = [.pdf, .pptx, .key, .png, .jpg]
            return targets.filter { !(sources.count == 1 && sources.contains($0.rawValue)) }.map { target in
                AdawatOperation(id: "slides.\(target.rawValue)", title: L10n.t("To \(target.title)"),
                              systemImage: "rectangle.on.rectangle", badge: target.title) { files, ctx in
                    try await SlideOps.convert(files, to: target, context: ctx)
                }
            }
        case .subtitle:
            let sources = Set(files.map { $0.pathExtension.lowercased() })
            return SubtitleOps.Target.allCases.filter { !(sources.count == 1 && sources.contains($0.rawValue)) }.map { target in
                AdawatOperation(id: "subtitle.\(target.rawValue)", title: L10n.t("To \(target.title)"),
                              systemImage: "captions.bubble", badge: target.title) { files, ctx in
                    try await SubtitleOps.convert(files, to: target, context: ctx)
                }
            }
        case .archive:
            let current = files.count == 1 ? ArchiveOps.format(of: files[0]) : nil
            return [ArchiveOps.Format.zip, .tar, .tgz].filter { $0 != current }.map { format in
                AdawatOperation(id: "archive.to-\(format.rawValue)", title: L10n.t("To \(format.title)"),
                              systemImage: "archivebox", badge: format.title) { files, ctx in
                    try await ArchiveOps.convert(files, to: format, context: ctx)
                }
            }
        case .folder, .other, nil:
            // Mixed selections and folders can always be packed.
            var formats: [ArchiveOps.Format] = [.zip, .tar, .tgz]
            if files.count == 1, files.commonKind != .folder { formats.append(.gz) }
            return formats.map { format in
                AdawatOperation(id: "archive.\(format.rawValue)", title: L10n.t("To \(format.title)"), systemImage: "doc.zipper",
                              badge: format.title) { files, ctx in
                    try await ArchiveOps.pack(files, as: format, context: ctx)
                }
            }
        }
    }

    private static func mediaConversion(_ format: MediaFormat) -> AdawatOperation {
        AdawatOperation(id: "media.\(format.ext)", title: L10n.t("To \(format.name)"),
                      systemImage: format.audioOnly ? "waveform" : "film", badge: format.name) { files, ctx in
            try await MediaOps.convert(files, to: format, context: ctx)
        }
    }

    // MARK: Tools

    private static func tool(_ id: String, _ title: String, _ short: String? = nil, _ icon: String,
                             producesFiles: Bool = true,
                             _ run: @escaping @MainActor ([URL], OperationContext) async throws -> [URL]) -> AdawatOperation {
        AdawatOperation(id: id, title: title, systemImage: icon, short: short, producesFiles: producesFiles, run: run)
    }

    private static func tools(for files: [URL]) -> [AdawatOperation] {
        let kinds = files.kinds
        var ops: [AdawatOperation] = []

        if kinds == [.image] {
            let single = files.count == 1
            let needsWindow = "This tool needs its window."
            ops.append(tool("image.compress", "Compress", nil, "arrow.down.right.and.arrow.up.left") {
                try await Compression.images($0, .saved, context: $1)
            }.opening(CompressWindow.open))
            // Several photos get a one-step clean; one photo opens the field editor.
            let metadata = tool("image.metadata", "Edit Metadata", "Metadata", "mappin.slash") {
                try await ImageOps.removeMetadata($0, context: $1)
            }
            ops.append(single ? metadata.opening(MetadataWindow.open) : metadata)
            if single {
                ops += [
                    tool("image.edit", "Edit Photo", "Edit", "slider.horizontal.3") { _, _ in
                        throw AdawatError.message(needsWindow)
                    }.opening(EditPhotoWindow.open),
                    tool("image.annotate", "Annotate", nil, "pencil.tip.crop.circle") { _, _ in
                        throw AdawatError.message(needsWindow)
                    }.opening(AnnotateWindow.open),
                ]
            }
            ops.append(tool("image.frame", "Add Background", "Add BG", "photo.artframe") { files, context in
                try await ImageOps.frame(files, options: FrameOptions(), context: context)
            }.opening(FrameWindow.open))
            ops.append(tool("image.crop", "Crop", nil, "crop") {
                try await ImageOps.squareCrop($0, context: $1)
            }.opening(CropWindow.open))
            if single {
                ops.append(tool("image.redact", "Redact", nil, "eye.slash") { _, _ in
                    throw AdawatError.message(needsWindow)
                }.opening(RedactWindow.open))
            }
            ops += [
                tool("image.rotate", "Rotate", nil, "rotate.right") {
                    try await ImageOps.rotate($0, context: $1)
                },
                tool("image.ocr", "Copy Text", "Text", "text.viewfinder") {
                    try await ImageOps.extractText($0, context: $1)
                },
                tool("image.qr", "Read QR Code", "QR Code", "qrcode", producesFiles: false) {
                    try await ImageOps.readQRCodes($0, context: $1)
                },
            ]
            if !single {
                ops += [
                    tool("image.topdf", "Create PDF", "PDF", "doc.richtext") {
                        try await ImageOps.makePDF($0, context: $1)
                    },
                    tool("image.collage", "Create Collage", "Collage", "square.grid.2x2") { files, context in
                        try await ImageOps.collage(files, options: CollageOptions(), context: context)
                    }.opening(CollageWindow.open),
                ]
            }
        }

        if kinds == [.pdf] {
            ops += [
                tool("pdf.compress", "Compress", nil, "arrow.down.right.and.arrow.up.left") {
                    try await PDFOps.compress($0, context: $1)
                },
                tool("pdf.metadata", "Remove Metadata", "Metadata", "mappin.slash") {
                    try await PDFOps.stripMetadata($0, context: $1)
                },
            ]
            if files.count > 1 {
                ops.append(tool("pdf.merge", "Merge PDFs", "Merge", "rectangle.stack") {
                    try await PDFOps.merge($0, context: $1)
                }.opening(OrderWindow.mergePDFs))
            } else {
                ops.append(tool("pdf.organize", "Organize Pages", "Organize", "square.grid.3x3") { _, _ in
                    throw AdawatError.message("Organize needs its window.")
                }.opening(OrganizePDFWindow.open))
            }
            ops += [
                tool("pdf.split", "Split Pages", "Split", "scissors") {
                    try await PDFOps.split($0, context: $1)
                },
                tool("pdf.ocr", "Copy Text", "Text", "text.viewfinder") {
                    try await ImageOps.extractText($0, context: $1)
                },
            ]
        }

        if kinds == [.video] {
            let single = files.count == 1
            let needsWindow = "This tool needs its window."
            func windowTool(_ id: String, _ title: String, _ short: String?, _ icon: String,
                            _ open: @escaping @MainActor ([URL]) -> Void) -> AdawatOperation {
                tool(id, title, short, icon) { _, _ in throw AdawatError.message(needsWindow) }.opening(open)
            }
            ops.append(tool("video.compress", "Compress", nil, "arrow.down.right.and.arrow.up.left") {
                try await Compression.videos($0, .saved, context: $1)
            }.opening(CompressWindow.open))
            ops.append(tool("video.metadata", "Remove Metadata", "Metadata", "mappin.slash") {
                try await MediaOps.stripMetadata($0, context: $1)
            })
            if single {
                ops += [
                    windowTool("video.trim", "Trim", nil, "timeline.selection", TrimWindow.open),
                    windowTool("video.crop", "Crop Video", "Crop", "crop", CropVideoWindow.open),
                    windowTool("video.speed", "Change Speed", "Speed", "gauge.with.dots.needle.67percent", SpeedWindow.open),
                    windowTool("video.frames", "Save Frames", "Frames", "camera", FramesWindow.open),
                    windowTool("video.split", "Split Video", "Split", "scissors", SplitWindow.open),
                    windowTool("video.redact", "Redact Video", "Redact", "eye.slash", RedactVideoWindow.open),
                ]
            } else {
                ops.append(tool("video.join", "Join Videos", "Join", "film.stack") {
                    try await MediaOps.join($0, context: $1)
                }.opening(OrderWindow.joinVideos))
            }
            ops += [
                tool("video.rotate", "Rotate", nil, "rotate.right") {
                    try await MediaOps.rotate($0, context: $1)
                },
                tool("video.mute", "Mute", nil, "speaker.slash") {
                    try await MediaOps.mute($0, context: $1)
                },
            ]
            let normalize = tool("video.normalize", "Normalize Volume", "Normalize", "speaker.wave.2") {
                try await MediaOps.normalize($0, context: $1)
            }
            ops.append(single ? normalize.opening(NormalizeWindow.open) : normalize)
        }

        if kinds == [.audio] {
            let single = files.count == 1
            ops.append(tool("audio.compress", "Compress", nil, "arrow.down.right.and.arrow.up.left") {
                try await Compression.audio($0, .saved, context: $1)
            }.opening(CompressWindow.open))
            ops.append(tool("audio.metadata", "Remove Metadata", "Metadata", "mappin.slash") {
                try await MediaOps.stripMetadata($0, context: $1)
            })
            let normalize = tool("audio.normalize", "Normalize Volume", "Normalize", "speaker.wave.2") {
                try await MediaOps.normalize($0, context: $1)
            }
            ops.append(single ? normalize.opening(NormalizeWindow.open) : normalize)
            if single {
                let needsWindow = "This tool needs its window."
                ops += [
                    tool("audio.visualizer", "Audio Visualizer", "Visualizer", "waveform.and.magnifyingglass") { _, _ in
                        throw AdawatError.message(needsWindow)
                    }.opening(VisualizerWindow.open),
                    tool("audio.trim", "Trim", nil, "timeline.selection") { _, _ in
                        throw AdawatError.message(needsWindow)
                    }.opening(TrimWindow.open),
                    tool("audio.channels", "Convert Channels", "Channels", "circle.lefthalf.filled") {
                        try await MediaOps.toMono($0, context: $1)
                    }.opening(ChannelsWindow.open),
                    tool("audio.bleep", "Bleep Audio", "Bleep", "waveform.badge.exclamationmark") { _, _ in
                        throw AdawatError.message(needsWindow)
                    }.opening(BleepWindow.open),
                ]
            } else {
                ops += [
                    tool("audio.silence", "Trim Silence", "Silence", "waveform.badge.minus") {
                        try await MediaOps.trimSilence($0, context: $1)
                    },
                    tool("audio.mono", "To Mono", "Mono", "circle.lefthalf.filled") {
                        try await MediaOps.toMono($0, context: $1)
                    },
                ]
            }
        }

        if QRContent.canEncode(files) {
            ops.append(tool("text.qr", "Create QR Code", "Make QR", "qrcode") {
                try await QRWindow.quick($0, context: $1)
            }.opening(QRWindow.open))
        }

        if kinds == [.archive] {
            ops.append(tool("archive.extract", "Extract", nil, "archivebox") {
                try await FileActions.extract($0, context: $1)
            })
        }

        return ops
    }

    // MARK: Actions

    /// The same six actions for any file or folder, clockwise from the top.
    private static func actions(for files: [URL]) -> [AdawatOperation] {
        [
            tool("action.airdrop", "AirDrop", nil, "dot.radiowaves.left.and.right", producesFiles: false) {
                try await FileActions.airDrop($0, context: $1)
            },
            tool("action.float", "Float", nil, "pin", producesFiles: false) {
                try await FileActions.newFloat($0, context: $1)
            },
            tool("action.copy", "Copy", nil, "doc.on.doc", producesFiles: false) {
                try await FileActions.copyFiles($0, context: $1)
            },
            tool("action.move", "Move", nil, "rectangle.portrait.and.arrow.right", producesFiles: false) {
                try await FileActions.move($0, context: $1)
            },
            tool("action.paths", "Copy Path", nil, "doc.text", producesFiles: false) {
                try await FileActions.copyPaths($0, context: $1)
            },
            tool("action.terminal", "Terminal", nil, "terminal", producesFiles: false) {
                try await FileActions.openTerminal($0, context: $1)
            },
        ]
    }
}
