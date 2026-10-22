using Adawat.Core.Graphics;
using SkiaSharp;

namespace Adawat.Core;

/// <summary>QR codes drawn with Skia, from the same SVG path data the Windows app draws with WPF.</summary>
public sealed partial record QrDesign
{
    public SKBitmap? Logo { get; init; }

    private bool HasLogo => Logo != null;

    private (double Width, double Height)? LogoPixels => Logo is { } logo ? (logo.Width, logo.Height) : null;

    private byte[]? LogoPng() => Logo is { } logo ? Png(logo) : null;

    /// <summary>The code as a square bitmap <paramref name="pixels"/> wide.</summary>
    public SKBitmap Image(QrMatrix matrix, int pixels)
    {
        var total = matrix.Size + Margin * 2;
        var bitmap = new SKBitmap(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)pixels / total);
        // Square modules and eyes stay crisp; every other shape is smoothed.
        var smooth = !(Module == ModuleShape.Square && Eye == EyeShape.Square);
        if (Background is { } background)
            using (var fill = Paint(background, smooth)) canvas.DrawRect(0, 0, total, total, fill);
        using var code = Paint(Foreground, smooth);
        if (GradientEnd is { } end)
            code.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(total, total), [Sk(Foreground), Sk(end)], SKShaderTileMode.Clamp);
        using (var modules = Path(ModulePath(matrix), SKPathFillType.Winding)) canvas.DrawPath(modules, code);
        using (var eyes = Path(EyePath(matrix), SKPathFillType.EvenOdd))
        {
            if (EyeColor == Foreground) canvas.DrawPath(eyes, code);
            else using (var eyePaint = Paint(EyeColor, smooth)) canvas.DrawPath(eyes, eyePaint);
        }
        if (PlatePath(matrix) is { } platePath)
            using (var plate = Path(platePath, SKPathFillType.Winding))
            using (var plateFill = Paint(PlateColor, true)) canvas.DrawPath(plate, plateFill);
        if (Logo is { } logo && LogoRect(matrix) is { } rect)
        {
            using var image = SKImage.FromBitmap(logo);
            canvas.DrawImage(image, new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        canvas.Flush();
        return bitmap;
    }

    public byte[] PngBytes(QrMatrix matrix, int pixels)
    {
        using var bitmap = Image(matrix, pixels);
        return Png(bitmap);
    }

    public static byte[] Png(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor Sk(Color color) => new(color.R, color.G, color.B, color.A);

    private static SKPaint Paint(Color color, bool smooth) => new() { Color = Sk(color), IsAntialias = smooth, Style = SKPaintStyle.Fill };

    private static SKPath Path(string data, SKPathFillType fill)
    {
        var path = SKPath.ParseSvgPathData(data) ?? new SKPath();
        path.FillType = fill;
        return path;
    }
}
