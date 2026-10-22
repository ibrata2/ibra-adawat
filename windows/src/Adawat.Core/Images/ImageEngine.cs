using ImageMagick;

namespace Adawat.Core.Images;

/// <summary>Reading and writing pictures with ImageMagick: upright pixels, color profiles kept, metadata kept
/// unless asked otherwise, and an alpha channel only where the format can hold one.</summary>
public static class ImageEngine
{
    static ImageEngine()
    {
        // Large camera files and scans are fine; the default limits are conservative.
        ResourceLimits.Memory = 2UL * 1024 * 1024 * 1024;
    }

    /// <summary>The first frame, turned upright. Animated GIFs and multi-page TIFFs keep only their first page here.</summary>
    public static MagickImage Load(string path, int? maxPixels = null)
    {
        var settings = new MagickReadSettings();
        if (FileKinds.Ext(path) == "svg") settings.Density = new Density(192);
        var image = new MagickImage(path, settings);
        image.AutoOrient();
        if (maxPixels is { } max && Math.Max(image.Width, image.Height) > max) image.Resize(new MagickGeometry((uint)max, (uint)max));
        return image;
    }

    public static async Task<MagickImage> LoadAsync(string path, int? maxPixels = null) => await Task.Run(() => Load(path, maxPixels));

    /// <summary>Encodes <paramref name="image"/> in <paramref name="format"/> at quality 0…1.</summary>
    public static byte[] Encode(MagickImage image, ImageFormat format, double quality, bool keepMetadata = true)
    {
        using var copy = (MagickImage)image.Clone();
        Prepare(copy, format, quality, keepMetadata);
        return copy.ToByteArray(format.Magick);
    }

    public static void Save(MagickImage image, ImageFormat format, string path, double quality, bool keepMetadata = true)
    {
        using var copy = (MagickImage)image.Clone();
        Prepare(copy, format, quality, keepMetadata);
        copy.Write(path, format.Magick);
    }

    private static void Prepare(MagickImage image, ImageFormat format, double quality, bool keepMetadata)
    {
        image.Format = format.Magick;
        image.Quality = (uint)Math.Clamp(Math.Round(quality * 100), 1, 100);
        if (!keepMetadata)
        {
            var icc = image.GetColorProfile();
            image.Strip();
            if (icc != null) image.SetProfile(icc);
        }
        if (format == ImageFormat.Jpeg || format == ImageFormat.Bmp)
        {
            // No transparency in these formats: flatten onto white rather than black.
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }
        }
        if (format == ImageFormat.Jpeg) image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
        if (format == ImageFormat.WebP) image.Settings.SetDefine(MagickFormat.WebP, "method", "5");
        if (format == ImageFormat.Png) image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        if (format == ImageFormat.Ico)
        {
            if (Math.Max(image.Width, image.Height) > 256) image.Resize(new MagickGeometry(256, 256));
            image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
        }
        if (format == ImageFormat.Gif) image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
    }

    public static long Size(string path) => new FileInfo(path).Length;
}
