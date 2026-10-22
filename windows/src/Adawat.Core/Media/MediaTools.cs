using System.Globalization;
using Adawat.Core.Images;

namespace Adawat.Core.Media;

/// <summary>A covered area of a video, shown from Start to End seconds. Rect is in shares (0…1) of the upright frame, top-left origin.</summary>
public sealed record VideoRedaction(double X, double Y, double W, double H, double Start, double End)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public enum VisualizerShape { Landscape, Portrait, Square }

public static class VisualizerShapes
{
    public static string Title(this VisualizerShape shape) => shape switch
    {
        VisualizerShape.Landscape => "Landscape · 16:9", VisualizerShape.Portrait => "Portrait · 9:16", _ => "Square · 1:1",
    };

    public static (int Width, int Height) Size(this VisualizerShape shape) => shape switch
    {
        VisualizerShape.Landscape => (1920, 1080), VisualizerShape.Portrait => (1080, 1920), _ => (1080, 1080),
    };
}

/// <summary>The video and audio tools behind the option windows: speed, crop, split, frames, redaction, loudness,
/// visualizer, channels and bleeps.</summary>
public static class MediaTools
{
    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string[] VideoEncoder(MediaInfo info) => info.IsHighDepth ? FFmpeg.Hevc10 : FFmpeg.H264;

    private static string VideoExtension(string file) => FileKinds.Ext(file) is "mov" or "mp4" or "m4v" or "mkv" ? FileKinds.Ext(file) : "mp4";

    private static string[] FastStart(string ext) => ext is "mp4" or "mov" or "m4v" ? ["-movflags", "+faststart"] : [];

    /// <summary>A faster or slower copy. Audio keeps its pitch through chained atempo filters (each 0.5…2).</summary>
    public static async Task<string> ChangeSpeed(string file, double speed, OperationContext context)
    {
        var info = await MediaProbe.Info(file);
        speed = Math.Clamp(speed, 0.25, 4);
        var tempo = new List<string>();
        var remaining = speed;
        while (remaining > 2) { tempo.Add("atempo=2"); remaining /= 2; }
        while (remaining < 0.5) { tempo.Add("atempo=0.5"); remaining /= 0.5; }
        tempo.Add($"atempo={N(remaining)}");
        var stretched = info with { Duration = info.Duration / speed };
        var label = speed == Math.Round(speed) ? $"{(int)speed}x" : speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        if (info.HasVideo)
        {
            var ext = VideoExtension(file);
            var output = Output.PathFor(file, ext, label);
            var args = new List<string> { "-map", "0:v:0", "-map", "0:a?", "-map_metadata", "0", "-filter:v", $"setpts=PTS/{N(speed)}" };
            args.AddRange(VideoEncoder(info));
            if (info.HasAudio) args.AddRange(["-filter:a", string.Join(",", tempo), "-c:a", FFmpeg.Aac, "-b:a", "192k"]);
            args.AddRange(FastStart(ext));
            await MediaOps.Encode(file, output, args, stretched, context.ProgressFor(0, 1), context: context);
            return output;
        }
        var audio = Output.PathFor(file, FileKinds.Ext(file), label);
        await MediaOps.Encode(file, audio, ["-vn", "-map", "0:a:0", "-map_metadata", "0", "-filter:a", string.Join(",", tempo), .. MediaOps.AudioCodec(FileKinds.Ext(file), info.AudioBits >= 24)],
                              stretched, context.ProgressFor(0, 1), context: context);
        return audio;
    }

