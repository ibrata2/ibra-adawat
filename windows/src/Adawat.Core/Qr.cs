using System.Globalization;
using System.Text;
#if WINDOWS
using System.Windows;
using System.Windows.Media;
#else
using Adawat.Core.Graphics;
#endif
using QRCoder;

namespace Adawat.Core;

/// <summary>A QR code as its grid of modules.</summary>
public sealed class QrMatrix
{
    public enum Correction { Low, Medium, Quartile, High }

    public static string Title(Correction correction) => correction switch
    {
        Correction.Low => "Low · 7%", Correction.Medium => "Medium · 15%", Correction.Quartile => "Quartile · 25%", _ => "High · 30%",
    };

    public int Size { get; }
    private readonly bool[] _dark;

    public bool IsDark(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && _dark[y * Size + x];

    public QrMatrix(int size, bool[] dark) { Size = size; _dark = dark; }

    /// <summary>Encodes <paramref name="text"/>, or null when it does not fit a QR code at this correction level.</summary>
    public static QrMatrix? Encode(string text, Correction correction)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var level = correction switch { Correction.Low => QRCodeGenerator.ECCLevel.L, Correction.Medium => QRCodeGenerator.ECCLevel.M, Correction.Quartile => QRCodeGenerator.ECCLevel.Q, _ => QRCodeGenerator.ECCLevel.H };
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, level, forceUtf8: true);
            const int quiet = 4;
            var size = data.ModuleMatrix.Count - quiet * 2;
            var dark = new bool[size * size];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) dark[y * size + x] = data.ModuleMatrix[y + quiet][x + quiet];
            return new QrMatrix(size, dark);
        }
        catch (QRCoder.Exceptions.DataTooLongException) { return null; }
    }

    /// <summary>True inside the three 7×7 corner squares, which are drawn as whole shapes.</summary>
    public bool IsFinder(int x, int y)
    {
        // Real codes are at least 21 modules; smaller grids are the pickers' samples, which have no corners.
        if (Size < 21) return false;
        var far = Size - 7;
        return x < 7 && y < 7 || x >= far && y < 7 || x < 7 && y >= far;
    }

    public IEnumerable<(int X, int Y)> FinderOrigins => [(0, 0), (Size - 7, 0), (0, Size - 7)];
}

/// <summary>How a QR code looks. Everything is laid out in module units with the origin at the top left, as SVG path data,
/// so the same paths draw the PNG and become the SVG. The logo and the drawing itself are in Platform/*/QrDrawing.cs.</summary>
public sealed partial record QrDesign
{
    public enum ModuleShape { Square, Rounded, Dots, Diamond, Fluid, Bars }
    public enum EyeShape { Square, Rounded, Circle, Leaf }
    public enum Plate { None, Square, Rounded, Circle }

    public ModuleShape Module { get; init; } = ModuleShape.Square;
    public EyeShape Eye { get; init; } = EyeShape.Square;
    public Color Foreground { get; init; } = Colors.Black;
    /// <summary>The second colour of a diagonal gradient across the code, or null for one colour.</summary>
    public Color? GradientEnd { get; init; }
    public Color EyeColor { get; init; } = Colors.Black;
    public Color? Background { get; init; } = Colors.White;
    public int Margin { get; init; } = 4;
    /// <summary>The logo's width as a share of the code's width.</summary>
    public double LogoScale { get; init; } = 0.22;
    public Plate LogoPlate { get; init; } = Plate.Rounded;

    public static string Title(ModuleShape shape) => shape switch
    {
        ModuleShape.Square => "Squares", ModuleShape.Rounded => "Rounded", ModuleShape.Dots => "Dots", ModuleShape.Diamond => "Diamonds", ModuleShape.Fluid => "Fluid", _ => "Bars",
    };
    public static string Title(EyeShape shape) => shape switch { EyeShape.Square => "Square", EyeShape.Rounded => "Rounded", EyeShape.Circle => "Circle", _ => "Leaf" };
    public static string Title(Plate plate) => plate switch { Plate.None => "None", Plate.Square => "Square", Plate.Rounded => "Rounded", _ => "Circle" };

    // MARK: Layout

