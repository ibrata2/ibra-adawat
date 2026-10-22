using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Adawat.Core.Media;

namespace Adawat.Core.Ibra;

public sealed record SpeechLanguage(string Code, string Name);

public sealed record TranscribeOptions
{
    /// <summary>Language spoken in the file, or "auto".</summary>
    public string Spoken { get; init; } = "auto";
    /// <summary>Language of the written result; empty means the spoken language.</summary>
    public string Target { get; init; } = "";
    public string Model { get; init; } = "medium";
    public bool WriteText { get; init; } = true;
    public bool WriteSubtitles { get; init; } = true;
    /// <summary>Also writes a copy of the video with the subtitles drawn on it.</summary>
    public bool Burn { get; init; }

    public static TranscribeOptions Saved => new()
    {
        Spoken = Settings.Current.TranscribeSpoken,
        Target = Settings.Current.TranscribeTarget,
        Model = Settings.Current.TranscribeModel,
        Burn = Settings.Current.TranscribeBurn,
    };
}

public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// Speech to text on this computer with whisper.cpp (whisper-cli.exe in tools\). Arabic comes out in Modern Standard Arabic.
/// Translation to English is done by Whisper itself, offline; to any other language the written text is sent to a free
/// online translator (MyMemory), one line at a time.
/// </summary>
public static class Transcription
{
    public static readonly IReadOnlyList<SpeechLanguage> Languages =
    [
        new("auto", "Detect automatically"), new("ar", "Arabic"), new("en", "English"), new("fr", "French"), new("de", "German"),
        new("es", "Spanish"), new("it", "Italian"), new("pt", "Portuguese"), new("tr", "Turkish"), new("fa", "Persian"),
        new("ru", "Russian"), new("hi", "Hindi"), new("ur", "Urdu"), new("zh", "Chinese"), new("ja", "Japanese"), new("ko", "Korean"),
    ];

    public static IReadOnlyList<SpeechLanguage> Targets => [new("", "Same as the speech"), .. Languages.Skip(1)];

    public static readonly IReadOnlyList<(string Id, string Title)> Models =
    [
        ("medium", "Medium · 1.5 GB · more accurate"),
        ("small", "Small · 466 MB · faster"),
    ];

    public static string ModelPath(string id) => Path.Combine(IbraTools.Models, $"ggml-{id}.bin");

    public static bool HasModel(string id) => File.Exists(ModelPath(id));

    public static bool CanRun => ProcessRunner.Find("whisper-cli") != null;

    public static Task<IReadOnlyList<string>> Quick(IReadOnlyList<string> files, OperationContext context) => Run(files, TranscribeOptions.Saved, context);

