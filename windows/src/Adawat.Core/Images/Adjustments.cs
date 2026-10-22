using ImageMagick;

namespace Adawat.Core.Images;

/// <summary>The photo editor's sliders, the same set and ranges as the Mac app's.</summary>
public enum Adjust
{
    Exposure, Brightness, Contrast, Highlights, Shadows, Whites, Blacks, Gamma,
    Temperature, Tint, Saturation, Vibrance, Hue,
    Clarity, Dehaze, Sharpness, NoiseReduction,
    Vignette, Grain, Fade,
}

public sealed record AdjustmentInfo(Adjust Key, string Title, double Min, double Max, string Unit);

public sealed class PhotoAdjustments
{
    public static readonly IReadOnlyList<(string Title, IReadOnlyList<AdjustmentInfo> Items)> Sections =
    [
        ("Light", [new(Adjust.Exposure, "Exposure", -3, 3, " EV"), new(Adjust.Brightness, "Brightness", -100, 100, ""), new(Adjust.Contrast, "Contrast", -100, 100, ""),
                   new(Adjust.Highlights, "Highlights", -100, 100, ""), new(Adjust.Shadows, "Shadows", -100, 100, ""), new(Adjust.Whites, "Whites", -100, 100, ""),
                   new(Adjust.Blacks, "Blacks", -100, 100, ""), new(Adjust.Gamma, "Gamma", -100, 100, "")]),
        ("Color", [new(Adjust.Temperature, "Temperature", -100, 100, ""), new(Adjust.Tint, "Tint", -100, 100, ""), new(Adjust.Saturation, "Saturation", -100, 100, ""),
                   new(Adjust.Vibrance, "Vibrance", -100, 100, ""), new(Adjust.Hue, "Hue", -180, 180, "°")]),
        ("Detail", [new(Adjust.Clarity, "Clarity", -100, 100, ""), new(Adjust.Dehaze, "Dehaze", -100, 100, ""), new(Adjust.Sharpness, "Sharpness", 0, 100, ""),
                    new(Adjust.NoiseReduction, "Noise Reduction", 0, 100, "")]),
        ("Effects", [new(Adjust.Vignette, "Vignette", -100, 100, ""), new(Adjust.Grain, "Grain", 0, 100, ""), new(Adjust.Fade, "Fade", 0, 100, "")]),
    ];

    private readonly Dictionary<Adjust, double> _values = new();

    public double this[Adjust key]
    {
        get => _values.TryGetValue(key, out var v) ? v : 0;
        set { if (Math.Abs(value) < 1e-9) _values.Remove(key); else _values[key] = value; }
    }

    public bool IsNeutral => _values.Count == 0;

    public PhotoAdjustments Copy()
    {
        var copy = new PhotoAdjustments();
        foreach (var (key, value) in _values) copy._values[key] = value;
        return copy;
    }