    public (Rect Logo, Rect Clear)? LogoBox(QrMatrix matrix)
    {
        if (!HasLogo) return null;
        var side = Math.Round(matrix.Size * LogoScale);
        var origin = (matrix.Size - side) / 2;
        var logo = new Rect(origin, origin, side, side);
        var clear = logo;
        clear.Inflate(LogoPlate == Plate.None ? 0.4 : 0.9, LogoPlate == Plate.None ? 0.4 : 0.9);
        return (logo, clear);
    }

    /// <summary>Every data module, in the chosen shape, as path data.</summary>
    public string ModulePath(QrMatrix matrix)
    {
        var path = new PathBuilder();
        var clear = LogoBox(matrix)?.Clear;
        bool Keep(int x, int y)
        {
            if (!matrix.IsDark(x, y) || matrix.IsFinder(x, y)) return false;
            if (clear is { } box && box.IntersectsWith(new Rect(x + 0.2, y + 0.2, 0.6, 0.6))) return false;
            return true;
        }
        for (var y = 0; y < matrix.Size; y++)
        for (var x = 0; x < matrix.Size; x++)
        {
            if (!Keep(x, y)) continue;
            var cell = new Rect(x + Margin, y + Margin, 1, 1);
            switch (Module)
            {
                case ModuleShape.Square: path.Rect(Inset(cell, -0.01)); break;
                case ModuleShape.Rounded: path.RoundedRect(Inset(cell, 0.06), 0.28, 0.28, 0.28, 0.28); break;
                case ModuleShape.Dots: path.Ellipse(Inset(cell, 0.07)); break;
                case ModuleShape.Diamond:
                    path.Polygon([(cell.X + 0.5, cell.Y + 0.02), (cell.Right - 0.02, cell.Y + 0.5), (cell.X + 0.5, cell.Bottom - 0.02), (cell.X + 0.02, cell.Y + 0.5)]);
                    break;
                case ModuleShape.Fluid:
                {
                    // Neighbours join flat; a corner is round only where both of its sides are open.
                    bool up = Keep(x, y - 1), down = Keep(x, y + 1), left = Keep(x - 1, y), right = Keep(x + 1, y);
                    const double r = 0.5;
                    path.RoundedRect(Inset(cell, -0.005), !up && !left ? r : 0, !up && !right ? r : 0, !down && !right ? r : 0, !down && !left ? r : 0);
                    break;
                }
                case ModuleShape.Bars:
                {
                    bool up = Keep(x, y - 1), down = Keep(x, y + 1);
                    const double r = 0.4;
                    path.RoundedRect(new Rect(cell.X + 0.1, cell.Y - (up ? 0.005 : 0), 0.8, 1 + (up ? 0.005 : 0) + (down ? 0.005 : 0)),
                                     up ? 0 : r, up ? 0 : r, down ? 0 : r, down ? 0 : r);
                    break;
                }
            }
        }
        return path.ToString();
    }

    /// <summary>The three corner squares: each a ring with a ball inside, filled even-odd.</summary>
    public string EyePath(QrMatrix matrix)
    {
        var path = new PathBuilder();
        foreach (var (x, y) in matrix.FinderOrigins) AddEye(Eye, new Rect(x + Margin, y + Margin, 7, 7), path);
        return path.ToString();
    }

    public static void AddEye(EyeShape eye, Rect outer, PathBuilder path)
    {
        var hole = Inset(outer, 1);
        var ball = Inset(outer, 2);
        switch (eye)
        {
            case EyeShape.Square: path.Rect(outer); path.Rect(hole); path.Rect(ball); break;
            case EyeShape.Rounded: path.RoundedRect(outer, 2, 2, 2, 2); path.RoundedRect(hole, 1.3, 1.3, 1.3, 1.3); path.RoundedRect(ball, 0.9, 0.9, 0.9, 0.9); break;
            case EyeShape.Circle: path.Ellipse(outer); path.Ellipse(hole); path.Ellipse(ball); break;
            default: path.RoundedRect(outer, 3, 0.4, 3, 0.4); path.RoundedRect(hole, 2.2, 0.2, 2.2, 0.2); path.RoundedRect(ball, 1.4, 0, 1.4, 0); break;
        }
    }

