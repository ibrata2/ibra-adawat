using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace Adawat.Core.Ibra;

/// <summary>
/// Finds and removes the information inside an image file that says it was made or edited with AI: C2PA Content Credentials
/// (JUMBF boxes), XMP and IPTC "digital source type" fields, EXIF software and description tags, and the prompt and workflow
/// text that generators such as Stable Diffusion and ComfyUI write into PNG files.
/// JPEG, PNG and WebP are cleaned without re-encoding, so the picture stays exactly the same; other formats are re-saved
/// at high quality. Watermarks drawn into the pixels themselves are not touched.
/// </summary>
public static class AiInfo
{
    /// <summary>Words that point to AI generation or to provenance records, searched in the file's bytes.</summary>
    private static readonly (string Needle, string Label)[] Signs =
    [
        ("c2pa", "C2PA Content Credentials"), ("jumb", "C2PA Content Credentials"), ("contentauth", "C2PA Content Credentials"),
        ("trainedAlgorithmicMedia", "IPTC: made by AI"), ("compositeWithTrainedAlgorithmicMedia", "IPTC: edited with AI"),
        ("algorithmicMedia", "IPTC: computer generated"), ("Made with AI", "\"Made with AI\""), ("AI generated", "\"AI generated\""),
        ("Firefly", "Adobe Firefly"), ("Generative Fill", "Photoshop Generative Fill"), ("DALL-E", "DALL·E"), ("DALL·E", "DALL·E"),
        ("OpenAI", "OpenAI"), ("ChatGPT", "ChatGPT"), ("Midjourney", "Midjourney"), ("Stable Diffusion", "Stable Diffusion"),
        ("ComfyUI", "ComfyUI"), ("\"workflow\"", "ComfyUI workflow"), ("Negative prompt", "Stable Diffusion prompt"),
        ("sd-metadata", "Stable Diffusion"), ("NovelAI", "NovelAI"), ("Imagen", "Google Imagen"), ("Gemini", "Google Gemini"),
        ("SynthID", "Google SynthID tag"), ("Leonardo.Ai", "Leonardo"), ("Ideogram", "Ideogram"), ("Flux", "Flux"), ("Grok", "Grok"),
        ("Bing Image Creator", "Bing Image Creator"), ("Microsoft Designer", "Microsoft Designer"), ("Canva", "Canva"),
    ];

