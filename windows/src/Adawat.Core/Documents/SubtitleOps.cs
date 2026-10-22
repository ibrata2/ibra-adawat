using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Adawat.Core.Documents;

/// <summary>SRT and WebVTT subtitles, either way, or as plain text with the timings removed.</summary>
public static partial class SubtitleOps
{
    public static readonly IReadOnlyList<string> Targets = ["srt", "vtt", "txt"];

    public static string Title(string target) => target.ToUpperInvariant();

    private sealed record Cue(double Start, double End, string Text);

    [GeneratedRegex(@"(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})\s*-->\s*(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})")]
    private static partial Regex TimingLine();

    public static async Task<IReadOnlyList<string>> Convert(IReadOnlyList<string> files, string target, OperationContext context)
    {
        var outputs = new List<string>();
        foreach (var file in files)
        {
            var cues = Parse(DocumentOps.ReadText(file));
            if (cues.Count == 0) throw new AdawatException($"No valid subtitle cues were found in {Path.GetFileName(file)}.");
            var output = Output.PathFor(file, target);
            await File.WriteAllTextAsync(output, target switch
            {
                "srt" => Srt(cues),
                "vtt" => Vtt(cues),
                _ => string.Join("\n", cues.Select(c => c.Text)) + "\n",
            }, new UTF8Encoding(false));
            outputs.Add(output);
        }
        return outputs;
    }

    private static List<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        foreach (var block in Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n\s*\n"))
        {
            var lines = block.Split('\n').ToList();
            var timing = lines.FindIndex(line => TimingLine().IsMatch(line));
            if (timing < 0) continue;
            var match = TimingLine().Match(lines[timing]);
            double Time(int group) =>
                (match.Groups[group].Success ? int.Parse(match.Groups[group].Value) * 3600 : 0) + int.Parse(match.Groups[group + 1].Value) * 60
                + int.Parse(match.Groups[group + 2].Value) + int.Parse(match.Groups[group + 3].Value.PadRight(3, '0')) / 1000.0;
            var body = string.Join("\n", lines.Skip(timing + 1)).Trim();
            if (body.Length > 0) cues.Add(new Cue(Time(1), Time(5), body));
        }
        return cues;
    }

    private static string Stamp(double seconds, char separator)
    {
        var ms = (long)Math.Round(seconds * 1000);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}{3}{4:000}", ms / 3_600_000, ms / 60_000 % 60, ms / 1000 % 60, separator, ms % 1000);
    }

    private static string Srt(List<Cue> cues) =>
        string.Concat(cues.Select((cue, i) => $"{i + 1}\n{Stamp(cue.Start, ',')} --> {Stamp(cue.End, ',')}\n{Regex.Replace(cue.Text, "<[^>]+>", "")}\n\n"));

    private static string Vtt(List<Cue> cues) =>
        "WEBVTT\n\n" + string.Concat(cues.Select(cue => $"{Stamp(cue.Start, '.')} --> {Stamp(cue.End, '.')}\n{cue.Text}\n\n"));
}
