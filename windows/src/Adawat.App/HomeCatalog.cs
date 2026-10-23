using System.Windows.Media;
using Adawat.Core;

namespace Adawat.App;

/// <summary>One tile in a section of the main window: an operation of the wheel catalog, found by id for the chosen files.</summary>
public sealed record HomeTile(string Id, string English, string Arabic, string Glyph, string? Badge = null)
{
    /// <summary>All files go into one task (merge, join, collage…); otherwise each file becomes its own task.</summary>
    public bool Together { get; init; }
    /// <summary>Opens without files (Create QR, Download from link).</summary>
    public Action? Direct { get; init; }
    /// <summary>File kinds this tile accepts; the section's kinds when null.</summary>
    public FileKind[]? Kinds { get; init; }
    public int MinFiles { get; init; } = 1;

    public string Title => L10n.IsArabic ? Arabic : English;
}

public sealed record HomeSection(string English, string Arabic, string Glyph, Color Color, FileKind[] Kinds, string DialogFilter, IReadOnlyList<HomeTile> Tiles)
{
    public string Title => L10n.IsArabic ? Arabic : English;
}

public static class HomeCatalog
{
    private const string VideoExt = "*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.flv;*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.3gp;*.ogv";
    private const string AudioExt = "*.mp3;*.m4a;*.aac;*.wav;*.flac;*.aif;*.aiff;*.ogg;*.oga;*.opus;*.wma;*.alac;*.caf;*.amr";
    private const string ImageExt = "*.jpg;*.jpeg;*.jfif;*.png;*.heic;*.heif;*.webp;*.avif;*.tif;*.tiff;*.gif;*.bmp;*.ico;*.svg;*.dng;*.cr2;*.cr3;*.nef;*.arw;*.psd;*.jxl";
    private const string DocExt = "*.pdf;*.docx;*.doc;*.odt;*.rtf;*.txt;*.md;*.html;*.htm;*.epub;*.xlsx;*.xls;*.csv;*.ods;*.pptx;*.ppt;*.odp";

    private static HomeTile To(string prefix, string ext, string name) => new($"{prefix}.{ext}", $"To {name}", $"إلى {name}", "", name);

