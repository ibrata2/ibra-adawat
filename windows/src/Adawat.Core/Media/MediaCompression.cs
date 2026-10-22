using Adawat.Core.Images;
using Adawat.Core.Media;

namespace Adawat.Core;

public static partial class Compression
{
    public static async Task<IReadOnlyList<string>> Videos(IReadOnlyList<string> files, CompressSettings settings, OperationContext context)
    {
        var outputs = new List<string>();
        long before = 0, after = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            context.Report($"Compressing {index + 1} of {files.Count}");
            var info = await MediaProbe.Info(file);
            if (!info.HasVideo || info.Duration <= 0) throw new AdawatException($"{Path.GetFileName(file)} has no video track");
            double audioBits = info.HasAudio ? 128_000 : 0;
            var videoBits = VideoBitrate(info, settings, audioBits);
            var output = Output.PathFor(file, "mp4", "compressed");
            long? limit = settings.Target?.For(info.FileSize);
            // Bitrate targets overshoot short clips; with a size target, retry at a proportionally lower rate.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var args = new List<string> { "-map", "0:v:0", "-map", "0:a?", "-map_metadata", "0" };
                args.AddRange(FFmpeg.AtBitrate((long)videoBits, info.IsHighDepth));
                if (settings.VideoMaxHeight is { } height && info.Height > height) args.AddRange(["-vf", $"scale=-2:{height}"]);
                if (info.HasAudio) args.AddRange(["-c:a", FFmpeg.Aac, "-b:a", "128k"]);
                args.AddRange(["-movflags", "+faststart"]);
                if (attempt > 0) context.Report($"Fitting {index + 1} of {files.Count} to the target size");
                await MediaOps.Encode(file, output, args, info, context.ProgressFor(index, files.Count), context: context);
                var size = ImageEngine.Size(output);
                if (limit is not { } max || size <= max || videoBits <= 100_000) break;
                videoBits = Math.Max(100_000, videoBits / ((double)size / max) * 0.95);
                if (attempt == 2)
                {
                    MediaOps.TryDelete(output);
                    throw new AdawatException($"Could not fit {Path.GetFileName(file)} within {Format.Bytes(max)}. Choose a larger size.");
                }
            }
            before += info.FileSize;
            after += ImageEngine.Size(output);
            outputs.Add(output);
        }
        context.Finish(Savings("video", files.Count, before, after));
        return outputs;
    }

    private static double VideoBitrate(MediaInfo info, CompressSettings settings, double audioBits)
    {
        if (settings.Target is { } target) return Math.Max(150_000, target.For(info.FileSize) * 8 / info.Duration * 0.97 - audioBits);
        var bits = info.Bitrate * settings.Preset.MediaShare() - audioBits;
        // A smaller frame needs proportionally fewer bits for the same look.
        if (settings.VideoMaxHeight is { } height && info.Height > height) bits *= (double)height * height / ((double)info.Height * info.Height);
        return Math.Max(300_000, bits);
    }

    public static async Task<IReadOnlyList<string>> Audio(IReadOnlyList<string> files, CompressSettings settings, OperationContext context)
    {
        var outputs = new List<string>();
        long before = 0, after = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            context.Report($"Compressing {index + 1} of {files.Count}");
            var info = await MediaProbe.Info(file);
            if (!info.HasAudio || info.Duration <= 0) throw new AdawatException($"{Path.GetFileName(file)} has no audio track");
            var bitrate = (long)AudioBitrate(info, settings);
            var (codec, ext) = FileKinds.Ext(file) switch
            {
                "mp3" => ("libmp3lame", "mp3"),
                "opus" => ("libopus", "opus"),
                "ogg" => ("libvorbis", "ogg"),
                _ => (FFmpeg.Aac, "m4a"),
            };
            var output = Output.PathFor(file, ext, "compressed");
            await MediaOps.Encode(file, output, ["-vn", "-map", "0:a:0", "-map_metadata", "0", "-c:a", codec, "-b:a", bitrate.ToString()],
                                  info, context.ProgressFor(index, files.Count), context: context);
            before += info.FileSize;
            after += ImageEngine.Size(output);
            outputs.Add(output);
        }
        context.Finish(Savings("file", files.Count, before, after));
        return outputs;
    }

    private static double AudioBitrate(MediaInfo info, CompressSettings settings)
    {
        if (settings.Target is { } target) return Math.Min(320_000, Math.Max(32_000, target.For(info.FileSize) * 8 / info.Duration * 0.97));
        return Math.Min(320_000, Math.Max(64_000, info.Bitrate * settings.Preset.MediaShare()));
    }

    /// <summary>Expected total size after compressing, or null when it cannot be predicted.</summary>
    public static async Task<long?> Estimate(IReadOnlyList<string> files, FileKind kind, CompressSettings settings)
    {
        switch (kind)
        {
            case FileKind.Image:
            {
                if (settings.Target is { } target) return files.Sum(file => Math.Min(ImageEngine.Size(file), target.For(ImageEngine.Size(file))));
                // Encode a few files for real and scale up; large selections stay responsive.
                var sample = files.Take(4).ToList();
                long encoded = 0;
                foreach (var file in sample)
                {
                    try
                    {
                        encoded += await Task.Run(() =>
                        {
                            using var image = ImageEngine.Load(file, settings.ImageMaxPixels);
                            var bytes = ImageEngine.Encode(image, ImageOps.CompressedFormat(file, image), settings.Preset.ImageQuality());
                            return Math.Min(bytes.LongLength, ImageEngine.Size(file));
                        });
                    }
                    catch (ImageMagick.MagickException) { return null; }
                }
                return encoded * files.Count / Math.Max(sample.Count, 1);
            }
            case FileKind.Video or FileKind.Audio:
            {
                long total = 0;
                foreach (var file in files)
                {
                    var info = await MediaProbe.Info(file);
                    if (info.Duration <= 0) return null;
                    if (settings.Target is { } target) total += Math.Min(info.FileSize, target.For(info.FileSize));
                    else if (kind == FileKind.Video)
                    {
                        double audioBits = info.HasAudio ? 128_000 : 0;
                        total += Math.Min(info.FileSize, (long)((VideoBitrate(info, settings, audioBits) + audioBits) * info.Duration / 8));
                    }
                    else total += Math.Min(info.FileSize, (long)(AudioBitrate(info, settings) * info.Duration / 8));
                }
                return total;
            }
            default:
                return null;
        }
    }
}
