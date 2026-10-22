using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adawat.Core;
using ImageMagick;

namespace Adawat.Linux;

/// <summary>Pictures of files for Floats: the file manager's own thumbnail (the freedesktop.org cache that GNOME Files,
/// Dolphin and others fill), a quick one made here for pictures, or null so the caller shows the file kind's icon.</summary>
public static class Thumbnails
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Made = new();

    /// <summary>The thumbnail if one is ready now; otherwise null, and <paramref name="ready"/> is called when one is made.</summary>
    public static Bitmap? For(string path, Action? ready = null)
    {
        if (Made.TryGetValue(path, out var known)) return known;
        if (Cached(path) is { } cached) return Made[path] = cached;
        if (FileKinds.Of(path) is not (FileKind.Image or FileKind.Pdf)) return null;
        Made[path] = null;
        Task.Run(() =>
        {
            try
            {
                using var image = FileKinds.Of(path) == FileKind.Pdf ? Core.Pdf.PdfOps.RenderPage(path, 0, 48) : new MagickImage(path, new MagickReadSettings { Width = 256, Height = 256 });
                image.AutoOrient();
                image.Thumbnail(new MagickGeometry(160, 160));
                using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                var bitmap = new Bitmap(stream);
                Made[path] = bitmap;
                if (ready != null) Dispatcher.UIThread.Post(ready);
            }
            catch (Exception) { }
        });
        return null;
    }

    private static Bitmap? Cached(string path)
    {
        try
        {
            var uri = new Uri(path).AbsoluteUri;
            var name = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant() + ".png";
            var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            foreach (var size in new[] { "large", "x-large", "normal" })
            {
                var file = Path.Combine(cache, "thumbnails", size, name);
                if (File.Exists(file) && File.GetLastWriteTimeUtc(file) >= File.GetLastWriteTimeUtc(path)) return new Bitmap(file);
            }
        }
        catch (Exception) { }
        return null;
    }

    /// <summary>The icon for a file's kind, as a Segoe code point for <see cref="UI.Icon"/>.</summary>
    public static string KindIcon(string path) => FileKinds.Of(path) switch
    {
        FileKind.Image => Catalog.Icons.Photo,
        FileKind.Video => Catalog.Icons.Film,
        FileKind.Audio => Catalog.Icons.Audio,
        FileKind.Pdf => Catalog.Icons.Pdf,
        FileKind.Archive => Catalog.Icons.Archive,
        FileKind.Folder => Catalog.Icons.Folder,
        _ => Catalog.Icons.Document,
    };
}