    public static readonly IReadOnlyList<HomeSection> Sections =
    [
        new("Video", "الفيديو", "", Color.FromRgb(0xE3, 0x19, 0x2B), [FileKind.Video], $"Video|{VideoExt}",
        [
            To("media", "mp4", "MP4"), To("media", "mkv", "MKV"), To("media", "webm", "WebM"), To("media", "mov", "MOV"),
            To("media", "avi", "AVI"), To("media", "gif", "GIF"),
            new("video.compress", "Compress", "ضغط الفيديو", ""),
            new("ibra.whatsapp", "WhatsApp Size", "حجم واتساب", ""),
            new("video.trim", "Trim", "قص المدة", ""),
            new("video.crop", "Crop", "قص الصورة", ""),
            new("video.join", "Join", "دمج فيديوهات", "") { Together = true, MinFiles = 2 },
            new("video.split", "Split", "تقسيم", ""),
            new("ibra.stories", "Cut into Stories", "تقطيع للستوري", ""),
            new("video.speed", "Speed", "تغيير السرعة", ""),
            new("video.rotate", "Rotate", "تدوير", ""),
            new("video.frames", "Save Frames", "حفظ لقطات", ""),
            new("video.mute", "Mute", "كتم الصوت", ""),
            new("video.normalize", "Normalize Volume", "توحيد الصوت", ""),
            new("ibra.transcribe", "Speech to Text", "الكلام إلى نص", ""),
            new("ibra.burn", "Burn Subtitles", "طباعة الترجمة", "") { Together = true, MinFiles = 2, Kinds = [FileKind.Video, FileKind.Subtitle] },
            new("ibra.watermark", "Watermark", "علامة مائية", ""),
            new("ibra.social", "Social Size", "مقاس السوشيال", ""),
            new("ibra.sticker", "WhatsApp Sticker", "ستيكر واتساب", ""),
            new("video.redact", "Redact", "إخفاء أجزاء", ""),
            new("video.metadata", "Remove Metadata", "حذف البيانات", ""),
        ]),
        new("Audio", "الصوت", "", Color.FromRgb(0x8E, 0x44, 0xFF), [FileKind.Audio, FileKind.Video], $"Audio and video|{AudioExt};{VideoExt}",
        [
            To("media", "mp3", "MP3"), To("media", "m4a", "M4A"), To("media", "aac", "AAC"), To("media", "wav", "WAV"),
            To("media", "flac", "FLAC"), To("media", "ogg", "OGG"), To("media", "opus", "Opus"), To("media", "wma", "WMA"),
            To("media", "aiff", "AIFF") with { Kinds = [FileKind.Audio] },
            new("audio.compress", "Compress", "ضغط الصوت", "") { Kinds = [FileKind.Audio] },
            new("audio.trim", "Trim", "قص", "") { Kinds = [FileKind.Audio] },
            new("audio.normalize", "Normalize Volume", "توحيد الصوت", "") { Kinds = [FileKind.Audio] },
            new("audio.channels", "Channels", "ستيريو / مونو", "") { Kinds = [FileKind.Audio] },
            new("audio.bleep", "Bleep", "تصفير أجزاء", "") { Kinds = [FileKind.Audio] },
            new("audio.visualizer", "Audio Visualizer", "فيديو بموجة الصوت", "") { Kinds = [FileKind.Audio] },
            new("ibra.transcribe", "Speech to Text", "الكلام إلى نص", ""),
            new("audio.metadata", "Remove Metadata", "حذف البيانات", "") { Kinds = [FileKind.Audio] },
        ]),
        new("Image", "الصورة", "", Color.FromRgb(0x1F, 0xA8, 0x5C), [FileKind.Image], $"Images|{ImageExt}",
        [
            To("image", "jpg", "JPG"), To("image", "png", "PNG"), To("image", "webp", "WebP"), To("image", "avif", "AVIF"),
            To("image", "heic", "HEIC"), To("image", "tiff", "TIFF"), To("image", "bmp", "BMP"), To("image", "ico", "ICO"),
            new("image.pdf", "To PDF", "إلى PDF", "", "PDF") { Together = true },
            new("image.compress", "Compress", "ضغط الصور", ""),
            new("ibra.nobg", "Remove Background", "إزالة الخلفية", ""),
            new("ibra.upscale", "Enlarge with AI", "تكبير بالذكاء الاصطناعي", ""),
            new("image.edit", "Edit Photo", "تعديل الصورة", ""),
            new("image.annotate", "Annotate", "كتابة وأسهم", ""),
            new("image.crop", "Crop", "قص", ""),
            new("image.rotate", "Rotate", "تدوير", ""),
            new("ibra.watermark", "Watermark", "علامة مائية", ""),
            new("ibra.social", "Social Size", "مقاس السوشيال", ""),
            new("ibra.beforeafter", "Before and After", "قبل وبعد", "") { Together = true, MinFiles = 2 },
            new("image.collage", "Collage", "صورة مجمّعة", "") { Together = true, MinFiles = 2 },
            new("ibra.slideshow", "Slideshow Video", "فيديو عرض صور", "") { Together = true, MinFiles = 2, Kinds = [FileKind.Image, FileKind.Audio] },
            new("ibra.sticker", "WhatsApp Sticker", "ستيكر واتساب", ""),
            new("image.frame", "Add Background", "إضافة خلفية", ""),
            new("image.redact", "Redact", "إخفاء أجزاء", ""),
            new("image.ocr", "Copy Text", "نسخ النص", ""),
            new("image.qr", "Read QR Code", "قراءة QR", ""),
            new("image.metadata", "Edit Metadata", "البيانات", ""),
        ]),
        new("PDF and documents", "PDF والمستندات", "", Color.FromRgb(0xF0, 0x6A, 0x0F),
            [FileKind.Pdf, FileKind.Document, FileKind.Spreadsheet, FileKind.Presentation], $"Documents|{DocExt}",
        [
            To("pdf", "docx", "Word") with { Kinds = [FileKind.Pdf] }, To("pdf", "xlsx", "Excel") with { Kinds = [FileKind.Pdf] },
            To("pdf", "pptx", "PowerPoint") with { Kinds = [FileKind.Pdf] }, To("pdf", "jpg", "JPG") with { Kinds = [FileKind.Pdf] },
            To("pdf", "png", "PNG") with { Kinds = [FileKind.Pdf] }, To("pdf", "txt", "TXT") with { Kinds = [FileKind.Pdf] },
            To("pdf", "epub", "EPUB") with { Kinds = [FileKind.Pdf] },
            To("doc", "pdf", "PDF") with { Kinds = [FileKind.Document] }, To("doc", "docx", "Word") with { Kinds = [FileKind.Document] },
            To("sheet", "xlsx", "Excel") with { Kinds = [FileKind.Spreadsheet] }, To("sheet", "pdf", "PDF") with { Id = "sheet.pdf", Kinds = [FileKind.Spreadsheet] },
            new("pdf.compress", "Compress", "ضغط PDF", "") { Kinds = [FileKind.Pdf] },
            new("pdf.merge", "Merge PDFs", "دمج PDF", "") { Together = true, MinFiles = 2, Kinds = [FileKind.Pdf] },
            new("pdf.organize", "Organize Pages", "ترتيب الصفحات", "") { Kinds = [FileKind.Pdf] },
            new("pdf.split", "Split Pages", "تقسيم الصفحات", "") { Kinds = [FileKind.Pdf] },
            new("pdf.ocr", "Copy Text", "نسخ النص", "") { Kinds = [FileKind.Pdf] },
            new("pdf.metadata", "Remove Metadata", "حذف البيانات", "") { Kinds = [FileKind.Pdf] },
        ]),
        new("Tools", "أدوات مساعدة", "", Color.FromRgb(0x1E, 0x7B, 0xFF), [], "All files|*.*",
        [
            new("tool.download", "Download from Link", "تحميل من رابط", "") { Direct = Windows.IbraWindows.DownloadLink },
            new("tool.qr", "Create QR Code", "إنشاء QR", "") { Direct = () => ToolWindows.Open("qr", []) },
            new("archive.zip", "To ZIP", "ضغط ZIP", "") { Together = true },
            new("archive.extract", "Extract", "فك الضغط", ""),
        ]),
    ];

    /// <summary>Which section a file belongs in, so dropped files open the right one.</summary>
    public static int SectionFor(IReadOnlyList<string> files)
    {
        var kind = files.Count > 0 ? FileKinds.Of(files[0]) : FileKind.Other;
        return kind switch
        {
            FileKind.Video => 0,
            FileKind.Audio => 1,
            FileKind.Image => 2,
            FileKind.Pdf or FileKind.Document or FileKind.Spreadsheet or FileKind.Presentation => 3,
            _ => 4,
        };
    }
}
