using ImageMagick;

namespace Adawat.Core.Images;

public enum CollageStyle { Grid, Row, Column, Featured }

public enum CollageCanvas { Square, Portrait, Story, Landscape, Classic }

public static class CollageCanvases
{
    public static string Title(this CollageCanvas canvas) => canvas switch
    {
        CollageCanvas.Square => "Square · 1:1", CollageCanvas.Portrait => "Portrait · 4:5", CollageCanvas.Story => "Story · 9:16",
        CollageCanvas.Landscape => "Landscape · 16:9", _ => "Classic · 4:3",
    };

    /// <summary>Width over height.</summary>
    public static double Ratio(this CollageCanvas canvas) => canvas switch
    {
        CollageCanvas.Square => 1, CollageCanvas.Portrait => 4.0 / 5, CollageCanvas.Story => 9.0 / 16, CollageCanvas.Landscape => 16.0 / 9, _ => 4.0 / 3,
    };
}

public sealed record CollageOptions
{
    public CollageStyle Style { get; init; } = CollageStyle.Grid;
    public int Columns { get; init; } = 2;
    public CollageCanvas Canvas { get; init; } = CollageCanvas.Square;
    public int Width { get; init; } = 2048;
    public bool Fill { get; init; } = true;
    public double Spacing { get; init; } = 16;
    public double Corners { get; init; }
    public Rgb Background { get; init; } = new(1, 1, 1);
}

/// <summary>Lays several photos out on one canvas.</summary>
public static class Collage
{
    public readonly record struct Cell(double X, double Y, double W, double H);

    public static List<Cell> Cells(int count, CollageOptions options, double width, double height, double spacing)
    {
        if (count <= 0) return [];
        List<Cell> Grid(int columns, int rows, Cell area, int n)
        {
            var w = (area.W - spacing * (columns + 1)) / columns;
            var h = (area.H - spacing * (rows + 1)) / rows;
            return Enumerable.Range(0, n).Select(i => new Cell(area.X + spacing + i % columns * (w + spacing), area.Y + spacing + i / columns * (h + spacing), w, h)).ToList();
        }
        var all = new Cell(0, 0, width, height);
        switch (options.Style)
        {
            case CollageStyle.Grid:
                var columns = Math.Min(Math.Max(1, options.Columns), count);
                return Grid(columns, (int)Math.Ceiling((double)count / columns), all, count);
            case CollageStyle.Row: return Grid(count, 1, all, count);
            case CollageStyle.Column: return Grid(1, count, all, count);
            default:
                if (count == 1) return Grid(1, 1, all, 1);
                // The first photo takes the top three fifths; the rest share a row underneath.
                var split = height * 0.6;
                return [.. Grid(1, 1, new Cell(0, 0, width, split + spacing / 2), 1), .. Grid(count - 1, 1, new Cell(0, split - spacing / 2, width, height - split + spacing / 2), count - 1)];
        }
    }

    /// <param name="scale">Shrinks the canvas and every pixel measure for previews.</param>
    public static MagickImage Render(IReadOnlyList<MagickImage> images, CollageOptions options, double scale = 1)
    {
        var width = Math.Round(options.Width * scale);
        var height = Math.Round(width / options.Canvas.Ratio());
        var canvas = new MagickImage(options.Background.Magick, (uint)width, (uint)height);
        var spacing = options.Spacing * scale;
        var radius = options.Corners * scale;
        var cells = Cells(images.Count, options, width, height, spacing);
        for (var index = 0; index < images.Count && index < cells.Count; index++)
        {
            var cell = cells[index];
            uint cw = (uint)Math.Max(1, Math.Round(cell.W)), ch = (uint)Math.Max(1, Math.Round(cell.H));
            using var photo = (MagickImage)images[index].Clone();
            using var tile = new MagickImage(MagickColors.Transparent, cw, ch);
            if (options.Fill)
            {
                using var filled = Frames.Fill((MagickImage)photo.Clone(), cw, ch);
                tile.Composite(filled, 0, 0, CompositeOperator.Over);
            }
            else
            {
                photo.Resize(new MagickGeometry(cw, ch));
                tile.Composite(photo, (int)((cw - photo.Width) / 2), (int)((ch - photo.Height) / 2), CompositeOperator.Over);
            }
            using var rounded = Frames.Rounded(tile, Math.Min(radius, Math.Min(cw, ch) / 2.0));
            canvas.Composite(rounded, (int)Math.Round(cell.X), (int)Math.Round(cell.Y), CompositeOperator.Over);
        }
        return canvas;
    }

    public static async Task<IReadOnlyList<string>> Make(IReadOnlyList<string> files, CollageOptions options, OperationContext context)
    {
        context.Report("Creating collage");
        if (options.Style == CollageStyle.Grid && options.Columns == 2) options = options with { Columns = files.Count <= 4 ? 2 : 3 };
        var output = Output.Named("Collage.jpg", files[0]);
        await Task.Run(() =>
        {
            var images = files.Select(file => ImageEngine.Load(file, options.Width)).ToList();
            try
            {
                using var collage = Render(images, options);
                ImageEngine.Save(collage, ImageFormat.Jpeg, output, 0.92);
            }
            finally
            {
                foreach (var image in images) image.Dispose();
            }
        });
        return [output];
    }
}
