using System.Globalization;
using System.Text.RegularExpressions;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Adawat.Core.Ibra;

/// <summary>
/// Background removal with IS-Net (DIS, Apache 2.0, the "isnet-general-use" export from rembg), run on the CPU with ONNX Runtime.
/// The 170 MB model downloads the first time it is used.
/// </summary>
public static class BackgroundRemoval
{
    private const int Size = 1024;
    private const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx";

    private static InferenceSession? _session;
    private static readonly SemaphoreSlim Gate = new(1);

    private static string ModelPath => Path.Combine(IbraTools.Models, "isnet-general-use.onnx");

    public static async Task<IReadOnlyList<string>> Remove(IReadOnlyList<string> files, OperationContext context)
    {
        await Gate.WaitAsync(context.Cancellation);
        try
        {
            if (!File.Exists(ModelPath))
            {
                context.Report("Downloading the background model (one time only)");
                await IbraTools.Fetch(ModelUrl, ModelPath, context.Progress, context.Cancellation);
            }
            _session ??= new InferenceSession(ModelPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
            var outputs = new List<string>();
            for (var index = 0; index < files.Count; index++)
            {
                context.Report($"Removing background {index + 1} of {files.Count}");
                context.Progress((double)index / files.Count);
                var file = files[index];
                var output = Output.PathFor(file, "png", "no-bg");
                await Task.Run(() => RemoveOne(_session, file, output), context.Cancellation);
                outputs.Add(output);
            }
            return outputs;
        }
        finally { Gate.Release(); }
    }

    private static void RemoveOne(InferenceSession session, string file, string output)
    {
        using var image = new MagickImage(file);
        image.AutoOrient();
        using var copy = (MagickImage)image.Clone();
        copy.BackgroundColor = MagickColors.White;
        copy.Alpha(AlphaAction.Remove);
        copy.Resize(new MagickGeometry(Size, Size) { IgnoreAspectRatio = true });
        using var pixels = copy.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB)!;
        var max = 1f;
        foreach (var b in rgb) if (b > max) max = b;
        var input = new DenseTensor<float>([1, 3, Size, Size]);
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var i = (y * Size + x) * 3;
            for (var c = 0; c < 3; c++) input[0, c, y, x] = rgb[i + c] / max - 0.5f;
        }
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)]);
        var prediction = results.First().AsTensor<float>();
        float lo = float.MaxValue, hi = float.MinValue;
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var v = prediction[0, 0, y, x];
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        var range = Math.Max(hi - lo, 1e-6f);
        var mask = new byte[Size * Size * 3];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var value = (byte)Math.Clamp((prediction[0, 0, y, x] - lo) / range * 255f, 0, 255);
            var i = (y * Size + x) * 3;
            mask[i] = mask[i + 1] = mask[i + 2] = value;
        }
        using var alpha = new MagickImage();
        alpha.ReadPixels(mask, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGB));
        alpha.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
        alpha.ColorType = ColorType.Grayscale;
        image.Alpha(AlphaAction.Set);
        image.Composite(alpha, CompositeOperator.CopyAlpha);
        image.Write(output, MagickFormat.Png);
    }
}

/// <summary>Real-ESRGAN (BSD-3) through realesrgan-ncnn-vulkan, which runs on the graphics card (any Vulkan GPU, RTX included).</summary>
public static class Upscale
{
    private static string Tool => Path.Combine(AppContext.BaseDirectory, "tools", "realesrgan", OperatingSystem.IsWindows() ? "realesrgan-ncnn-vulkan.exe" : "realesrgan-ncnn-vulkan");

    public static Task<IReadOnlyList<string>> Quick(IReadOnlyList<string> files, OperationContext context) => Run(files, Settings.Current.UpscaleFactor, context);

