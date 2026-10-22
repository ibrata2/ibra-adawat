using Adawat.Core.Images;
using Adawat.Core.Media;
using Adawat.Core.Pdf;
using Adawat.Core.Documents;
using Adawat.Core.Ibra;

namespace Adawat.Core;

/// <summary>Decides which wheel choices fit the dragged files. Mirrors the Mac app's catalog, choice for choice.</summary>
public static class Catalog
{
    // Segoe Fluent Icons glyphs, drawn in tool and action petals.
    public static class Icons
    {
        public const string Photo = "", Document = "", Film = "", Audio = "", Archive = "",
            Compress = "", Metadata = "", Edit = "", Annotate = "", Background = "",
            Crop = "", Redact = "", Rotate = "", Text = "", Qr = "", Pdf = "",
            Collage = "", Merge = "", Organize = "", Split = "", Trim = "", Speed = "",
            Camera = "", Join = "", Mute = "", Volume = "", Waveform = "", Channels = "",
            Bleep = "", Silence = "", Share = "", Float = "", Copy = "", Move = "",
            Path = "", Terminal = "", Extract = "", Folder = "",
            Speech = "\uE720", Stamp = "\uE8EC", Resize = "\uE740", Compare = "\uE8A9", Cutout = "\uE7C5",
            Sticker = "\uE76E", Upscale = "\uE8A3", Slides = "\uE8B2", Chat = "\uE8BD", Subtitle = "\uE7F0", Story = "\uE8C6";
    }

    public static IReadOnlyList<AdawatOperation> For(WheelMode mode, IReadOnlyList<string> files)
    {
        if (files.Count == 0) return [];
        return mode switch
        {
            WheelMode.Convert => Conversions(files),
            WheelMode.Tools => Tools(files),
            _ => Actions(),
        };
    }

    public static AdawatOperation? Find(string id, IReadOnlyList<string> files) =>
        Enum.GetValues<WheelMode>().SelectMany(mode => For(mode, files)).FirstOrDefault(op => op.Id == id);

    // MARK: Convert

