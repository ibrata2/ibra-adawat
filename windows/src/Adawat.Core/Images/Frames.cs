using ImageMagick;
using ImageMagick.Drawing;

namespace Adawat.Core.Images;

public readonly record struct Rgb(double R, double G, double B)
{
    public MagickColor Magick => new((byte)Math.Round(R * 255), (byte)Math.Round(G * 255), (byte)Math.Round(B * 255));
    public string Hex => $"#{(int)Math.Round(R * 255):X2}{(int)Math.Round(G * 255):X2}{(int)Math.Round(B * 255):X2}";
}

public abstract record FrameBackground
{
    public sealed record Gradient(int Index) : FrameBackground;
    public sealed record Solid(Rgb Color) : FrameBackground;
    public sealed record Picture(string Path) : FrameBackground;
    /// <summary>The photo itself, enlarged and blurred behind it.</summary>
    public sealed record Blurred : FrameBackground;

    /// <summary>Two-stop diagonal gradients, warm to cool.</summary>
    public static readonly Rgb[][] Gradients =
    [
        [new(0.96, 0.45, 0.33), new(0.98, 0.78, 0.33)],
        [new(0.98, 0.62, 0.55), new(0.6, 0.45, 0.92)],
        [new(1, 0.84, 0.4), new(0.95, 0.42, 0.62)],
        [new(0.98, 0.85, 0.88), new(0.82, 0.8, 0.98)],
        [new(0.8, 0.62, 0.92), new(0.98, 0.72, 0.8)],
        [new(0.13, 0.33, 0.42), new(0.95, 0.6, 0.36)],
        [new(0.35, 0.65, 0.98), new(0.6, 0.9, 0.95)],
        [new(0.45, 0.85, 0.82), new(0.25, 0.5, 0.92)],
        [new(0.92, 0.94, 1), new(0.68, 0.74, 0.98)],
    ];

    public static readonly Rgb[] Solids =
    [
        new(1, 1, 1), new(0.93, 0.78, 0.38), new(0.68, 0.8, 0.36), new(0.38, 0.62, 0.44), new(0.36, 0.6, 0.6),
        new(0.5, 0.78, 0.9), new(0.27, 0.4, 0.82), new(0.16, 0.2, 0.3), new(0.42, 0.3, 0.68),
    ];
}

public enum FrameRatio { Auto, Square, Wide, Portrait, Story }

public static class FrameRatios
{
    public static string Title(this FrameRatio ratio) => ratio switch
    {
        FrameRatio.Auto => "Auto", FrameRatio.Square => "1:1", FrameRatio.Wide => "16:9", FrameRatio.Portrait => "4:5", _ => "9:16",
    };

    public static double? Value(this FrameRatio ratio) => ratio switch
    {
        FrameRatio.Square => 1, FrameRatio.Wide => 16.0 / 9, FrameRatio.Portrait => 4.0 / 5, FrameRatio.Story => 9.0 / 16, _ => null,
    };
}

public sealed record FrameOptions
{
    public FrameBackground Background { get; init; } = new FrameBackground.Gradient(0);
    /// <summary>In output pixels.</summary>
    public double Padding { get; init; } = 160;
    public double Corners { get; init; } = 24;
    public double Shadow { get; init; } = 24;
    public FrameRatio Ratio { get; init; } = FrameRatio.Auto;
}

