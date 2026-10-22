using System.Globalization;
using ImageMagick;

namespace Adawat.Core.Images;

/// <summary>One line in the metadata editor: a read-only fact about the file, or an EXIF/IPTC field that can be changed or removed.</summary>
public sealed class MetadataField
{
    public required string Group { get; init; }
    public required string Name { get; init; }
    public required string Original { get; init; }
    public string Value { get; set; } = "";
    public bool Editable { get; init; }
    public bool Removed { get; set; }
    public ExifTag? Tag { get; init; }
    public IptcTag? Iptc { get; init; }
    public bool Changed => Removed || Value != Original;
}

/// <summary>Reads and writes photo metadata. JPEG edits rewrite only the EXIF block, so the picture itself is not re-encoded.</summary>
public static class ImageMetadata
{
    private static readonly HashSet<string> EditableExif = new(StringComparer.OrdinalIgnoreCase)
    {
        "Artist", "Copyright", "ImageDescription", "Make", "Model", "Software", "DateTime", "DateTimeOriginal", "DateTimeDigitized", "UserComment", "LensModel", "BodySerialNumber",
    };

    public static List<MetadataField> Read(string path)
    {
        var fields = new List<MetadataField>();
        using var image = new MagickImage(path);
        var info = new FileInfo(path);
        void Fact(string name, string value) => fields.Add(new MetadataField { Group = "File", Name = name, Original = value, Value = value });
        Fact("Format", ImageFormat.Of(path)?.Name ?? FileKinds.Ext(path).ToUpperInvariant());
        Fact("Dimensions", $"{image.Width} × {image.Height}");
        Fact("File Size", Format.Bytes(info.Length));
        Fact("Bits Per Sample", image.Depth.ToString(CultureInfo.InvariantCulture));
        Fact("Color Model", image.ColorSpace.ToString().ToUpperInvariant().Replace("SRGB", "RGB"));
        if (image.GetColorProfile() is { } icc) Fact("Color Profile", icc.Description ?? icc.Model ?? "ICC");
        if (image.Density.X > 0) Fact("Resolution", $"{Math.Round(image.Density.ChangeUnits(DensityUnit.PixelsPerInch).X)} dpi");

        if (image.GetExifProfile() is { } exif)
        {
            foreach (var value in exif.Values.OrderBy(v => v.Tag.ToString()))
            {
                var name = value.Tag.ToString();
                if (name is "MakerNote" or "PrintImageMatching" or "JPEGTables" || value.DataType == ExifDataType.Undefined && name != "UserComment") continue;
                var text = Describe(value);
                if (text.Length == 0 || text.Length > 200) continue;
                var group = name.StartsWith("GPS") ? "GPS" : "EXIF";
                fields.Add(new MetadataField
                {
                    Group = group, Name = Spaced(name), Original = text, Value = text, Tag = value.Tag,
                    Editable = EditableExif.Contains(name) && value.DataType == ExifDataType.String,
                });
            }
        }
        if (image.GetIptcProfile() is { } iptc)
        {
            foreach (var value in iptc.Values)
                fields.Add(new MetadataField { Group = "IPTC", Name = Spaced(value.Tag.ToString()), Original = value.Value, Value = value.Value, Iptc = value.Tag, Editable = true });
        }
        return fields;
    }

    private static string Describe(IExifValue value)
    {
        var raw = value.GetValue();
        return raw switch
        {
            null => "",
            string s => s.Trim('\0', ' '),
            Rational r => r.Denominator == 0 ? "" : (r.Numerator / (double)r.Denominator).ToString("0.####", CultureInfo.InvariantCulture),
            Rational[] rs => string.Join(", ", rs.Select(r => r.Denominator == 0 ? "0" : (r.Numerator / (double)r.Denominator).ToString("0.####", CultureInfo.InvariantCulture))),
            byte[] bytes => value.Tag.ToString() == "UserComment" ? System.Text.Encoding.UTF8.GetString(bytes.Skip(8).ToArray()).Trim('\0', ' ') : "",
            Array array => string.Join(", ", array.Cast<object>()),
            _ => raw.ToString() ?? "",
        };
    }

