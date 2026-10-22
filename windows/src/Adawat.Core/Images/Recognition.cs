using ImageMagick;
using ZXing;
using ZXing.Common;

namespace Adawat.Core.Images;

/// <summary>Text, QR codes and faces in pictures, all recognised on this computer. Codes are read the same way everywhere;
/// text and faces use each system's own engines (Platform/Windows, Platform/Linux).</summary>
public static partial class Recognition
{
    /// <summary>Every QR code (and other barcode) payload in the picture.</summary>
    public static IReadOnlyList<string> Codes(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        if (Math.Max(copy.Width, copy.Height) > 2400) copy.Resize(new MagickGeometry(2400, 2400));
        copy.ColorSpace = ColorSpace.Gray;
        using var pixels = copy.GetPixels();
        var gray = pixels.ToByteArray(PixelMapping.RGB)!;
        var luminance = new byte[copy.Width * copy.Height];
        for (var i = 0; i < luminance.Length; i++) luminance[i] = gray[i * 3];
        var source = new RGBLuminanceSource(luminance, (int)copy.Width, (int)copy.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var reader = new ZXing.Multi.GenericMultipleBarcodeReader(new MultiFormatReader());
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
        var results = reader.decodeMultiple(new BinaryBitmap(new HybridBinarizer(source)), hints);
        return results?.Select(result => result.Text).Distinct().ToList() ?? [];
    }
}
