using ImageMagick;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Adawat.Core.Images;

/// <summary>Text and faces, with Windows' own engines.</summary>
public static partial class Recognition
{
    /// <summary>Lines of text, in reading order. Arabic is read when its Windows OCR pack is installed.</summary>
    public static async Task<string> Text(MagickImage image)
    {
        var engines = Engines();
        if (engines.Count == 0) throw new AdawatException("No text recognition languages are installed in Windows.");
        using var bitmap = await ToSoftwareBitmap(image, (int)OcrEngine.MaxImageDimension);
        string best = "";
        foreach (var engine in engines)
        {
            var result = await engine.RecognizeAsync(bitmap);
            var text = string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
            // Each engine reads its own script; keep the reading that found the most letters.
            if (text.Count(char.IsLetterOrDigit) > best.Count(char.IsLetterOrDigit)) best = text;
        }
        return best.Trim();
    }

    private static List<OcrEngine> Engines()
    {
        var engines = new List<OcrEngine>();
        foreach (var tag in new[] { "ar", "en-US", "en-GB" })
        {
            var language = new Language(tag);
            if (OcrEngine.IsLanguageSupported(language) && OcrEngine.TryCreateFromLanguage(language) is { } engine) engines.Add(engine);
        }
        if (engines.Count == 0 && OcrEngine.TryCreateFromUserProfileLanguages() is { } fallback) engines.Add(fallback);
        return engines;
    }

    /// <summary>Rectangles around faces, in pixels of <paramref name="image"/>.</summary>
    public static async Task<IReadOnlyList<(double X, double Y, double Width, double Height)>> Faces(MagickImage image)
    {
        if (!Windows.Media.FaceAnalysis.FaceDetector.IsSupported) return [];
        var detector = await Windows.Media.FaceAnalysis.FaceDetector.CreateAsync();
        using var bitmap = await ToSoftwareBitmap(image, 2400);
        var format = Windows.Media.FaceAnalysis.FaceDetector.GetSupportedBitmapPixelFormats().First();
        using var converted = SoftwareBitmap.Convert(bitmap, format);
        var scale = (double)image.Width / bitmap.PixelWidth;
        var faces = await detector.DetectFacesAsync(converted);
        return faces.Select(face => (face.FaceBox.X * scale, face.FaceBox.Y * scale, face.FaceBox.Width * scale, face.FaceBox.Height * scale)).ToList();
    }

    /// <summary>Boxes around lines of text, in pixels of <paramref name="image"/>.</summary>
    public static async Task<IReadOnlyList<(double X, double Y, double Width, double Height)>> TextBoxes(MagickImage image)
    {
        var engine = Engines().FirstOrDefault() ?? throw new AdawatException("No text recognition languages are installed in Windows.");
        using var bitmap = await ToSoftwareBitmap(image, (int)OcrEngine.MaxImageDimension);
        var scale = (double)image.Width / bitmap.PixelWidth;
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(line =>
        {
            var left = line.Words.Min(w => w.BoundingRect.X);
            var top = line.Words.Min(w => w.BoundingRect.Y);
            var right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            var bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            return ((double)left * scale, (double)top * scale, (double)(right - left) * scale, (double)(bottom - top) * scale);
        }).ToList();
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmap(MagickImage image, int maxDimension)
    {
        using var copy = (MagickImage)image.Clone();
        if (Math.Max(copy.Width, copy.Height) > maxDimension) copy.Resize(new MagickGeometry((uint)maxDimension, (uint)maxDimension));
        var png = copy.ToByteArray(MagickFormat.Png);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
        }
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
