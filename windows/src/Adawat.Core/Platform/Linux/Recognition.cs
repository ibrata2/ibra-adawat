using System.Globalization;
using ImageMagick;

namespace Adawat.Core.Images;

/// <summary>Text with Tesseract (Arabic and English together), faces with a small detector that ships with the app.</summary>
public static partial class Recognition
{
    /// <summary>Lines of text, in reading order.</summary>
    public static async Task<string> Text(MagickImage image)
    {
        using var temp = Output.Temp();
        var png = Write(image, temp, 4000);
        var result = await ProcessRunner.Run(Tesseract(), [png.Path, "stdout", "-l", Languages(), "--psm", "3"]);
        if (result.ExitCode != 0) throw new AdawatException("Text recognition failed: " + Last(result.Error));
        // Tesseract leaves a blank line between paragraphs and a form feed at the end of the page.
        var lines = result.Output.Replace("\f", "").Split('\n').Select(line => line.TrimEnd());
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>Boxes around lines of text, in pixels of <paramref name="image"/>.</summary>
    public static async Task<IReadOnlyList<(double X, double Y, double Width, double Height)>> TextBoxes(MagickImage image)
    {
        using var temp = Output.Temp();
        var png = Write(image, temp, 4000);
        var result = await ProcessRunner.Run(Tesseract(), [png.Path, "stdout", "-l", Languages(), "--psm", "3", "tsv"]);
        if (result.ExitCode != 0) throw new AdawatException("Text recognition failed: " + Last(result.Error));
        // TSV columns: level page block paragraph line word left top width height confidence text. Words (level 5) are
        // gathered into their line so that lines Tesseract found no letters in are left out.
        var lines = new Dictionary<(int, int, int), (double Left, double Top, double Right, double Bottom)>();
        foreach (var row in result.Output.Split('\n').Skip(1))
        {
            var cells = row.Split('\t');
            if (cells.Length < 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            if (!double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || confidence < 30) continue;
            var key = (int.Parse(cells[2]), int.Parse(cells[3]), int.Parse(cells[4]));
            double left = int.Parse(cells[6]), top = int.Parse(cells[7]), right = left + int.Parse(cells[8]), bottom = top + int.Parse(cells[9]);
            lines[key] = lines.TryGetValue(key, out var box)
                ? (Math.Min(box.Left, left), Math.Min(box.Top, top), Math.Max(box.Right, right), Math.Max(box.Bottom, bottom))
                : (left, top, right, bottom);
        }
        return lines.Values.Select(b => (b.Left * png.Scale, b.Top * png.Scale, (b.Right - b.Left) * png.Scale, (b.Bottom - b.Top) * png.Scale)).ToList();
    }

    /// <summary>Rectangles around faces, in pixels of <paramref name="image"/>.</summary>
    public static Task<IReadOnlyList<(double X, double Y, double Width, double Height)>> Faces(MagickImage image) =>
        Task.Run(() => FaceDetector.Detect(image));

    private static string Tesseract() =>
        ProcessRunner.Find("tesseract") ?? throw new AdawatException("Text recognition needs Tesseract. Install it with: " +
            Desktop.Install("tesseract-ocr tesseract-ocr-ara", "tesseract tesseract-langpack-ara", "tesseract-ocr tesseract-ocr-traineddata-arabic", "tesseract tesseract-data-ara"));

    /// <summary>Arabic and English when both are installed, as Windows reads with both of its engines.</summary>
    private static string Languages()
    {
        if (_languages != null) return _languages;
        var installed = new HashSet<string>();
        try
        {
            var list = Task.Run(() => ProcessRunner.Run(Tesseract(), ["--list-langs"])).GetAwaiter().GetResult();
            foreach (var line in (list.Output + list.Error).Split('\n')) installed.Add(line.Trim());
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { }
        var wanted = new[] { "ara", "eng" }.Where(installed.Contains).ToList();
        return _languages = wanted.Count > 0 ? string.Join("+", wanted) : "eng";
    }

    private static string? _languages;

    private static (string Path, double Scale) Write(MagickImage image, TempFolder temp, int maxDimension)
    {
        using var copy = (MagickImage)image.Clone();
        if (Math.Max(copy.Width, copy.Height) > maxDimension) copy.Resize(new MagickGeometry((uint)maxDimension, (uint)maxDimension));
        var scale = (double)image.Width / copy.Width;
        copy.BackgroundColor = MagickColors.White;
        copy.Alpha(AlphaAction.Remove);
        var path = temp.File("page.png");
        copy.Write(path, MagickFormat.Png);
        return (path, scale);
    }

    private static string Last(string error) => error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
}