    public static async Task<IReadOnlyList<string>> Run(IReadOnlyList<string> files, int factor, OperationContext context)
    {
        if (!File.Exists(Tool)) throw new AdawatException("The image enlarger is missing. Reinstall IBMC.");
        factor = factor >= 4 ? 4 : 2;
        var outputs = new List<string>();
        using var temp = Output.Temp();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Enlarging {index + 1} of {files.Count} (×{factor})");
            var file = files[index];
            var step = context.ProgressFor(index, files.Count);
            var input = temp.File($"in{index}.png");
            int width, height;
            using (var image = new MagickImage(file))
            {
                image.AutoOrient();
                width = (int)image.Width;
                height = (int)image.Height;
                image.Write(input, MagickFormat.Png);
            }
            var enlarged = temp.File($"out{index}.png");
            var percent = new Regex(@"(\d+(?:\.\d+)?)%");
            var result = await IbraTools.Run(Tool, ["-i", input, "-o", enlarged, "-n", "realesrgan-x4plus", "-s", "4", "-f", "png"], line =>
            {
                var m = percent.Match(line);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) step(p / 100 * 0.95);
            }, context.Cancellation, Path.GetDirectoryName(Tool));
            if (result.ExitCode != 0 || !File.Exists(enlarged))
                throw new AdawatException("Enlarging failed. The graphics card driver may need an update: " + IbraTools.LastLine(result));
            var ext = FileKinds.Ext(file) is "png" or "webp" ? FileKinds.Ext(file) : "jpg";
            var output = Output.PathFor(file, ext, $"x{factor}");
            using (var big = new MagickImage(enlarged))
            {
                if (factor == 2) big.Resize(new MagickGeometry((uint)(width * 2), (uint)(height * 2)) { IgnoreAspectRatio = true });
                if (ext == "jpg") big.Quality = 95;
                big.Write(output, ext switch { "png" => MagickFormat.Png, "webp" => MagickFormat.WebP, _ => MagickFormat.Jpeg });
            }
            outputs.Add(output);
            step(1);
        }
        return outputs;
    }
}

/// <summary>Downloads a video or its sound from a link (YouTube, Instagram, TikTok, Facebook…) with yt-dlp into Downloads.</summary>
public static class LinkDownload
{
    public static readonly IReadOnlyList<(string Id, string Title)> Qualities =
    [
        ("best", "Best quality"), ("1080", "1080p"), ("720", "720p"), ("480", "480p (small)"), ("mp3", "Sound only (MP3)"),
    ];

    public static bool CanRun => ProcessRunner.Find("yt-dlp") != null;

    public static async Task<IReadOnlyList<string>> Run(string url, string quality, OperationContext context)
    {
        var tool = ProcessRunner.Find("yt-dlp") ?? throw new AdawatException("The downloader is missing. Reinstall IBMC.");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AdawatException("Paste a link that starts with https://");
        var ffmpeg = ProcessRunner.Find("ffmpeg");
        var args = new List<string> { "--newline", "--no-playlist", "--windows-filenames", "--no-mtime", "-P", Output.Downloads,
                                      "-o", "%(title).90s.%(ext)s", "--print", "after_move:filepath" };
        if (ffmpeg != null) args.AddRange(["--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!]);
        switch (quality)
        {
            case "mp3":
                args.AddRange(["-x", "--audio-format", "mp3", "--audio-quality", "0"]);
                break;
            case "best":
                args.AddRange(["-f", "bv*[vcodec^=avc1]+ba[ext=m4a]/bv*+ba/b", "--merge-output-format", "mp4"]);
                break;
            default:
                args.AddRange(["-f", $"bv*[height<={quality}][vcodec^=avc1]+ba[ext=m4a]/bv*[height<={quality}]+ba/b[height<={quality}]/b", "--merge-output-format", "mp4"]);
                break;
        }
        args.Add(uri.ToString());
        context.Report("Downloading");
        var percent = new Regex(@"\[download\]\s+(\d+(?:\.\d+)?)%");
        var saved = new List<string>();
        var result = await IbraTools.Run(tool, args, line =>
        {
            var m = percent.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) context.Progress(p / 100);
            var trimmed = line.Trim();
            if (trimmed.Length > 3 && Path.IsPathRooted(trimmed) && File.Exists(trimmed)) lock (saved) saved.Add(trimmed);
        }, context.Cancellation, Output.Downloads);
        if (result.ExitCode != 0 && saved.Count == 0)
        {
            var last = IbraTools.LastLine(result);
            throw new AdawatException(last.Contains("login", StringComparison.OrdinalIgnoreCase) || last.Contains("private", StringComparison.OrdinalIgnoreCase)
                ? "This video is private or needs a login"
                : "Download failed: " + last);
        }
        return saved;
    }
}
