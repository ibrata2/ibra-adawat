using ImageMagick;

namespace Adawat.Core.Images;

public sealed record ImageFormat(string Name, string Ext, MagickFormat Magick, bool Lossy)
{
    public static readonly ImageFormat Png = new("PNG", "png", MagickFormat.Png, false);
    public static readonly ImageFormat Jpeg = new("JPEG", "jpg", MagickFormat.Jpeg, true);
    public static readonly ImageFormat Heic = new("HEIC", "heic", MagickFormat.Heic, true);
    public static readonly ImageFormat WebP = new("WebP", "webp", MagickFormat.WebP, true);
    public static readonly ImageFormat Avif = new("AVIF", "avif", MagickFormat.Avif, true);
    public static readonly ImageFormat Tiff = new("TIFF", "tiff", MagickFormat.Tiff, false);
    public static readonly ImageFormat Gif = new("GIF", "gif", MagickFormat.Gif, false);
    public static readonly ImageFormat Bmp = new("BMP", "bmp", MagickFormat.Bmp, false);
    public static readonly ImageFormat Ico = new("ICO", "ico", MagickFormat.Ico, false);

    public static readonly IReadOnlyList<ImageFormat> All = [Png, Jpeg, Heic, WebP, Avif, Tiff, Gif, Bmp, Ico];

    public static ImageFormat? Of(string path) => FileKinds.Ext(path) switch
    {
        "jpeg" or "jfif" => Jpeg,
        "tif" => Tiff,
        "heif" => Heic,
        var ext => All.FirstOrDefault(format => format.Ext == ext),
    };

    /// <summary>Whether this build of ImageMagick can write the format.</summary>
    public bool CanWrite => MagickFormatInfo.Create(Magick)?.SupportsWriting ?? false;
}