    public static async Task<IReadOnlyList<string>> Run(IReadOnlyList<string> files, TranscribeOptions options, OperationContext context)
    {
        var whisper = ProcessRunner.Find("whisper-cli") ?? throw new AdawatException("The speech engine is missing. Reinstall IBRA Adawat.");
        var model = ModelPath(options.Model);
        if (!File.Exists(model))
        {
            context.Report("Downloading the speech model (one time only)");
            await IbraTools.Fetch($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{options.Model}.bin", model,
                                  fraction => context.Progress(fraction), context.Cancellation);
        }

        var outputs = new List<string>();
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var step = context.ProgressFor(index, files.Count);
            var info = await MediaProbe.Info(file);
            if (!info.HasAudio) throw new AdawatException("This file has no audio track");
            using var temp = Output.Temp();

            context.Report($"Preparing audio {index + 1} of {files.Count}");
            var wav = temp.File("speech.wav");
            await FFmpeg.Run(file, wav, ["-vn", "-map", "0:a:0", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le"], info.Duration,
                             f => step(f * 0.08), null, context.Cancellation);

            context.Report($"Writing down the speech {index + 1} of {files.Count}");
            var whisperTranslates = options.Target == "en" && options.Spoken != "en";
            var threads = Math.Clamp(Environment.ProcessorCount - 1, 2, 16);
            var basePath = temp.File("speech");
            var args = new List<string>
            {
                "-m", model, "-f", wav, "-l", options.Spoken, "-t", threads.ToString(CultureInfo.InvariantCulture),
                "-osrt", "-of", basePath, "-pp", "-ml", "42", "-sow",
            };
            if (whisperTranslates) args.Add("-tr");
            if (options.Spoken == "ar" && !whisperTranslates)
            {
                // A short example in Modern Standard Arabic with punctuation steers the writing style.
                args.AddRange(["--prompt", "مرحبًا بكم. هذا نصٌّ مكتوب باللغة العربية الفصحى، مع علامات الترقيم."]);
            }
            string? detected = null;
            var progress = new Regex(@"progress\s*=\s*(\d+)%");
            var language = new Regex(@"auto-detected language:\s*([a-z]{2,3})");
            var result = await IbraTools.Run(whisper, args, line =>
            {
                var p = progress.Match(line);
                if (p.Success) step(0.08 + double.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture) / 100 * 0.8);
                var l = language.Match(line);
                if (l.Success) detected = l.Groups[1].Value;
            }, context.Cancellation);
            var srtPath = basePath + ".srt";
            if (result.ExitCode != 0 || !File.Exists(srtPath))
                throw new AdawatException("Speech to text failed: " + IbraTools.LastLine(result));

            var cues = ParseSrt(await File.ReadAllTextAsync(srtPath, Encoding.UTF8, context.Cancellation));
            if (cues.Count == 0) throw new AdawatException("No speech was found in this file");

            var source = whisperTranslates ? "en" : options.Spoken == "auto" ? detected ?? "auto" : options.Spoken;
            var target = string.IsNullOrEmpty(options.Target) ? source : options.Target;
            if (target != source && target != "auto")
            {
                context.Report("Translating");
                cues = await Translate(cues, source, target, f => step(0.88 + f * 0.1), context.Cancellation);
            }

            var suffix = string.IsNullOrEmpty(options.Target) || target == source && !whisperTranslates ? null : target;
            if (options.WriteSubtitles || options.Burn)
            {
                var srt = Output.PathFor(file, "srt", suffix);
                await File.WriteAllTextAsync(srt, WriteSrt(cues), new UTF8Encoding(true), context.Cancellation);
                if (options.WriteSubtitles) outputs.Add(srt);
                if (options.Burn && info.HasVideo)
                {
                    context.Report("Drawing the subtitles on the video");
                    outputs.Add(await Subtitles.Burn(file, srt, info, f => step(0.98 + f * 0.02), context.Cancellation));
                    if (!options.WriteSubtitles) IbraTools.TryDelete(srt);
                }
            }
            if (options.WriteText)
            {
                var txt = Output.PathFor(file, "txt", suffix);
                await File.WriteAllTextAsync(txt, WriteText(cues), new UTF8Encoding(true), context.Cancellation);
                outputs.Add(txt);
            }
            step(1);
        }
        return outputs;
    }

    // MARK: SRT

    public static List<Cue> ParseSrt(string text)
    {
        var cues = new List<Cue>();
        var time = new Regex(@"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)");
        foreach (var block in Regex.Split(text.Replace("\r", ""), @"\n\s*\n"))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            var at = lines.FindIndex(l => time.IsMatch(l));
            if (at < 0) continue;
            var m = time.Match(lines[at]);
            TimeSpan T(int g) => new(0, int.Parse(m.Groups[g].Value), int.Parse(m.Groups[g + 1].Value), int.Parse(m.Groups[g + 2].Value),
                                     int.Parse(m.Groups[g + 3].Value.PadRight(3, '0')[..3]));
            var words = string.Join(" ", lines.Skip(at + 1)).Trim();
            if (words.Length == 0 || words.StartsWith('[') && words.EndsWith(']')) continue;
            cues.Add(new Cue(T(1), T(5), words));
        }
        return cues;
    }

    public static string WriteSrt(IReadOnlyList<Cue> cues)
    {
        var text = new StringBuilder();
        static string F(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
        for (var i = 0; i < cues.Count; i++)
        {
            text.Append(i + 1).Append("\r\n").Append(F(cues[i].Start)).Append(" --> ").Append(F(cues[i].End)).Append("\r\n")
                .Append(cues[i].Text).Append("\r\n\r\n");
        }
        return text.ToString();
    }

    private static string WriteText(IReadOnlyList<Cue> cues)
    {
        // Lines join into paragraphs; a pause of two seconds or more starts a new one.
        var text = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            if (i > 0) text.Append((cues[i].Start - cues[i - 1].End).TotalSeconds >= 2 ? "\r\n\r\n" : " ");
            text.Append(cues[i].Text);
        }
        return text.Append("\r\n").ToString();
    }

    // MARK: Translation

    private static async Task<List<Cue>> Translate(List<Cue> cues, string source, string target, Action<double> progress, CancellationToken cancellation)
    {
        var results = new string[cues.Count];
        var done = 0;
        using var gate = new SemaphoreSlim(4);
        var tasks = cues.Select(async (cue, i) =>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                results[i] = await TranslateLine(cue.Text, source, target, cancellation);
                progress((double)Interlocked.Increment(ref done) / cues.Count);
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        return cues.Select((cue, i) => cue with { Text = results[i] }).ToList();
    }

    public static async Task<string> TranslateLine(string text, string source, string target, CancellationToken cancellation)
    {
        var from = source == "auto" ? "autodetect" : source;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={from}|{target}";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await IbraTools.Client.GetAsync(url, cancellation);
                var body = await response.Content.ReadAsStringAsync(cancellation);
                using var json = JsonDocument.Parse(body);
                var status = json.RootElement.TryGetProperty("responseStatus", out var s) ? s.ToString() : "";
                var translated = json.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
                if (status == "429" || translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                    throw new AdawatException("The free translation limit for today is reached. Try again tomorrow, or translate to English (works offline).");
                if (status != "200" || translated.Length == 0) throw new HttpRequestException(translated);
                return System.Net.WebUtility.HtmlDecode(translated);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException && attempt < 2)
            {
                await Task.Delay(1500 * (attempt + 1), cancellation);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new AdawatException("Translation needs the internet. Check the connection and try again.");
            }
        }
    }
}