    /// <summary>"DateTimeOriginal" → "Date Time Original".</summary>
    private static string Spaced(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

    /// <summary>Writes a copy with the changes; every field marked removed is dropped, and "remove all" clears everything but the colour profile.</summary>
    public static async Task<string> Save(string path, IReadOnlyList<MetadataField> fields, bool removeAll, OperationContext context)
    {
        var output = Output.PathFor(path, FileKinds.Ext(path), removeAll ? "clean" : "edited");
        await Task.Run(() =>
        {
            using var image = new MagickImage(path);
            var exif = image.GetExifProfile() ?? new ExifProfile();
            var iptc = image.GetIptcProfile();
            if (removeAll)
            {
                var icc = image.GetColorProfile();
                var orientation = image.Orientation;
                if (FileKinds.Ext(path) is "jpg" or "jpeg")
                {
                    // Keep the picture bit-for-bit; drop every metadata block.
                    File.WriteAllBytes(output, JpegSegments.Rewrite(File.ReadAllBytes(path), null, dropXmpAndIptc: true));
                    return;
                }
                image.Strip();
                if (icc != null) image.SetProfile(icc);
                image.Orientation = orientation;
                image.Quality = 95;
                image.Write(output);
                return;
            }
            foreach (var field in fields.Where(f => f.Changed))
            {
                if (field.Tag is { } tag)
                {
                    if (field.Removed) exif.RemoveValue(tag);
                    else if (tag is ExifTag<string> text) exif.SetValue(text, field.Value);
                }
                else if (field.Iptc is { } iptcTag && iptc != null)
                {
                    iptc.RemoveValue(iptcTag);
                    if (!field.Removed) iptc.SetValue(iptcTag, field.Value);
                }
            }
            // Location fields go together: removing one removes them all.
            if (fields.Any(f => f.Group == "GPS" && f.Removed))
                foreach (var gps in exif.Values.Where(v => v.Tag.ToString().StartsWith("GPS")).Select(v => v.Tag).ToList()) exif.RemoveValue(gps);
            if (FileKinds.Ext(path) is "jpg" or "jpeg")
            {
                File.WriteAllBytes(output, JpegSegments.Rewrite(File.ReadAllBytes(path), exif.ToByteArray(), dropXmpAndIptc: false));
                return;
            }
            image.SetProfile(exif);
            if (iptc != null) image.SetProfile(iptc);
            image.Quality = 95;
            image.Write(output);
        });
        context.Finish("Metadata saved");
        return output;
    }
}

/// <summary>Replaces a JPEG's EXIF block without touching the compressed picture.</summary>
internal static class JpegSegments
{
    public static byte[] Rewrite(byte[] jpeg, byte[]? exif, bool dropXmpAndIptc)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new AdawatException("This file type's metadata cannot be edited here.");
        using var output = new MemoryStream();
        output.Write(jpeg, 0, 2);
        var position = 2;
        var wroteExif = false;
        void WriteExif()
        {
            if (wroteExif || exif == null || exif.Length == 0) return;
            wroteExif = true;
            var payload = exif.AsSpan().StartsWith("Exif\0\0"u8) ? exif : [.. "Exif\0\0"u8.ToArray(), .. exif];
            output.WriteByte(0xFF);
            output.WriteByte(0xE1);
            var length = payload.Length + 2;
            output.WriteByte((byte)(length >> 8));
            output.WriteByte((byte)length);
            output.Write(payload);
        }
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            var marker = jpeg[position + 1];
            if (marker == 0xDA) break;
            var length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            var segment = jpeg.AsSpan(position, length + 2);
            var isExif = marker == 0xE1 && length > 8 && jpeg.AsSpan(position + 4, 6).SequenceEqual("Exif\0\0"u8);
            var isXmp = marker == 0xE1 && !isExif;
            var isIptc = marker == 0xED;
            var isComment = marker == 0xFE;
            // EXIF goes after JFIF (APP0) when there is one, otherwise first.
            if (marker != 0xE0) WriteExif();
            if (!isExif && !(dropXmpAndIptc && (isXmp || isIptc || isComment))) output.Write(segment);
            position += length + 2;
        }
        WriteExif();
        output.Write(jpeg, position, jpeg.Length - position);
        return output.ToArray();
    }
}