    /// <summary>Applies every non-zero slider. <paramref name="scale"/> is the preview's size relative to the full image,
    /// so radius-based effects look the same in the preview as in the saved photo.</summary>
    public void Apply(MagickImage image, double scale = 1)
    {
        if (IsNeutral) return;
        var hadAlpha = image.HasAlpha;
        if (this[Adjust.Exposure] != 0) image.Evaluate(Channels.RGB, EvaluateOperator.Multiply, Math.Pow(2, this[Adjust.Exposure]));

        // One tone curve carries brightness, contrast, highlights, shadows, whites, blacks, gamma, fade and dehaze.
        if (new[] { Adjust.Brightness, Adjust.Contrast, Adjust.Highlights, Adjust.Shadows, Adjust.Whites, Adjust.Blacks, Adjust.Gamma, Adjust.Fade, Adjust.Dehaze }.Any(k => this[k] != 0))
        {
            using var lut = Curve();
            image.Clut(lut, PixelInterpolateMethod.Bilinear);
        }
        if (this[Adjust.Temperature] != 0 || this[Adjust.Tint] != 0)
        {
            var t = this[Adjust.Temperature] / 100;
            var g = this[Adjust.Tint] / 100;
            image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1 + t * 0.16);
            image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1 - t * 0.16);
            image.Evaluate(Channels.Green, EvaluateOperator.Multiply, 1 - g * 0.12);
        }
        var saturation = this[Adjust.Saturation] + this[Adjust.Vibrance] * 0.6 + this[Adjust.Dehaze] * 0.15;
        if (saturation != 0 || this[Adjust.Hue] != 0)
            image.Modulate(new Percentage(100), new Percentage(Math.Max(0, 100 + saturation)), new Percentage(100 + this[Adjust.Hue] / 180 * 100));
        if (this[Adjust.Clarity] != 0)
        {
            // Local contrast: an unsharp mask with a wide radius.
            var radius = Math.Max(4, Math.Min(image.Width, image.Height) * 0.02);
            if (this[Adjust.Clarity] > 0) image.UnsharpMask(0, radius / 2, this[Adjust.Clarity] / 100 * 0.9, 0.02);
            else image.Blur(0, radius / 6 * -this[Adjust.Clarity] / 100);
        }
        if (this[Adjust.NoiseReduction] > 0) image.Statistic(StatisticType.Median, (uint)Math.Max(1, Math.Round(this[Adjust.NoiseReduction] / 100 * 3 * scale + 1)), (uint)Math.Max(1, Math.Round(this[Adjust.NoiseReduction] / 100 * 3 * scale + 1)));
        if (this[Adjust.Sharpness] > 0) image.Sharpen(0, Math.Max(0.4, 1.2 * scale) * (0.5 + this[Adjust.Sharpness] / 100));
        if (this[Adjust.Vignette] != 0)
        {
            using var mask = new MagickImage(MagickColors.White, image.Width, image.Height);
            var amount = Math.Abs(this[Adjust.Vignette]) / 100;
            mask.BackgroundColor = this[Adjust.Vignette] > 0 ? MagickColors.Black : MagickColors.White;
            mask.Vignette(0, Math.Max(image.Width, image.Height) * 0.35, 0, 0);
            mask.Evaluate(Channels.All, EvaluateOperator.Multiply, 1);
            using var blended = (MagickImage)image.Clone();
            blended.Composite(mask, this[Adjust.Vignette] > 0 ? CompositeOperator.Multiply : CompositeOperator.Screen);
            image.Composite(blended, CompositeOperator.Blend, $"{amount * 100}");
        }
        if (this[Adjust.Grain] > 0)
        {
            using var noise = new MagickImage(new MagickColor("#808080"), image.Width, image.Height);
            noise.AddNoise(NoiseType.Gaussian, this[Adjust.Grain] / 100 * 1.4);
            noise.ColorSpace = ColorSpace.Gray;
            image.Composite(noise, CompositeOperator.SoftLight);
        }
        if (!hadAlpha && image.HasAlpha) image.Alpha(AlphaAction.Off);
    }

    /// <summary>A 256-step curve image for Clut, built from the light sliders.</summary>
    private MagickImage Curve()
    {
        var brightness = this[Adjust.Brightness] / 100 * 0.25 - this[Adjust.Dehaze] / 100 * 0.04;
        var contrast = 1 + this[Adjust.Contrast] / 100 * 0.5 + this[Adjust.Dehaze] / 100 * 0.25;
        var highlights = this[Adjust.Highlights] / 100;
        var shadows = this[Adjust.Shadows] / 100;
        var whites = this[Adjust.Whites] / 100 * 0.15;
        var blacks = this[Adjust.Blacks] / 100 * 0.15 + this[Adjust.Fade] / 100 * 0.18;
        var gamma = Math.Pow(2, -this[Adjust.Gamma] / 100);
        var pixels = new byte[256 * 3];
        for (var i = 0; i < 256; i++)
        {
            var x = i / 255.0;
            // End points: blacks lift or crush the floor, whites move the ceiling.
            var floor = Math.Max(0, blacks);
            var ceiling = 1 + Math.Min(0, whites);
            var start = Math.Max(0, -blacks);
            var end = 1 - Math.Max(0, whites);
            var y = Math.Clamp((x - start) / Math.Max(1e-6, end - start), 0, 1) * (ceiling - floor) + floor;
            // Shadows lift the dark half, highlights pull the bright half, both fading toward the middle.
            y += shadows * 0.35 * Math.Pow(1 - y, 2) * y * 2;
            y += highlights * 0.35 * Math.Pow(y, 2) * (1 - y) * 2;
            y = Math.Pow(Math.Clamp(y, 0, 1), gamma);
            y = (y - 0.5) * contrast + 0.5 + brightness;
            var value = (byte)Math.Round(Math.Clamp(y, 0, 1) * 255);
            pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = value;
        }
        return new MagickImage(pixels, new MagickReadSettings { Width = 256, Height = 1, Format = MagickFormat.Rgb, Depth = 8 });
    }

    public static async Task<string> Save(string file, PhotoAdjustments adjustments, OperationContext context)
    {
        context.Report("Saving edited copy");
        var format = ImageOps.EditedFormat(file);
        var output = Output.PathFor(file, format.Ext, "edited");
        await Task.Run(() =>
        {
            using var image = ImageEngine.Load(file);
            adjustments.Apply(image);
            ImageEngine.Save(image, format, output, 0.92);
        });
        return output;
    }
}