/// <summary>Draws subtitles onto a copy of a video (libass inside ffmpeg shapes Arabic correctly).</summary>
public static class Subtitles
{
    public static async Task<string> Burn(string video, string srt, MediaInfo info, Action<double>? progress, CancellationToken cancellation)
    {
        using var temp = Output.Temp();
        var copy = temp.File("subs.srt");
        File.Copy(srt, copy, true);
        // Subtitle sizes are in units of 1/288 of the video height: text about 5.5% of the short side, lifted above the Reels buttons when vertical.
        var portrait = info.Height > info.Width;
        var size = info.Height > 0 ? Math.Max(7, (int)Math.Round(15.8 * Math.Min(info.Width, info.Height) / info.Height)) : 16;
        var style = $"FontName=Arial,FontSize={size},Bold=1,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BackColour=&H80000000," +
                    $"BorderStyle=1,Outline=1.5,Shadow=1,Alignment=2,MarginV={(portrait ? 60 : 20)}";
        var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        var filter = $"subtitles={IbraTools.FilterPath(copy)}:fontsdir={IbraTools.FilterPath(fonts)}:force_style='{style}'";
        var output = Output.PathFor(video, "mp4", "subtitled");
        var args = new List<string> { "-map", "0:v:0", "-map", "0:a?", "-vf", filter };
        args.AddRange(FFmpeg.H264);
        args.AddRange(["-c:a", "copy", "-movflags", "+faststart"]);
        try
        {
            await FFmpeg.Run(video, output, args, info.Duration, progress, null, cancellation);
        }
        catch
        {
            IbraTools.TryDelete(output);
            throw;
        }
        return output;
    }

    /// <summary>A video and an .srt dropped together: draw the subtitles on the video.</summary>
    public static async Task<IReadOnlyList<string>> BurnPair(IReadOnlyList<string> files, OperationContext context)
    {
        var video = files.FirstOrDefault(f => FileKinds.Of(f) == FileKind.Video) ?? throw new AdawatException("Drop a video together with its .srt file");
        var srt = files.FirstOrDefault(f => FileKinds.Ext(f) == "srt") ?? throw new AdawatException("Drop a video together with its .srt file");
        context.Report("Drawing the subtitles on the video");
        var info = await MediaProbe.Info(video);
        return [await Burn(video, srt, info, context.Progress, context.Cancellation)];
    }
}