    /// <summary>What the file says about AI, as short labels; empty when nothing was found.</summary>
    public static IReadOnlyList<string> Scan(string file)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); } catch (IOException) { return []; }
        string text;
        try { text = Encoding.Latin1.GetString(MetadataBytes(bytes)); }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidDataException) { text = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1 << 18)); }
        var found = new List<string>();
        foreach (var (needle, label) in Signs)
        {
            if (found.Contains(label)) continue;
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                text.Contains(string.Join("\0", needle.ToCharArray()), StringComparison.OrdinalIgnoreCase)) found.Add(label);
        }
        return found;
    }

    /// <summary>Only the metadata parts of a file (never the compressed picture), so picture bytes can't match by chance.</summary>
    private static byte[] MetadataBytes(byte[] b)
    {
        using var meta = new MemoryStream();
        if (IsJpeg(b))
        {
            var i = 2;
            while (i + 4 <= b.Length && b[i] == 0xFF)
            {
                var marker = b[i + 1];
                if (marker is 0xD9 or 0xDA) break;
                if (marker is 0xFF) { i++; continue; }
                var length = (b[i + 2] << 8) | b[i + 3];
                if (marker is >= 0xE1 and <= 0xEF or 0xFE) meta.Write(b, i + 4, Math.Min(length - 2, b.Length - i - 4));
                i += 2 + length;
            }
        }
        else if (IsPng(b))
        {
            var i = 8;
            while (i + 12 <= b.Length)
            {
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i));
                var type = Encoding.ASCII.GetString(b, i + 4, 4);
                if (length < 0 || i + 12 + length > b.Length) break;
                if (type is not ("IDAT" or "fdAT")) { meta.Write(b, i + 4, 4); meta.Write(b, i + 8, length); }
                i += 12 + length;
            }
        }
        else if (IsWebP(b))
        {
            var i = 12;
            while (i + 8 <= b.Length)
            {
                var type = Encoding.ASCII.GetString(b, i, 4);
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 4));
                if (length < 0 || i + 8 + length > b.Length) break;
                if (type is not ("VP8 " or "VP8L" or "ALPH" or "ANMF")) { meta.Write(b, i, 4); meta.Write(b, i + 8, length); }
                i += 8 + length + (length & 1);
            }
        }
        else
        {
            // HEIC, AVIF, TIFF and others keep their metadata near the start.
            meta.Write(b, 0, Math.Min(b.Length, 1 << 18));
        }
        return meta.ToArray();
    }

    public static Task<IReadOnlyList<string>> Check(IReadOnlyList<string> files, OperationContext context) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var lines = new List<string>();
        var marked = 0;
        foreach (var file in files)
        {
            var found = Scan(file);
            if (found.Count > 0) marked++;
            lines.Add(found.Count > 0 ? $"{Path.GetFileName(file)}: {string.Join(", ", found)}" : $"{Path.GetFileName(file)}: —");
        }
        Log(lines);
        context.Finish(marked == 0
            ? (files.Count == 1 ? "No AI information found in this image" : $"No AI information found in {files.Count} images")
            : files.Count == 1 ? $"AI information found: {string.Join(", ", Scan(files[0]).Take(3))}" : $"AI information found in {marked} of {files.Count} images");
        return [];
    });

    private static void Log(List<string> lines)
    {
        try { File.AppendAllLines(Path.Combine(Settings.Folder, "ai-info.log"), lines.Prepend($"— {DateTime.Now:yyyy-MM-dd HH:mm}")); } catch (IOException) { }
    }

    public static async Task<IReadOnlyList<string>> Remove(IReadOnlyList<string> files, OperationContext context)
    {
        var outputs = new List<string>();
        var hadInfo = 0;
        var stillMarked = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            context.Report($"Removing AI information {index + 1} of {files.Count}");
            context.Progress((double)index / files.Count);
            if (Scan(file).Count > 0) hadInfo++;
            var output = await Task.Run(() => Clean(file), context.Cancellation);
            outputs.Add(output);
            if (Scan(output) is { Count: > 0 } left) stillMarked.Add($"{Path.GetFileName(file)}: {string.Join(", ", left)}");
        }
        if (stillMarked.Count > 0) Log(stillMarked);
        context.Finish(stillMarked.Count > 0
            ? $"Cleaned {files.Count - stillMarked.Count} of {files.Count}; some information could not be removed"
            : hadInfo == 0 ? (files.Count == 1 ? "No AI information was found; saved a clean copy" : $"No AI information was found; saved {files.Count} clean copies")
            : files.Count == 1 ? "AI information removed" : $"AI information removed from {hadInfo} of {files.Count} images");
        return outputs;
    }

    /// <summary>Writes a copy without metadata next to the original (or where results go) and returns its path.</summary>
    public static string Clean(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var ext = FileKinds.Ext(file);
        byte[]? cleaned = null;
        try
        {
            if (IsJpeg(bytes) && Orientation(file) is "TopLeft" or "Undefined") cleaned = CleanJpeg(bytes);
            else if (IsPng(bytes)) cleaned = CleanPng(bytes);
            else if (IsWebP(bytes)) cleaned = CleanWebP(bytes);
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or InvalidDataException) { cleaned = null; }

        if (cleaned != null)
        {
            var output = Output.PathFor(file, ext, "clean");
            File.WriteAllBytes(output, cleaned);
            return output;
        }
        // Anything else (or a JPEG that needs turning upright first): re-save the picture with only its colour profile.
        var format = Images.ImageFormat.Of(file) ?? Images.ImageFormat.Png;
        var target = Output.PathFor(file, format.CanWrite ? format.Ext : "png", "clean");
        using var image = new MagickImage(file);
        image.AutoOrient();
        var icc = image.GetColorProfile();
        image.Strip();
        foreach (var name in image.AttributeNames.ToList()) image.RemoveAttribute(name);
        if (icc != null) image.SetProfile(icc);
        image.Quality = 96;
        image.Write(target, format.CanWrite ? format.Magick : MagickFormat.Png);
        return target;
    }

    /// <summary>The EXIF orientation by name ("TopLeft" means already upright).</summary>
    private static string Orientation(string file)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(file);
            return image.Orientation.ToString();
        }
        catch (MagickException) { return "Undefined"; }
    }

    // MARK: JPEG

    private static bool IsJpeg(byte[] b) => b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8;

    /// <summary>Keeps the JFIF header, the colour profile (APP2 ICC_PROFILE) and the picture; drops EXIF, XMP, IPTC, C2PA and comments.</summary>
    private static byte[] CleanJpeg(byte[] b)
    {
        using var output = new MemoryStream(b.Length);
        output.Write(b, 0, 2);
        var i = 2;
        while (i + 2 <= b.Length)
        {
            if (b[i] != 0xFF) throw new InvalidDataException("bad JPEG marker");
            var marker = b[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker == 0xD9) { output.Write(b, i, 2); break; }
            if (marker is >= 0xD0 and <= 0xD7 or 0x01) { output.Write(b, i, 2); i += 2; continue; }
            if (i + 4 > b.Length) throw new InvalidDataException("bad JPEG segment");
            var length = (b[i + 2] << 8) | b[i + 3];
            if (length < 2 || i + 2 + length > b.Length) throw new InvalidDataException("bad JPEG segment");
            var keep = marker switch
            {
                0xE0 => true,                                            // JFIF / JFXX
                0xE2 => Starts(b, i + 4, "ICC_PROFILE"),                 // colour profile only (APP2 can also hold MPF)
                0xEE => Starts(b, i + 4, "Adobe"),                       // Adobe colour transform flag (CMYK/YCC), not metadata
                >= 0xE1 and <= 0xEF => false,                            // EXIF, XMP, IPTC, C2PA (APP11) and the rest
                0xFE => false,                                           // comment
                _ => true,
            };
            if (keep) output.Write(b, i, 2 + length);
            if (marker == 0xDA)
            {
                // Start of scan: copy the compressed picture through to the end of the image.
                var start = i + 2 + length;
                var end = start;
                while (end + 1 < b.Length && !(b[end] == 0xFF && b[end + 1] == 0xD9)) end++;
                output.Write(b, start, end - start);
                i = end;
                continue;
            }
            i += 2 + length;
        }
        return output.ToArray();
    }

    private static bool Starts(byte[] b, int at, string text)
    {
        if (at + text.Length > b.Length) return false;
        for (var k = 0; k < text.Length; k++) if (b[at + k] != text[k]) return false;
        return true;
    }

    // MARK: PNG

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    private static readonly HashSet<string> PngKeep =
        ["IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "acTL", "fcTL", "fdAT", "cICP", "mDCv", "cLLi"];

    /// <summary>Keeps the picture, transparency, colour and animation chunks; drops text (prompts, workflows, XMP), EXIF and C2PA (caBX).</summary>
    private static byte[] CleanPng(byte[] b)
    {
        using var output = new MemoryStream(b.Length);
        output.Write(b, 0, 8);
        var i = 8;
        while (i + 12 <= b.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i));
            if (length < 0 || i + 12 + length > b.Length) throw new InvalidDataException("bad PNG chunk");
            var type = Encoding.ASCII.GetString(b, i + 4, 4);
            if (PngKeep.Contains(type)) output.Write(b, i, 12 + length);
            i += 12 + length;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }

    // MARK: WebP

    private static bool IsWebP(byte[] b) => b.Length > 12 && Starts(b, 0, "RIFF") && Starts(b, 8, "WEBP");

    private static readonly HashSet<string> WebPKeep = ["VP8 ", "VP8L", "VP8X", "ALPH", "ANIM", "ANMF", "ICCP"];

    /// <summary>Keeps the picture, alpha, animation and colour profile; drops EXIF, XMP and any other chunk, and clears their flags.</summary>
    private static byte[] CleanWebP(byte[] b)
    {
        using var body = new MemoryStream(b.Length);
        body.Write(Encoding.ASCII.GetBytes("WEBP"));
        var i = 12;
        while (i + 8 <= b.Length)
        {
            var type = Encoding.ASCII.GetString(b, i, 4);
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 4));
            var padded = length + (length & 1);
            if (length < 0 || i + 8 + length > b.Length) throw new InvalidDataException("bad WebP chunk");
            if (WebPKeep.Contains(type))
            {
                var chunk = b.AsSpan(i, 8 + Math.Min(padded, b.Length - i - 8)).ToArray();
                if (type == "VP8X" && chunk.Length > 8) chunk[8] &= unchecked((byte)~0x0C); // EXIF (0x08) and XMP (0x04) flags
                body.Write(chunk);
            }
            i += 8 + padded;
        }
        var content = body.ToArray();
        var result = new byte[8 + content.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)content.Length);
        content.CopyTo(result, 8);
        return result;
    }
}
