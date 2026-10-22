namespace Adawat.Core;

public enum FileKind { Image, Pdf, Video, Audio, Document, Spreadsheet, Presentation, Subtitle, Archive, Folder, Other }

public static class FileKinds
{
    public static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { "jpg", "jpeg", "jfif", "png", "heic", "heif", "webp", "avif", "tif", "tiff", "gif", "bmp", "ico", "svg", "dng", "cr2", "cr3", "nef", "arw", "raf", "orf", "rw2", "psd", "tga", "jxl" };
    public static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "m4v", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "ts", "mts", "m2ts", "3gp", "ogv" };
    public static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
        { "mp3", "m4a", "aac", "wav", "flac", "aif", "aiff", "ogg", "oga", "opus", "wma", "alac", "caf", "amr" };
    public static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { "txt", "rtf", "doc", "docx", "odt", "html", "htm", "md", "markdown", "epub" };
    public static readonly HashSet<string> Spreadsheets = new(StringComparer.OrdinalIgnoreCase) { "xlsx", "xls", "csv", "tsv", "ods" };
    public static readonly HashSet<string> Presentations = new(StringComparer.OrdinalIgnoreCase) { "pptx", "ppt", "odp" };
    public static readonly HashSet<string> Subtitles = new(StringComparer.OrdinalIgnoreCase) { "srt", "vtt" };
    public static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { "zip", "tar", "gz", "tgz", "bz2", "xz", "rar", "7z", "tbz", "txz", "iso", "cab" };

    public static FileKind Of(string path)
    {
        if (Directory.Exists(path)) return FileKind.Folder;
        var ext = Ext(path);
        if (Spreadsheets.Contains(ext)) return FileKind.Spreadsheet;
        if (Presentations.Contains(ext)) return FileKind.Presentation;
        if (Subtitles.Contains(ext)) return FileKind.Subtitle;
        if (ext == "pdf") return FileKind.Pdf;
        if (Images.Contains(ext)) return FileKind.Image;
        if (Videos.Contains(ext)) return FileKind.Video;
        if (Audios.Contains(ext)) return FileKind.Audio;
        if (Documents.Contains(ext)) return FileKind.Document;
        if (Archives.Contains(ext)) return FileKind.Archive;
        return FileKind.Other;
    }

    /// <summary>The lower-case extension without the dot ("tar.gz" counts as "tgz").</summary>
    public static string Ext(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz")) return "tgz";
        return Path.GetExtension(name).TrimStart('.');
    }

    public static HashSet<FileKind> Kinds(this IEnumerable<string> files) => files.Select(Of).ToHashSet();

    /// <summary>The single kind shared by every file, if there is one.</summary>
    public static FileKind? CommonKind(this IReadOnlyCollection<string> files)
    {
        var kinds = files.Kinds();
        return kinds.Count == 1 ? kinds.First() : null;
    }
}
