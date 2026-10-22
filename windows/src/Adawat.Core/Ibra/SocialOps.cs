using System.Globalization;
using System.Text;
using Adawat.Core.Media;
using ImageMagick;

namespace Adawat.Core.Ibra;

public sealed record WatermarkOptions
{
    public string Logo { get; init; } = "";
    public string Text { get; init; } = "";
    public string Position { get; init; } = "bottom-right";
    /// <summary>Logo width (or text height × 4) as a fraction of the picture's width.</summary>
    public double Scale { get; init; } = 0.18;
    public double Opacity { get; init; } = 0.85;

    public static WatermarkOptions Saved => new()
    {
        Logo = Settings.Current.WatermarkLogo, Text = Settings.Current.WatermarkText, Position = Settings.Current.WatermarkPosition,
        Scale = Settings.Current.WatermarkScale, Opacity = Settings.Current.WatermarkOpacity,
    };
}

public sealed record SocialSize(string Id, string Title, int Width, int Height);

/// <summary>Studio tools for social media: watermark, Instagram sizes, before/after, WhatsApp, stories, stickers, slideshows. All through ffmpeg.</summary>
public static class SocialOps
{
    public static readonly IReadOnlyList<SocialSize> Sizes =
    [
        new("story", "Story / Reels 9:16", 1080, 1920),
        new("post", "Post 4:5", 1080, 1350),
        new("square", "Square 1:1", 1080, 1080),
        new("wide", "YouTube 16:9", 1920, 1080),
    ];

    private static string I(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static readonly HashSet<string> FfmpegImages = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "bmp", "tif", "tiff", "gif" };

    /// <summary>An image ffmpeg can read: formats such as HEIC or RAW go through ImageMagick to PNG first.</summary>
    private static string Readable(string file, TempFolder temp)
    {
        if (FfmpegImages.Contains(FileKinds.Ext(file))) return file;
        var png = temp.File($"{Guid.NewGuid():N}.png");
        using var image = new MagickImage(file);
        image.AutoOrient();
        image.Write(png, MagickFormat.Png);
        return png;
    }

    /// <summary>Output extension and encoder arguments for a still picture made from <paramref name="source"/>.</summary>
    private static (string Ext, string[] Args) StillOutput(string source, bool alpha = false)
    {
        var ext = FileKinds.Ext(source) switch { "png" => "png", "webp" => "webp", "jpg" or "jpeg" => "jpg", _ => alpha ? "png" : "jpg" };
        if (alpha && ext == "jpg") ext = "png";
        return ext switch
        {
            "png" => ("png", new[] { "-frames:v", "1", "-update", "1" }),
            "webp" => ("webp", new[] { "-frames:v", "1", "-c:v", "libwebp", "-quality", "92" }),
            _ => ("jpg", new[] { "-frames:v", "1", "-update", "1", "-q:v", "2", "-pix_fmt", "yuvj444p" }),
        };
    }

    private static string[] VideoTail(MediaInfo info) =>
        [.. FFmpeg.H264, .. (info.HasAudio ? new[] { "-c:a", FFmpeg.Aac, "-b:a", "192k" } : Array.Empty<string>()), "-movflags", "+faststart"];

    private static async Task<string> Encode(IEnumerable<string> args, string output, double duration, Action<double>? progress, CancellationToken cancellation)
    {
        try
        {
            await FFmpeg.Run([.. args, output], duration, progress, cancellation);
            return output;
        }
        catch
        {
            IbraTools.TryDelete(output);
            throw;
        }
    }

    public static string FontFile(bool bold = true)
    {
        var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", bold ? "arialbd.ttf" : "arial.ttf");
        if (File.Exists(windows)) return windows;
        foreach (var candidate in new[] { "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf" })
            if (File.Exists(candidate)) return candidate;
        return windows;
    }

    // MARK: Watermark

    public static Task<IReadOnlyList<string>> WatermarkQuick(IReadOnlyList<string> files, OperationContext context) =>
        Watermark(files, WatermarkOptions.Saved, context);

