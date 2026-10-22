using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Adawat.Core;

/// <summary>QR codes drawn with WPF.</summary>
public sealed partial record QrDesign
{
    public BitmapSource? Logo { get; init; }

    private bool HasLogo => Logo != null;

    private (double Width, double Height)? LogoPixels => Logo is { } logo ? (logo.PixelWidth, logo.PixelHeight) : null;

    private byte[]? LogoPng() => Logo is { } logo ? Png(logo) : null;

    /// <summary>The code as a square bitmap <paramref name="pixels"/> wide. Call on a UI (STA) thread.</summary>
    public BitmapSource Image(QrMatrix matrix, int pixels)
    {
        var total = matrix.Size + Margin * 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform((double)pixels / total, (double)pixels / total));
            if (Background is { } background) dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, total, total));
            Brush codeBrush = GradientEnd is { } end
                ? new LinearGradientBrush(Foreground, end, new Point(0, 0), new Point(total, total)) { MappingMode = BrushMappingMode.Absolute }
                : new SolidColorBrush(Foreground);
            var modules = Geometry.Parse("F1 " + ModulePath(matrix));
            dc.DrawGeometry(codeBrush, null, modules);
            var eyes = Geometry.Parse("F0 " + EyePath(matrix));
            dc.DrawGeometry(EyeColor == Foreground ? codeBrush : new SolidColorBrush(EyeColor), null, eyes);
            if (PlatePath(matrix) is { } plate) dc.DrawGeometry(new SolidColorBrush(PlateColor), null, Geometry.Parse(plate));
            if (Logo != null && LogoRect(matrix) is { } logo) dc.DrawImage(Logo, logo);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        RenderOptions.SetEdgeMode(visual, Module == ModuleShape.Square && Eye == EyeShape.Square ? EdgeMode.Aliased : EdgeMode.Unspecified);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The code as a PNG <paramref name="pixels"/> wide. Call on a UI (STA) thread.</summary>
    public byte[] PngBytes(QrMatrix matrix, int pixels) => Png(Image(matrix, pixels));

    public static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