    /// <summary>Crops to a rectangle of the upright frame (shares 0…1), keeping the audio untouched.</summary>
    public static async Task<IReadOnlyList<string>> Crop(IReadOnlyList<string> files, double x, double y, double w, double h, OperationContext context)
    {
        // Expressions on the frame's own size stay right for rotated phone videos; sizes stay even for the encoder.
        var crop = $"crop=w=trunc(iw*{N(w)}/2)*2:h=trunc(ih*{N(h)}/2)*2:x=trunc(iw*{N(x)}):y=trunc(ih*{N(y)})";
        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var info = await MediaProbe.Info(file);
            var ext = VideoExtension(file);
            var output = Output.PathFor(file, ext, "cropped");
            await MediaOps.Encode(file, output, ["-map", "0:v:0", "-map", "0:a?", "-map_metadata", "0", "-vf", crop, .. VideoEncoder(info), "-c:a", "copy", .. FastStart(ext)],
                                  info, context.ProgressFor(index, files.Count), context: context);
            outputs.Add(output);
        }
        return outputs;
    }

    /// <summary>Cuts the video at <paramref name="points"/>. Kept clips go into one folder, or are joined back into a single video.</summary>
    public static async Task<IReadOnlyList<string>> Split(string file, IReadOnlyList<double> points, ISet<int> removed, bool joinKept, OperationContext context)
    {
        var info = await MediaProbe.Info(file);
        var bounds = new List<double> { 0 };
        bounds.AddRange(points.OrderBy(p => p).Where(p => p > 0.05 && p < info.Duration - 0.05));
        bounds.Add(info.Duration);
        var segments = Enumerable.Range(0, bounds.Count - 1).Where(i => !removed.Contains(i)).Select(i => (Start: bounds[i], End: bounds[i + 1])).ToList();
        if (segments.Count == 0) throw new AdawatException("Keep at least one clip.");
        var stem = Path.GetFileNameWithoutExtension(file);
        var ext = FileKinds.Ext(file);
        if (joinKept)
        {
            using var staging = Output.Temp();
            var parts = new List<string>();
            for (var order = 0; order < segments.Count; order++)
            {
                context.Report($"Cutting clip {order + 1} of {segments.Count}");
                var part = staging.File($"part {order}.{ext}");
                await MediaOps.CutSegment(file, segments[order].Start, segments[order].End, part, info, context.ProgressFor(order, segments.Count + 1), context);
                parts.Add(part);
            }
            context.Report($"Joining {parts.Count} clips");
            var output = Output.PathFor(file, VideoExtension(file), "edited");
            if (parts.Count == 1) File.Move(parts[0], output);
            else
            {
                var joined = await MediaOps.Join(parts, context);
                File.Move(joined[0], output);
            }
            return [output];
        }
        var folder = Output.Named($"{stem} clips", file);
        Directory.CreateDirectory(folder);
        for (var order = 0; order < segments.Count; order++)
        {
            context.Report($"Saving clip {order + 1} of {segments.Count}");
            var output = Path.Combine(folder, $"{stem} {order + 1:00}.{ext}");
            await MediaOps.CutSegment(file, segments[order].Start, segments[order].End, output, info, context.ProgressFor(order, segments.Count), context);
        }
        context.Finish(segments.Count == 1 ? "Saved 1 clip" : $"Saved {segments.Count} clips");
        return [folder];
    }

    /// <summary>Full-resolution PNGs at exact times; several go into a folder.</summary>
    public static async Task<IReadOnlyList<string>> SaveFrames(string file, IReadOnlyList<double> times, OperationContext context)
    {
        if (times.Count == 0) throw new AdawatException("Add a frame to save.");
        var stem = Path.GetFileNameWithoutExtension(file);
        string? folder = times.Count > 1 ? Output.Named($"{stem} frames", file) : null;
        if (folder != null) Directory.CreateDirectory(folder);
        var outputs = new List<string>();
        var sorted = times.OrderBy(t => t).ToList();
        for (var index = 0; index < sorted.Count; index++)
        {
            context.Progress((double)index / sorted.Count);
            var time = sorted[index];
            var output = folder != null ? Path.Combine(folder, $"Frame {index + 1:000} - {time.ToString("0.000", CultureInfo.InvariantCulture)}s.png")
                                        : Output.PathFor(file, "png", time.ToString("0.000", CultureInfo.InvariantCulture) + "s");
            await MediaOps.Frame(file, time, output);
            if (folder == null) outputs.Add(output);
        }
        return folder != null ? [folder] : outputs;
    }

    /// <summary>Covers each area for its time range: a filled box, or a pixelated blur.</summary>
    public static async Task<string> Redact(string file, IReadOnlyList<VideoRedaction> redactions, bool blur, Rgb color, OperationContext context)
    {
        if (redactions.Count == 0) throw new AdawatException("Draw at least one redaction.");
        var info = await MediaProbe.Info(file);
        static string Enable(VideoRedaction r) => $"between(t\\,{N(r.Start)}\\,{N(r.End)})";
        var format = info.IsHighDepth ? "yuv420p10le" : "yuv420p";
        string graph;
        if (blur)
        {
            var g = new System.Text.StringBuilder("[0:v]null[v0];");
            for (var index = 0; index < redactions.Count; index++)
            {
                var r = redactions[index];
                g.Append($"[v{index}]split[b{index}][c{index}];[c{index}]crop=w=iw*{N(r.W)}:h=ih*{N(r.H)}:x=iw*{N(r.X)}:y=ih*{N(r.Y)},");
                g.Append($"scale=w=max(2\\,trunc(iw/16)):h=max(2\\,trunc(ih/16)),scale=w=iw*16:h=ih*16:flags=neighbor,boxblur=4[p{index}];");
                g.Append($"[b{index}][p{index}]overlay=x=main_w*{N(r.X)}:y=main_h*{N(r.Y)}:enable='{Enable(r)}'[v{index + 1}];");
            }
            g.Append($"[v{redactions.Count}]format={format}[out]");
            graph = g.ToString();
        }
        else
        {
            var hex = $"0x{(int)(color.R * 255):X2}{(int)(color.G * 255):X2}{(int)(color.B * 255):X2}";
            graph = "[0:v]" + string.Join(",", redactions.Select(r => $"drawbox=x=iw*{N(r.X)}:y=ih*{N(r.Y)}:w=iw*{N(r.W)}:h=ih*{N(r.H)}:color={hex}@1:t=fill:enable='{Enable(r)}'"))
                    + $",format={format}[out]";
        }
        var ext = VideoExtension(file);
        var output = Output.PathFor(file, ext, "redacted");
        // A redacted copy should not reveal where it was filmed either.
        await MediaOps.Encode(file, output, ["-filter_complex", graph, "-map", "[out]", "-map", "0:a?", "-map_metadata", "-1", .. VideoEncoder(info), "-c:a", "copy", .. FastStart(ext)],
                              info, context.ProgressFor(0, 1), context: context);
        return output;
    }

    /// <summary>An MP4 of the audio over an animated waveform, or over a still image.</summary>
    public static async Task<string> Visualize(string file, string? image, VisualizerShape shape, Rgb color, OperationContext context)
    {
        var info = await MediaProbe.Info(file);
        if (!info.HasAudio) throw new AdawatException("This file has no playable audio.");
        var (w, h) = shape.Size();
        var output = Output.PathFor(file, "mp4", "visualizer");
        var hex = $"0x{(int)(color.R * 255):X2}{(int)(color.G * 255):X2}{(int)(color.B * 255):X2}";
        // draw=full paints the chosen colour as is; the default blending adds overlapping samples until the colour overflows.
        List<string> args = image != null
            ? ["-loop", "1", "-framerate", "15", "-i", image, "-i", file, "-filter_complex",
               $"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,format=rgba,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black,format=yuv420p[v]", "-map", "[v]", "-map", "1:a:0"]
            : ["-i", file, "-filter_complex", $"[0:a]showwaves=s={(w - w / 8) / 2 * 2}x{h / 3 / 2 * 2}:mode=cline:rate=30:colors={hex}:scale=sqrt:draw=full,format=rgba,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black,format=yuv420p[v]",
               "-map", "[v]", "-map", "0:a:0"];
        args.AddRange([.. FFmpeg.H264, "-c:a", FFmpeg.Aac, "-b:a", "192k", "-shortest", "-movflags", "+faststart", output]);
        try { await FFmpeg.Run(args, info.Duration, context.ProgressFor(0, 1), context.Cancellation); }
        catch { MediaOps.TryDelete(output); throw; }
        return output;
    }

    /// <summary>The pan filter for a mono or stereo result with per-side volume; <paramref name="solo"/> previews one source channel.</summary>
    public static string ChannelFilter(MediaInfo info, bool mono, double left, double right, int? solo = null)
    {
        if (info.AudioChannels < 2)
        {
            // One source channel: a term may not name it twice, so the two sides' gains are combined.
            if (solo != null) return "pan=mono|c0=c0";
            if (mono) return $"pan=mono|c0={N((left + right) / 2)}*c0";
            return $"pan=stereo|c0={N(left)}*c0|c1={N(right)}*c0";
        }
        if (solo is { } s) return $"pan=mono|c0=c{(s == 0 ? 0 : 1)}";
        if (mono) return $"pan=mono|c0={N(left * 0.5)}*c0+{N(right * 0.5)}*c1";
        return $"pan=stereo|c0={N(left)}*c0|c1={N(right)}*c1";
    }

    public static async Task<string> ConvertChannels(string file, bool mono, double left, double right, OperationContext context)
    {
        var info = await MediaProbe.Info(file);
        if (!info.HasAudio) throw new AdawatException("Choose Mono or Stereo for a file with an audio track.");
        var args = new List<string> { "-map", "0:v?", "-map", "0:a:0", "-map_metadata", "0", "-af", ChannelFilter(info, mono, left, right) };
        if (info.HasVideo) args.AddRange(["-c:v", "copy"]);
        args.AddRange(MediaOps.AudioCodec(FileKinds.Ext(file), info.AudioBits >= 24));
        var output = Output.PathFor(file, FileKinds.Ext(file), mono ? "mono" : "stereo");
        await MediaOps.Encode(file, output, args, info, context.ProgressFor(0, 1), context: context);
        return output;
    }

    public static string BleepFilter(IReadOnlyList<(double Start, double End)> ranges, double duration)
    {
        var windows = string.Join("+", ranges.Select(r => $"between(t\\,{N(r.Start)}\\,{N(r.End)})"));
        return $"[0:a:0]aresample=48000,volume=0:enable='{windows}'[muted];"
             + $"sine=frequency=1000:sample_rate=48000:duration={N(duration)},volume=0.3,volume=0:enable='not({windows})'[tone];"
             + "[muted][tone]amix=inputs=2:duration=first:normalize=0[a]";
    }

    /// <summary>Replaces each range with a 1 kHz tone; video, if any, is copied untouched.</summary>
    public static async Task<string> Bleep(string file, IReadOnlyList<(double Start, double End)> ranges, OperationContext? context, string? target = null)
    {
        if (ranges.Count == 0) throw new AdawatException("Add at least one bleep range.");
        var info = await MediaProbe.Info(file);
        if (!info.HasAudio) throw new AdawatException("This file has no audio track.");
        var ext = target != null ? FileKinds.Ext(target) : FileKinds.Ext(file);
        var output = target ?? Output.PathFor(file, ext, "bleeped");
        var args = new List<string> { "-filter_complex", BleepFilter(ranges, info.Duration), "-map", "0:v?", "-map", "[a]", "-map_metadata", "0" };
        if (info.HasVideo && target == null) args.AddRange(["-c:v", "copy"]);
        args.AddRange(MediaOps.AudioCodec(ext, info.AudioBits >= 24));
        await MediaOps.Encode(file, output, args, info, context?.ProgressFor(0, 1), context: context);
        return output;
    }

    /// <summary>A short temporary rendering for in-window previews (channel output, bleeps).</summary>
    public static async Task<string> PreviewAudio(string file, string filter, bool complex)
    {
        // Windows plays AAC itself; the Linux players (pw-play, paplay) read WAV.
        var ext = OperatingSystem.IsWindows() ? "m4a" : "wav";
        var output = Path.Combine(Path.GetTempPath(), "Adawat", $"preview-{Guid.NewGuid():N}.{ext}");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string[] args = complex ? ["-filter_complex", filter, "-map", "[a]"] : ["-vn", "-map", "0:a:0", "-af", filter];
        string[] codec = OperatingSystem.IsWindows() ? ["-c:a", FFmpeg.Aac, "-b:a", "160k"] : ["-c:a", "pcm_s16le"];
        await MediaOps.Encode(file, output, [.. args, .. codec], new MediaInfo(), null);
        return output;
    }

    /// <summary>A small picture of the video at <paramref name="time"/>, for timelines and previews.</summary>
    public static async Task<byte[]?> Thumbnail(string file, double time, int height = 120)
    {
        try { return await FFmpeg.Capture(["-ss", FFmpeg.Seconds(time), "-i", file, "-frames:v", "1", "-vf", $"scale=-2:{height}", "-f", "image2pipe", "-vcodec", "png", "-"]); }
        catch (AdawatException) { return null; }
    }
}