    public static async Task<IReadOnlyList<string>> Watermark(IReadOnlyList<string> files, WatermarkOptions options, OperationContext context)
    {
        var hasLogo = !string.IsNullOrWhiteSpace(options.Logo) && File.Exists(options.Logo);
        var hasText = !string.IsNullOrWhiteSpace(options.Text);
        if (!hasLogo && !hasText) throw new AdawatException("Choose a logo or write a text for the watermark first");
        using var temp = Output.Temp();
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Adding watermark {index + 1} of {files.Count}");
            var file = files[index];
            var isVideo = FileKinds.Of(file) == FileKind.Video;
            var input = isVideo ? file : Readable(file, temp);
            var info = await MediaProbe.Info(input);
            if (info.Width <= 0) throw new AdawatException("Could not read the picture size");
            double w = info.Width, h = info.Height;
            var margin = Math.Round(Math.Min(w, h) * 0.035);
            string X(string itemW) => options.Position switch
            {
                "top-left" or "bottom-left" => I(margin),
                "center" => $"(main_w-{itemW})/2",
                _ => $"main_w-{itemW}-{I(margin)}",
            };
            string Y(string itemH) => options.Position switch
            {
                "top-left" or "top-right" => I(margin),
                "center" => $"(main_h-{itemH})/2",
                _ => $"main_h-{itemH}-{I(margin)}",
            };
            var args = new List<string> { "-i", input };
            var graph = new StringBuilder();
            var last = "0:v";
            double logoHeight = 0;
            if (hasLogo)
            {
                var logo = Readable(options.Logo, temp);
                args.AddRange(["-i", logo]);
                var logoWidth = Math.Max(16, Math.Round(w * Math.Clamp(options.Scale, 0.03, 0.9) / 2) * 2);
                var logoInfo = await MediaProbe.Info(logo);
                logoHeight = logoInfo.Width > 0 ? logoWidth * logoInfo.Height / logoInfo.Width : logoWidth;
                graph.Append($"[1:v]scale={I(logoWidth)}:-1,format=rgba,colorchannelmixer=aa={I(options.Opacity)}[logo];");
                graph.Append($"[{last}][logo]overlay=x={X("overlay_w")}:y={Y("overlay_h")}:format=auto[wm1];");
                last = "wm1";
            }
            if (hasText)
            {
                var textFile = temp.File($"text{index}.txt");
                await File.WriteAllTextAsync(textFile, options.Text.Trim(), new UTF8Encoding(false));
                var size = Math.Max(12, Math.Round(Math.Min(w, h) * Math.Clamp(options.Scale, 0.03, 0.9) / 4));
                var gap = Math.Round(margin * 0.4);
                var y = !hasLogo ? Y("text_h")
                    : options.Position.StartsWith("top") ? I(margin + logoHeight + gap)
                    : options.Position == "center" ? $"(main_h+{I(logoHeight)})/2+{I(gap)}"
                    : $"main_h-{I(margin + logoHeight + gap)}-text_h";
                y = y.Replace("main_h", "h");
                var x = X("text_w").Replace("main_w", "w");
                graph.Append($"[{last}]drawtext=fontfile={IbraTools.FilterPath(FontFile())}:textfile={IbraTools.FilterPath(textFile)}:fontsize={I(size)}:" +
                             $"fontcolor=white@{I(options.Opacity)}:borderw={I(Math.Max(1, size / 14))}:bordercolor=black@0.55:x={x}:y={y}[wm2];");
                last = "wm2";
            }
            var filter = graph.ToString().TrimEnd(';');
            args.AddRange(["-filter_complex", filter, "-map", $"[{last}]"]);
            string output;
            if (isVideo)
            {
                args.AddRange(["-map", "0:a?"]);
                args.AddRange(VideoTail(info));
                output = Output.PathFor(file, "mp4", "IBRA");
            }
            else
            {
                var (ext, still) = StillOutput(file);
                args.AddRange(still);
                output = Output.PathFor(file, ext, "IBRA");
            }
            outputs.Add(await Encode(args, output, info.Duration, context.ProgressFor(index, files.Count), context.Cancellation));
        }
        return outputs;
    }

    // MARK: Social sizes

    /// <summary>Fits the picture inside the frame; the empty space is filled by a blurred, darkened copy of it.</summary>
    public static string FitFilter(int width, int height, string input = "0:v", string output = "out") =>
        $"[{input}]split=2[{output}bs][{output}fs];" +
        $"[{output}bs]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},boxblur=40:4,eq=brightness=-0.08[{output}b];" +
        $"[{output}fs]scale={width}:{height}:force_original_aspect_ratio=decrease[{output}f];" +
        $"[{output}b][{output}f]overlay=(W-w)/2:(H-h)/2,setsar=1[{output}]";

    public static Task<IReadOnlyList<string>> ResizeQuick(IReadOnlyList<string> files, OperationContext context) =>
        Resize(files, Sizes.FirstOrDefault(s => s.Id == Settings.Current.SocialSize) ?? Sizes[0], context);

    public static async Task<IReadOnlyList<string>> Resize(IReadOnlyList<string> files, SocialSize size, OperationContext context)
    {
        using var temp = Output.Temp();
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Resizing {index + 1} of {files.Count}");
            var file = files[index];
            var isVideo = FileKinds.Of(file) == FileKind.Video;
            var input = isVideo ? file : Readable(file, temp);
            var info = await MediaProbe.Info(input);
            var args = new List<string> { "-i", input, "-filter_complex", FitFilter(size.Width, size.Height), "-map", "[out]" };
            string output;
            if (isVideo)
            {
                args.AddRange(["-map", "0:a?", .. VideoTail(info)]);
                output = Output.PathFor(file, "mp4", size.Id);
            }
            else
            {
                var (ext, still) = StillOutput(file);
                args.AddRange(still);
                output = Output.PathFor(file, ext, size.Id);
            }
            outputs.Add(await Encode(args, output, info.Duration, context.ProgressFor(index, files.Count), context.Cancellation));
        }
        return outputs;
    }

    // MARK: Before / after

    public static Task<IReadOnlyList<string>> BeforeAfterQuick(IReadOnlyList<string> files, OperationContext context) =>
        BeforeAfter(files, true, "قبل", "بعد", context);

    /// <summary>Two pictures side by side (or one above the other) as one 1080×1350 post, each with its label.</summary>
    public static async Task<IReadOnlyList<string>> BeforeAfter(IReadOnlyList<string> files, bool sideBySide, string beforeLabel, string afterLabel,
                                                                OperationContext context)
    {
        if (files.Count != 2) throw new AdawatException("Drop exactly two pictures: before, then after");
        return await BeforeAfterOrdered(files.OrderBy(f => File.GetLastWriteTimeUtc(f)).ToList(), sideBySide, beforeLabel, afterLabel, context);
    }

    /// <summary>Like <see cref="BeforeAfter"/>, with the first file as "before" as given.</summary>
    public static async Task<IReadOnlyList<string>> BeforeAfterOrdered(IReadOnlyList<string> ordered, bool sideBySide, string beforeLabel, string afterLabel,
                                                                       OperationContext context)
    {
        context.Report("Making the before and after picture");
        using var temp = Output.Temp();
        int cw = sideBySide ? 540 : 1080, ch = sideBySide ? 1350 : 675;
        var font = IbraTools.FilterPath(FontFile());
        string Label(string text, int i)
        {
            var path = temp.File($"label{i}.txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return $"drawtext=fontfile={font}:textfile={IbraTools.FilterPath(path)}:fontsize=52:fontcolor=white:box=1:boxcolor=0xE3192B@0.92:boxborderw=18:" +
                   "x=(w-text_w)/2:y=h-text_h-56";
        }
        var graph = $"[0:v]scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch},setsar=1,{Label(beforeLabel, 0)}[a];" +
                    $"[1:v]scale={cw}:{ch}:force_original_aspect_ratio=increase,crop={cw}:{ch},setsar=1,{Label(afterLabel, 1)}[b];" +
                    $"[a][b]{(sideBySide ? "hstack" : "vstack")}=inputs=2,drawbox=x={(sideBySide ? "iw/2-3" : "0")}:y={(sideBySide ? "0" : "ih/2-3")}:" +
                    $"w={(sideBySide ? "6" : "iw")}:h={(sideBySide ? "ih" : "6")}:color=white:t=fill[out]";
        string[] args = ["-i", Readable(ordered[0], temp), "-i", Readable(ordered[1], temp), "-filter_complex", graph, "-map", "[out]",
                         "-frames:v", "1", "-update", "1", "-q:v", "2", "-pix_fmt", "yuvj444p"];
        var output = Output.PathFor(ordered[0], "jpg", "before-after");
        return [await Encode(args, output, 0, null, context.Cancellation)];
    }

    // MARK: WhatsApp

    /// <summary>Re-encodes so the video stays under 15 MB at the best quality that fits, at most 1080p.</summary>
    public static async Task<IReadOnlyList<string>> WhatsApp(IReadOnlyList<string> files, OperationContext context)
    {
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Making it WhatsApp size {index + 1} of {files.Count}");
            var file = files[index];
            var info = await MediaProbe.Info(file);
            if (info.Duration <= 0) throw new AdawatException("Could not read the video length");
            const double budgetBits = 15.0 * 1024 * 1024 * 8 * 0.93;
            var audioBits = info.HasAudio ? 96_000.0 : 0;
            var videoBits = Math.Max(150_000, budgetBits / info.Duration - audioBits);
            var maxHeight = videoBits > 2_500_000 ? 1080 : videoBits > 900_000 ? 720 : videoBits > 450_000 ? 540 : 360;
            var portrait = info.Height > info.Width;
            var scale = portrait ? $"scale='min({maxHeight},iw)':-2" : $"scale=-2:'min({maxHeight},ih)'";
            var bits = (long)Math.Min(videoBits, 4_000_000);
            string[] rate = ["-b:v", bits.ToString(CultureInfo.InvariantCulture), "-maxrate", ((long)(bits * 1.15)).ToString(CultureInfo.InvariantCulture),
                             "-bufsize", (bits * 2).ToString(CultureInfo.InvariantCulture)];
            List<string> args = ["-i", file, "-map", "0:v:0", "-map", "0:a?", "-vf", scale, "-c:v", "libx264", "-preset", "medium", .. rate,
                                 "-pix_fmt", "yuv420p", "-profile:v", "high"];
            if (info.HasAudio) args.AddRange(["-c:a", FFmpeg.Aac, "-b:a", "96k", "-ac", "2"]);
            args.AddRange(["-movflags", "+faststart"]);
            outputs.Add(await Encode(args, Output.PathFor(file, "mp4", "whatsapp"), info.Duration, context.ProgressFor(index, files.Count), context.Cancellation));
        }
        return outputs;
    }

    // MARK: Story split

    public static Task<IReadOnlyList<string>> StorySplitQuick(IReadOnlyList<string> files, OperationContext context) =>
        StorySplit(files, Settings.Current.StorySeconds, context);

    public static async Task<IReadOnlyList<string>> StorySplit(IReadOnlyList<string> files, int seconds, OperationContext context)
    {
        seconds = Math.Clamp(seconds, 5, 600);
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Cutting into stories {index + 1} of {files.Count}");
            var file = files[index];
            var info = await MediaProbe.Info(file);
            var folder = Output.Directory(file);
            var stem = Path.GetFileNameWithoutExtension(file);
            var tag = Guid.NewGuid().ToString("N")[..6];
            var pattern = Path.Combine(folder, $"{stem} story {tag} %02d.mp4");
            List<string> args = ["-i", file, "-map", "0:v:0", "-map", "0:a?", .. FFmpeg.H264, "-force_key_frames", $"expr:gte(t,n_forced*{seconds})"];
            if (info.HasAudio) args.AddRange(["-c:a", FFmpeg.Aac, "-b:a", "160k"]);
            args.AddRange(["-f", "segment", "-segment_time", seconds.ToString(CultureInfo.InvariantCulture), "-reset_timestamps", "1"]);
            await FFmpeg.Run([.. args, pattern], info.Duration, context.ProgressFor(index, files.Count), context.Cancellation);
            var made = Directory.GetFiles(folder, $"{stem} story {tag} *.mp4").OrderBy(f => f, StringComparer.Ordinal).ToList();
            // Give the parts friendly names: "name story 1.mp4", "name story 2.mp4"…
            for (var part = 0; part < made.Count; part++)
            {
                var target = Output.Unique(Path.Combine(folder, $"{stem} story {part + 1}.mp4"));
                File.Move(made[part], target);
                outputs.Add(target);
            }
        }
        return outputs;
    }

    // MARK: Sticker

    /// <summary>A WhatsApp sticker: 512×512 WebP under 500 KB, animated for video (first 6 seconds), still for pictures.</summary>
    public static async Task<IReadOnlyList<string>> Sticker(IReadOnlyList<string> files, OperationContext context)
    {
        using var temp = Output.Temp();
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            context.Report($"Making sticker {index + 1} of {files.Count}");
            var file = files[index];
            var isVideo = FileKinds.Of(file) is FileKind.Video || FileKinds.Ext(file) == "gif";
            var input = isVideo ? file : Readable(file, temp);
            var output = Output.PathFor(file, "webp", "sticker");
            var fit = "scale=512:512:force_original_aspect_ratio=decrease,format=rgba,pad=512:512:(ow-iw)/2:(oh-ih)/2:color=0x00000000";
            if (!isVideo)
            {
                await Encode(["-i", input, "-vf", fit, "-frames:v", "1", "-c:v", "libwebp", "-quality", "90"], output, 0, null, context.Cancellation);
            }
            else
            {
                foreach (var (fps, quality) in new[] { (15, 60), (12, 45), (10, 32), (8, 22) })
                {
                    await Encode(["-t", "6", "-i", input, "-an", "-vf", $"fps={fps},{fit}", "-c:v", "libwebp", "-lossless", "0", "-quality", quality.ToString(CultureInfo.InvariantCulture),
                                  "-compression_level", "6", "-loop", "0"], output, 6, context.ProgressFor(index, files.Count), context.Cancellation);
                    if (new FileInfo(output).Length <= 500 * 1024) break;
                }
            }
            outputs.Add(output);
        }
        return outputs;
    }

    // MARK: Slideshow

    public static Task<IReadOnlyList<string>> SlideshowQuick(IReadOnlyList<string> files, OperationContext context) =>
        Slideshow(files, Settings.Current.SlideSeconds, Sizes[0], context);

    /// <summary>Pictures (and an optional song) into a 1080×1920 reel: each picture fitted on its blurred copy, crossfades between them.</summary>
    public static async Task<IReadOnlyList<string>> Slideshow(IReadOnlyList<string> files, double seconds, SocialSize size, OperationContext context)
    {
        var images = files.Where(f => FileKinds.Of(f) == FileKind.Image).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Take(80).ToList();
        var song = files.FirstOrDefault(f => FileKinds.Of(f) == FileKind.Audio);
        if (images.Count < 2) throw new AdawatException("Drop at least two pictures (and a song if you like)");
        context.Report("Making the slideshow");
        using var temp = Output.Temp();
        seconds = Math.Clamp(seconds, 1, 15);
        const double fade = 0.6;
        var args = new List<string>();
        foreach (var image in images)
            args.AddRange(["-loop", "1", "-framerate", "30", "-t", I(seconds + fade), "-i", Readable(image, temp)]);
        if (song != null) args.AddRange(["-i", song]);
        var graph = new StringBuilder();
        for (var i = 0; i < images.Count; i++)
        {
            graph.Append(FitFilter(size.Width, size.Height, $"{i}:v", $"f{i}raw")).Append(';');
            graph.Append($"[f{i}raw]fps=30,format=yuv420p[f{i}];");
        }
        var last = "f0";
        for (var i = 1; i < images.Count; i++)
        {
            var offset = i * seconds;
            graph.Append($"[{last}][f{i}]xfade=transition=fade:duration={I(fade)}:offset={I(offset)}[x{i}];");
            last = $"x{i}";
        }
        var total = images.Count * seconds + fade;
        args.AddRange(["-filter_complex", graph.ToString().TrimEnd(';'), "-map", $"[{last}]"]);
        if (song != null) args.AddRange(["-map", $"{images.Count}:a:0", "-af", $"afade=t=out:st={I(Math.Max(0, total - 1.5))}:d=1.5", "-c:a", FFmpeg.Aac, "-b:a", "192k"]);
        args.AddRange([.. FFmpeg.H264, "-t", I(total), "-movflags", "+faststart"]);
        var output = Output.PathFor(images[0], "mp4", "slideshow");
        return [await Encode(args, output, total, context.Progress, context.Cancellation)];
    }
}