    private static List<AdawatOperation> Conversions(IReadOnlyList<string> files)
    {
        switch (files.CommonKind())
        {
            case FileKind.Image:
            {
                var sources = files.Select(ImageFormat.Of).ToHashSet();
                var ops = ImageFormat.All
                    .Where(format => !(sources.Count == 1 && sources.Contains(format)) && format.CanWrite)
                    .Select(format => new AdawatOperation($"image.{format.Ext}", $"To {format.Name}", Icons.Photo,
                                                          (f, c) => ImageOps.Convert(f, format, c)) { Badge = format.Name })
                    .ToList();
                ops.Add(new("image.pdf", "To PDF", Icons.Pdf, ImageOps.MakePdf) { Badge = "PDF" });
                return ops;
            }
            case FileKind.Pdf:
                return
                [
                    new("pdf.docx", "To Word", Icons.Document, (f, c) => PdfOps.ToDocument(f, DocumentTarget.Docx, c)) { Badge = "DOCX" },
                    new("pdf.pptx", "To PowerPoint", Icons.Document, SlideOps.PdfToPptx) { Badge = "PPTX" },
                    new("pdf.epub", "To EPUB", Icons.Document, (f, c) => PdfOps.ToDocument(f, DocumentTarget.Epub, c)) { Badge = "EPUB" },
                    new("pdf.md", "To Markdown", Icons.Document, (f, c) => PdfOps.ToDocument(f, DocumentTarget.Md, c)) { Badge = "MD" },
                    new("pdf.png", "Pages to PNG", Icons.Photo, (f, c) => PdfOps.RenderPages(f, ImageFormat.Png, c)) { Badge = "PNG" },
                    new("pdf.jpg", "Pages to JPEG", Icons.Photo, (f, c) => PdfOps.RenderPages(f, ImageFormat.Jpeg, c)) { Badge = "JPG" },
                    new("pdf.txt", "To Text", Icons.Text, PdfOps.ToText) { Badge = "TXT" },
                    new("pdf.xlsx", "To Excel", Icons.Document, PdfOps.ToExcel) { Badge = "XLSX" },
                ];
            case FileKind.Video:
                return MediaFormat.Video.Where(format => format.Ext != FileKinds.Ext(files[0]) || files.Count > 1).Select(MediaConversion).ToList();
            case FileKind.Audio:
                return MediaFormat.Audio.Where(format => format.Ext != FileKinds.Ext(files[0]) || files.Count > 1).Select(MediaConversion).ToList();
            case FileKind.Document:
            {
                var sources = files.Select(f => FileKinds.Ext(f) is "markdown" ? "md" : FileKinds.Ext(f) is "htm" ? "html" : FileKinds.Ext(f)).ToHashSet();
                return DocumentTargets.All.Where(target => !(sources.Count == 1 && sources.Contains(target.Ext())))
                    .Select(target => new AdawatOperation($"doc.{target.Ext()}", $"To {target.Title()}", Icons.Document,
                                                          (f, c) => DocumentOps.Convert(f, target, c)) { Badge = target.Title() })
                    .ToList();
            }
            case FileKind.Spreadsheet:
            {
                var sources = files.Select(FileKinds.Ext).ToHashSet();
                return SheetOps.Targets.Where(target => !(sources.Count == 1 && sources.Contains(target)))
                    .Select(target => new AdawatOperation($"sheet.{target}", $"To {SheetOps.Title(target)}", Icons.Document,
                                                          (f, c) => SheetOps.Convert(f, target, c)) { Badge = SheetOps.Title(target) })
                    .ToList();
            }
            case FileKind.Presentation:
            {
                if (!Office.CanRenderSlides) return [];
                var sources = files.Select(FileKinds.Ext).ToHashSet();
                return SlideOps.Targets.Where(target => !(sources.Count == 1 && sources.Contains(target)))
                    .Select(target => new AdawatOperation($"slides.{target}", $"To {SlideOps.Title(target)}", Icons.Document,
                                                          (f, c) => SlideOps.Convert(f, target, c)) { Badge = SlideOps.Title(target) })
                    .ToList();
            }
            case FileKind.Subtitle:
            {
                var sources = files.Select(FileKinds.Ext).ToHashSet();
                return SubtitleOps.Targets.Where(target => !(sources.Count == 1 && sources.Contains(target)))
                    .Select(target => new AdawatOperation($"subtitle.{target}", $"To {SubtitleOps.Title(target)}", Icons.Text,
                                                          (f, c) => SubtitleOps.Convert(f, target, c)) { Badge = SubtitleOps.Title(target) })
                    .ToList();
            }
            case FileKind.Archive:
            {
                var current = files.Count == 1 ? ArchiveOps.FormatOf(files[0]) : null;
                return new[] { "zip", "tar", "tgz" }.Where(format => format != current)
                    .Select(format => new AdawatOperation($"archive.to-{format}", $"To {ArchiveOps.Title(format)}", Icons.Archive,
                                                          (f, c) => ArchiveOps.Convert(f, format, c)) { Badge = ArchiveOps.Title(format) })
                    .ToList();
            }
            default:
            {
                // Mixed selections and folders can always be packed.
                var formats = new List<string> { "zip", "tar", "tgz" };
                if (files.Count == 1 && files.CommonKind() != FileKind.Folder) formats.Add("gz");
                return formats.Select(format => new AdawatOperation($"archive.{format}", $"To {ArchiveOps.Title(format)}", Icons.Archive,
                                                                    (f, c) => ArchiveOps.Pack(f, format, c)) { Badge = ArchiveOps.Title(format) })
                    .ToList();
            }
        }
    }

    private static AdawatOperation MediaConversion(MediaFormat format) =>
        new($"media.{format.Ext}", $"To {format.Name}", format.AudioOnly ? Icons.Audio : Icons.Film,
            (f, c) => MediaOps.Convert(f, format, c)) { Badge = format.Name };

    // MARK: Tools

    private static AdawatOperation Tool(string id, string title, string? @short, string icon, OperationWork run, string? window = null,
                                        bool producesFiles = true) =>
        new(id, title, icon, run) { Short = @short, Window = window, ProducesFiles = producesFiles };

    private static Task<IReadOnlyList<string>> NeedsWindow(IReadOnlyList<string> files, OperationContext context) =>
        throw new AdawatException("This tool needs its window.");