/// <summary>Covers areas of a photo with solid boxes or a heavy pixelated blur. Rects are shares (0…1) of the upright photo.</summary>
public static class ImageRedaction
{
    public sealed record Box(double X, double Y, double W, double H);

    public static void Apply(MagickImage image, IEnumerable<Box> boxes, bool blur, Rgb color)
    {
        foreach (var box in boxes)
        {
            var geometry = new MagickGeometry((int)Math.Round(box.X * image.Width), (int)Math.Round(box.Y * image.Height),
                                              (uint)Math.Max(1, Math.Round(box.W * image.Width)), (uint)Math.Max(1, Math.Round(box.H * image.Height)));
            if (blur)
            {
                using var part = (MagickImage)image.Clone();
                part.Crop(geometry);
                part.ResetPage();
                // Pixelate first so blurred text cannot be sharpened back.
                part.Scale(Math.Max(2, part.Width / 16), Math.Max(2, part.Height / 16));
                part.Scale(geometry.Width, geometry.Height);
                part.Blur(0, Math.Max(2, Math.Min(geometry.Width, geometry.Height) / 12.0));
                image.Composite(part, geometry.X, geometry.Y, CompositeOperator.Over);
            }
            else
            {
                new ImageMagick.Drawing.Drawables().FillColor(color.Magick).Rectangle(geometry.X, geometry.Y, geometry.X + geometry.Width, geometry.Y + geometry.Height).Draw(image);
            }
        }
    }

    public static async Task<string> Save(string file, IReadOnlyList<Box> boxes, bool blur, Rgb color, OperationContext context)
    {
        context.Report("Saving redacted copy");
        var format = ImageOps.EditedFormat(file);
        var output = Output.PathFor(file, format.Ext, "redacted");
        await Task.Run(() =>
        {
            using var image = ImageEngine.Load(file);
            ApplyAndStrip(image, boxes, blur, color);
            ImageEngine.Save(image, format, output, 0.92, keepMetadata: false);
        });
        return output;
    }

    /// <summary>A redacted copy should not reveal where it was taken either.</summary>
    private static void ApplyAndStrip(MagickImage image, IReadOnlyList<Box> boxes, bool blur, Rgb color)
    {
        Apply(image, boxes, blur, color);
        image.RemoveProfile("exif");
        image.RemoveProfile("xmp");
        image.RemoveProfile("iptc");
    }
}