    public string? PlatePath(QrMatrix matrix)
    {
        if (LogoBox(matrix) is not { } box || LogoPlate == Plate.None) return null;
        var rect = box.Clear;
        rect.Offset(Margin, Margin);
        rect = Inset(rect, 0.3);
        var path = new PathBuilder();
        switch (LogoPlate)
        {
            case Plate.Square: path.Rect(rect); break;
            case Plate.Rounded: path.RoundedRect(rect, rect.Width * 0.2, rect.Width * 0.2, rect.Width * 0.2, rect.Width * 0.2); break;
            default: path.Ellipse(rect); break;
        }
        return path.ToString();
    }

    /// <summary>Where the logo itself goes: fitted inside its box, keeping its shape.</summary>
    public Rect? LogoRect(QrMatrix matrix)
    {
        if (LogoPixels is not { } pixels || LogoBox(matrix) is not { } box) return null;
        var rect = box.Logo;
        rect.Offset(Margin, Margin);
        if (LogoPlate == Plate.Circle) rect = Inset(rect, rect.Width * 0.12);
        var aspect = pixels.Width / Math.Max(1, pixels.Height);
        var size = aspect >= 1 ? new Size(rect.Width, rect.Width / aspect) : new Size(rect.Height * aspect, rect.Height);
        return new Rect(rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2, size.Width, size.Height);
    }

    public Color PlateColor => Background ?? Colors.White;

    private static Rect Inset(Rect rect, double by) => new(rect.X + by, rect.Y + by, Math.Max(0, rect.Width - by * 2), Math.Max(0, rect.Height - by * 2));