    private static List<AdawatOperation> Tools(IReadOnlyList<string> files)
    {
        var kinds = files.Kinds();
        var single = files.Count == 1;
        var ops = new List<AdawatOperation>();

        if (kinds.SetEquals([FileKind.Image]))
        {
            ops.Add(Tool("image.compress", "Compress", null, Icons.Compress, (f, c) => Compression.Images(f, CompressSettings.Saved, c), "compress"));
            ops.Add(Tool("image.metadata", "Edit Metadata", "Metadata", Icons.Metadata, ImageOps.RemoveMetadata, single ? "metadata" : null));
            if (single)
            {
                ops.Add(Tool("image.edit", "Edit Photo", "Edit", Icons.Edit, NeedsWindow, "edit"));
                ops.Add(Tool("image.annotate", "Annotate", null, Icons.Annotate, NeedsWindow, "annotate"));
            }
            ops.Add(Tool("image.frame", "Add Background", "Add BG", Icons.Background, (f, c) => Frames.Apply(f, new FrameOptions(), c), "frame"));
            ops.Add(Tool("image.crop", "Crop", null, Icons.Crop, ImageOps.SquareCrop, "crop"));
            if (single) ops.Add(Tool("image.redact", "Redact", null, Icons.Redact, NeedsWindow, "redact"));
            ops.Add(Tool("image.rotate", "Rotate", null, Icons.Rotate, ImageOps.Rotate));
            ops.Add(Tool("image.ocr", "Copy Text", "Text", Icons.Text, ImageOps.ExtractText));
            ops.Add(Tool("image.qr", "Read QR Code", "QR Code", Icons.Qr, ImageOps.ReadQrCodes, producesFiles: false));
            ops.Add(Tool("ibra.watermark", "Watermark", null, Icons.Stamp, SocialOps.WatermarkQuick, "watermark"));
            ops.Add(Tool("ibra.social", "Social Size", "Size", Icons.Resize, SocialOps.ResizeQuick, "social"));
            ops.Add(Tool("ibra.nobg", "Remove Background", "No BG", Icons.Cutout, BackgroundRemoval.Remove));
            ops.Add(Tool("ibra.upscale", "Enlarge with AI", "AI ×2", Icons.Upscale, Upscale.Quick, "upscale"));
            ops.Add(Tool("ibra.sticker", "WhatsApp Sticker", "Sticker", Icons.Sticker, SocialOps.Sticker));
            if (files.Count == 2) ops.Add(Tool("ibra.beforeafter", "Before and After", "Before/After", Icons.Compare, SocialOps.BeforeAfterQuick, "beforeafter"));
            if (!single)
            {
                ops.Add(Tool("ibra.slideshow", "Slideshow Video", "Slideshow", Icons.Slides, SocialOps.SlideshowQuick, "slideshow"));
                ops.Add(Tool("image.topdf", "Create PDF", "PDF", Icons.Pdf, ImageOps.MakePdf));
                ops.Add(Tool("image.collage", "Create Collage", "Collage", Icons.Collage, (f, c) => Collage.Make(f, new CollageOptions(), c), "collage"));
            }
        }

        if (kinds.SetEquals([FileKind.Pdf]))
        {
            ops.Add(Tool("pdf.compress", "Compress", null, Icons.Compress, PdfOps.Compress));
            ops.Add(Tool("pdf.metadata", "Remove Metadata", "Metadata", Icons.Metadata, PdfOps.StripMetadata));
            ops.Add(single ? Tool("pdf.organize", "Organize Pages", "Organize", Icons.Organize, NeedsWindow, "organize")
                           : Tool("pdf.merge", "Merge PDFs", "Merge", Icons.Merge, PdfOps.Merge, "merge"));
            ops.Add(Tool("pdf.split", "Split Pages", "Split", Icons.Split, PdfOps.Split));
            ops.Add(Tool("pdf.ocr", "Copy Text", "Text", Icons.Text, ImageOps.ExtractText));
        }

        if (kinds.SetEquals([FileKind.Video]))
        {
            ops.Add(Tool("video.compress", "Compress", null, Icons.Compress, (f, c) => Compression.Videos(f, CompressSettings.Saved, c), "compress"));
            ops.Add(Tool("video.metadata", "Remove Metadata", "Metadata", Icons.Metadata, MediaOps.StripMetadata));
            if (single)
            {
                ops.Add(Tool("video.trim", "Trim", null, Icons.Trim, NeedsWindow, "trim"));
                ops.Add(Tool("video.crop", "Crop Video", "Crop", Icons.Crop, NeedsWindow, "crop-video"));
                ops.Add(Tool("video.speed", "Change Speed", "Speed", Icons.Speed, NeedsWindow, "speed"));
                ops.Add(Tool("video.frames", "Save Frames", "Frames", Icons.Camera, NeedsWindow, "frames"));
                ops.Add(Tool("video.split", "Split Video", "Split", Icons.Split, NeedsWindow, "split"));
                ops.Add(Tool("video.redact", "Redact Video", "Redact", Icons.Redact, NeedsWindow, "redact-video"));
            }
            else
            {
                ops.Add(Tool("video.join", "Join Videos", "Join", Icons.Join, MediaOps.Join, "join"));
            }
            ops.Add(Tool("video.rotate", "Rotate", null, Icons.Rotate, MediaOps.Rotate));
            ops.Add(Tool("video.mute", "Mute", null, Icons.Mute, MediaOps.Mute));
            ops.Add(Tool("video.normalize", "Normalize Volume", "Normalize", Icons.Volume, MediaOps.Normalize, single ? "normalize" : null));
            ops.Add(Tool("ibra.transcribe", "Speech to Text", "To Text", Icons.Speech, Transcription.Quick, "transcribe"));
            ops.Add(Tool("ibra.watermark", "Watermark", null, Icons.Stamp, SocialOps.WatermarkQuick, "watermark"));
            ops.Add(Tool("ibra.social", "Social Size", "Size", Icons.Resize, SocialOps.ResizeQuick, "social"));
            ops.Add(Tool("ibra.whatsapp", "WhatsApp Size", "WhatsApp", Icons.Chat, SocialOps.WhatsApp));
            ops.Add(Tool("ibra.stories", "Cut into Stories", "Stories", Icons.Story, SocialOps.StorySplitQuick, "stories"));
            ops.Add(Tool("ibra.sticker", "WhatsApp Sticker", "Sticker", Icons.Sticker, SocialOps.Sticker));
        }

        if (kinds.SetEquals([FileKind.Video, FileKind.Subtitle]) && files.Count == 2)
            ops.Add(Tool("ibra.burn", "Burn Subtitles", "Burn Subs", Icons.Subtitle, Subtitles.BurnPair));

        if (kinds.SetEquals([FileKind.Image, FileKind.Audio]))
            ops.Add(Tool("ibra.slideshow", "Slideshow Video", "Slideshow", Icons.Slides, SocialOps.SlideshowQuick, "slideshow"));

        if (kinds.SetEquals([FileKind.Audio]))
        {
            ops.Add(Tool("audio.compress", "Compress", null, Icons.Compress, (f, c) => Compression.Audio(f, CompressSettings.Saved, c), "compress"));
            ops.Add(Tool("audio.metadata", "Remove Metadata", "Metadata", Icons.Metadata, MediaOps.StripMetadata));
            ops.Add(Tool("audio.normalize", "Normalize Volume", "Normalize", Icons.Volume, MediaOps.Normalize, single ? "normalize" : null));
            if (single)
            {
                ops.Add(Tool("audio.visualizer", "Audio Visualizer", "Visualizer", Icons.Waveform, NeedsWindow, "visualizer"));
                ops.Add(Tool("audio.trim", "Trim", null, Icons.Trim, NeedsWindow, "trim"));
                ops.Add(Tool("audio.channels", "Convert Channels", "Channels", Icons.Channels, MediaOps.ToMono, "channels"));
                ops.Add(Tool("audio.bleep", "Bleep Audio", "Bleep", Icons.Bleep, NeedsWindow, "bleep"));
            }
            ops.Add(Tool("ibra.transcribe", "Speech to Text", "To Text", Icons.Speech, Transcription.Quick, "transcribe"));
            else
            {
                ops.Add(Tool("audio.silence", "Trim Silence", "Silence", Icons.Silence, MediaOps.TrimSilence));
                ops.Add(Tool("audio.mono", "To Mono", "Mono", Icons.Channels, MediaOps.ToMono));
            }
        }

        if (QrContent.CanEncode(files)) ops.Add(Tool("text.qr", "Create QR Code", "Make QR", Icons.Qr, QrContent.Quick, "qr"));

        if (kinds.SetEquals([FileKind.Archive])) ops.Add(Tool("archive.extract", "Extract", null, Icons.Extract, ArchiveOps.Extract));

        return ops;
    }

    // MARK: Actions

    /// <summary>The same six actions for any file or folder, clockwise from the top. The app performs them.</summary>
    private static List<AdawatOperation> Actions() =>
    [
        Tool("action.share", "Share", null, Icons.Share, FileActions.Share, producesFiles: false),
        Tool("action.float", "Float", null, Icons.Float, FileActions.NewFloat, producesFiles: false),
        Tool("action.copy", "Copy", null, Icons.Copy, FileActions.CopyFiles, producesFiles: false),
        Tool("action.move", "Move", null, Icons.Move, FileActions.Move, producesFiles: false),
        Tool("action.paths", "Copy Path", null, Icons.Path, FileActions.CopyPaths, producesFiles: false),
        Tool("action.terminal", "Terminal", null, Icons.Terminal, FileActions.OpenTerminal, producesFiles: false),
    ];
}