/// <summary>Places a photo on a new canvas: background, padding, rounded corners and a soft shadow.</summary>
public static class Frames
{
    /// <param name="scale">Shrinks every pixel measure for previews (1 for the saved photo).</param>
    public static MagickImage Render(MagickImage photo, FrameOptions options, double scale = 1)
    {
        var padding = options.Padding * scale;
        double width = photo.Width + padding * 2, height = photo.Height + padding * 2;
        if (options.Ratio.Value() is { } ratio)
        {
            if (width / height < ratio) width = height * ratio; else height = width / ratio;
        }
        uint w = (uint)Math.Round(width), h = (uint)Math.Round(height);
        if ((double)w * h > 400_000_000) throw new AdawatException("This frame would be too large. Use less padding or a closer output ratio.");
        var canvas = Background(options.Background, w, h, photo);
        var x = (int)Math.Round((w - photo.Width) / 2.0);
        var y = (int)Math.Round((h - photo.Height) / 2.0);
        var radius = Math.Min(options.Corners * scale, Math.Min(photo.Width, photo.Height) / 2.0);
        using var rounded = Rounded(photo, radius);
        if (options.Shadow > 0)
        {
            var blur = options.Shadow * scale;
            using var shadow = (MagickImage)rounded.Clone();
            shadow.BackgroundColor = new MagickColor(0, 0, 0, 100);
            shadow.Shadow(0, 0, blur / 2, new Percentage(40), MagickColors.Black);
            var offset = (int)Math.Round(blur * 0.35);
            canvas.Composite(shadow, x - (int)Math.Round(blur), y - (int)Math.Round(blur) + offset, CompositeOperator.Over);
        }
        canvas.Composite(rounded, x, y, CompositeOperator.Over);
        return canvas;
    }

    public static MagickImage Rounded(MagickImage photo, double radius)
    {
        var copy = (MagickImage)photo.Clone();
        copy.Alpha(AlphaAction.Set);
        if (radius <= 0.5) return copy;
        using var mask = new MagickImage(MagickColors.Transparent, copy.Width, copy.Height);
        new Drawables().FillColor(MagickColors.White).RoundRectangle(0, 0, copy.Width - 1, copy.Height - 1, radius, radius).Draw(mask);
        copy.Composite(mask, CompositeOperator.DstIn);
        return copy;
    }

    private static MagickImage Background(FrameBackground background, uint width, uint height, MagickImage photo)
    {
        switch (background)
        {
            case FrameBackground.Solid solid:
                return new MagickImage(solid.Color.Magick, width, height);
            case FrameBackground.Gradient gradient:
            {
                var stops = FrameBackground.Gradients[gradient.Index % FrameBackground.Gradients.Length];
                // Diagonal, light at the bottom left to the second colour at the top right, as on the Mac.
                var settings = new MagickReadSettings { Width = width, Height = height };
                settings.SetDefine("gradient:angle", "45");
                return new MagickImage($"gradient:{stops[0].Hex}-{stops[1].Hex}", settings);
            }
            case FrameBackground.Picture picture:
            {
                if (!File.Exists(picture.Path)) throw new AdawatException("Choose a background image.");
                var image = ImageEngine.Load(picture.Path, (int)Math.Max(width, height));
                return Fill(image, width, height);
            }
            default:
            {
                var image = (MagickImage)photo.Clone();
                image.Resize(Math.Max(width, height) / 8, 0);
                image.Blur(0, Math.Max(width, height) * 0.04 / 8);
                image.Modulate(new Percentage(94), new Percentage(115));
                var filled = Fill(image, width, height);
                return filled;
            }
        }
    }

    /// <summary>Scaled to cover the whole canvas and centred, edges cropped.</summary>
    public static MagickImage Fill(MagickImage image, uint width, uint height)
    {
        var scale = Math.Max((double)width / image.Width, (double)height / image.Height);
        image.Resize((uint)Math.Ceiling(image.Width * scale), (uint)Math.Ceiling(image.Height * scale));
        image.Crop(new MagickGeometry((int)((image.Width - width) / 2), (int)((image.Height - height) / 2), width, height));
        image.ResetPage();
        return image;
    }

    public static async Task<IReadOnlyList<string>> Apply(IReadOnlyList<string> files, FrameOptions options, OperationContext context)
    {
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Adding background {index + 1} of {files.Count}");
            context.Progress((double)index / files.Count);
            var file = files[index];
            var format = ImageOps.EditedFormat(file);
            var output = Output.PathFor(file, format.Ext, "framed");
            await Task.Run(() =>
            {
                using var photo = ImageEngine.Load(file);
                using var framed = Render(photo, options);
                ImageEngine.Save(framed, format, output, 0.92);
            });
            outputs.Add(output);
        }
        return outputs;
    }
}