    /// <summary>The code as scalable SVG, one unit per module, with the logo embedded.</summary>
    public string Svg(QrMatrix matrix)
    {
        var total = matrix.Size + Margin * 2;
        var defs = "";
        var codeFill = Hex(Foreground);
        if (GradientEnd is { } end)
        {
            defs = $"<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"{total}\" y2=\"{total}\" gradientUnits=\"userSpaceOnUse\"><stop offset=\"0\" stop-color=\"{Hex(Foreground)}\"/><stop offset=\"1\" stop-color=\"{Hex(end)}\"/></linearGradient></defs>";
            codeFill = "url(#g)";
        }
        var eyeFill = EyeColor == Foreground ? codeFill : Hex(EyeColor);
        var body = new StringBuilder();
        if (Background is { } background) body.Append($"<rect width=\"{total}\" height=\"{total}\" fill=\"{Hex(background)}\"/>");
        body.Append($"<path fill=\"{codeFill}\" d=\"{ModulePath(matrix)}\"/>");
        body.Append($"<path fill=\"{eyeFill}\" fill-rule=\"evenodd\" d=\"{EyePath(matrix)}\"/>");
        if (PlatePath(matrix) is { } plate) body.Append($"<path fill=\"{Hex(PlateColor)}\" d=\"{plate}\"/>");
        if (LogoPng() is { } logo && LogoRect(matrix) is { } rect)
            body.Append($"<image x=\"{Num(rect.X)}\" y=\"{Num(rect.Y)}\" width=\"{Num(rect.Width)}\" height=\"{Num(rect.Height)}\" href=\"data:image/png;base64,{Convert.ToBase64String(logo)}\"/>");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {total}\">{defs}{body}</svg>\n";
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    internal static string Num(double value)
    {
        var text = value.ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}

/// <summary>Writes SVG path data ("M… L… A… Z") that both SVG files and WPF geometry understand.</summary>
public sealed class PathBuilder
{
    private readonly StringBuilder _d = new();

    private static string N(double v) => QrDesign.Num(v);

    public void Rect(Rect r) => _d.Append($"M{N(r.X)} {N(r.Y)}H{N(r.Right)}V{N(r.Bottom)}H{N(r.X)}Z");

    public void Polygon(IReadOnlyList<(double X, double Y)> points)
    {
        _d.Append($"M{N(points[0].X)} {N(points[0].Y)}");
        foreach (var (x, y) in points.Skip(1)) _d.Append($"L{N(x)} {N(y)}");
        _d.Append('Z');
    }

    public void Ellipse(Rect r)
    {
        double rx = r.Width / 2, ry = r.Height / 2, cx = r.X + rx;
        _d.Append($"M{N(cx)} {N(r.Y)}A{N(rx)} {N(ry)} 0 1 1 {N(cx)} {N(r.Bottom)}A{N(rx)} {N(ry)} 0 1 1 {N(cx)} {N(r.Y)}Z");
    }

    /// <summary>A rectangle with its own radius at each corner: top left, top right, bottom right, bottom left.</summary>
    public void RoundedRect(Rect r, double tl, double tr, double br, double bl)
    {
        var limit = Math.Min(r.Width, r.Height) / 2;
        tl = Math.Min(tl, limit); tr = Math.Min(tr, limit); br = Math.Min(br, limit); bl = Math.Min(bl, limit);
        _d.Append($"M{N(r.X + tl)} {N(r.Y)}H{N(r.Right - tr)}");
        if (tr > 0) _d.Append($"A{N(tr)} {N(tr)} 0 0 1 {N(r.Right)} {N(r.Y + tr)}");
        _d.Append($"V{N(r.Bottom - br)}");
        if (br > 0) _d.Append($"A{N(br)} {N(br)} 0 0 1 {N(r.Right - br)} {N(r.Bottom)}");
        _d.Append($"H{N(r.X + bl)}");
        if (bl > 0) _d.Append($"A{N(bl)} {N(bl)} 0 0 1 {N(r.X)} {N(r.Bottom - bl)}");
        _d.Append($"V{N(r.Y + tl)}");
        if (tl > 0) _d.Append($"A{N(tl)} {N(tl)} 0 0 1 {N(r.X + tl)} {N(r.Y)}");
        _d.Append('Z');
    }

    public override string ToString() => _d.ToString();
}

/// <summary>What a QR code holds, and how files become that text.</summary>
public static class QrContent
{
    public static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase) { "txt", "md", "markdown", "url", "vcf", "webloc" };

    public static bool CanEncode(IReadOnlyList<string> files) => files.Count == 1 && FileExtensions.Contains(FileKinds.Ext(files[0]));

    /// <summary>The text inside a text file, a Windows internet shortcut (.url), a Safari link (.webloc) or a contact card.</summary>
    public static string? Text(string file)
    {
        try
        {
            switch (FileKinds.Ext(file))
            {
                case "url":
                    return File.ReadAllLines(file).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];
                case "webloc":
                    var xml = System.Xml.Linq.XDocument.Load(file);
                    var key = xml.Descendants("key").FirstOrDefault(k => k.Value == "URL");
                    return (key?.ElementsAfterSelf("string").FirstOrDefault())?.Value;
                default:
                    return Documents.DocumentOps.ReadText(file).Trim();
            }
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException) { return null; }
    }

    public enum Security { Wpa, Wep, None }

    public static string Title(Security security) => security switch { Security.None => "None", Security.Wpa => "WPA / WPA2 / WPA3", _ => "WEP" };

    /// <summary>The text phones read as "join this network".</summary>
    public static string Wifi(string name, string password, Security security, bool hidden)
    {
        static string Escape(string value) => string.Concat(value.Select(c => "\\;,:\"".Contains(c) ? $"\\{c}" : c.ToString()));
        var type = security switch { Security.Wpa => "WPA", Security.Wep => "WEP", _ => "nopass" };
        var text = $"WIFI:T:{type};S:{Escape(name)};";
        if (security != Security.None) text += $"P:{Escape(password)};";
        if (hidden) text += "H:true;";
        return text + ";";
    }

    /// <summary>Without the window: a 1024 px PNG with the default style, next to the file.</summary>
    public static async Task<IReadOnlyList<string>> Quick(IReadOnlyList<string> files, OperationContext context)
    {
        var file = files[0];
        var text = Text(file);
        if (string.IsNullOrEmpty(text)) throw new AdawatException("No text found");
        var matrix = QrMatrix.Encode(text, QrMatrix.Correction.Medium) ?? throw new AdawatException("This is too long for a QR code. Shorten it or lower the error correction.");
        var output = Output.PathFor(file, "png", "QR");
        await Task.Run(() => Documents.Sta.Run(() => File.WriteAllBytes(output, new QrDesign().PngBytes(matrix, 1024))));
        return [output];
    }
}
