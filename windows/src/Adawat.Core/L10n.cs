using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adawat.Core;

/// <summary>
/// Translations for every visible string, shared with the Mac app. Keys are the English text. Exact matches come
/// from strings.ar.json; text with values in it ("Converting 2 of 5") from patterns.ar.json, where {1}, {2}… mark
/// the values. Captured values are translated too when they are known words.
/// </summary>
public static class L10n
{
    private static readonly Dictionary<string, string> Strings = Load("strings.ar.json");
    private static readonly List<(Regex Pattern, string Template)> Patterns = Load("patterns.ar.json")
        .Select(pair => (new Regex("^" + string.Join("(.+?)", Regex.Split(pair.Key, @"\{\d\}").Select(Regex.Escape)) + "$",
                                   RegexOptions.Singleline | RegexOptions.CultureInvariant), pair.Value))
        .OrderByDescending(pair => pair.Item1.ToString().Length)
        .ToList();

    /// <summary>The app's language, chosen in Settings and read once at start.</summary>
    public static bool IsArabic { get; private set; } = Settings.Current.Language == "ar";

    public static bool IsRightToLeft => IsArabic;

    /// <summary>For tests and the command line: show this run in another language.</summary>
    public static void Override(string language) => IsArabic = language == "ar";

    public static string T(string english)
    {
        if (!IsArabic || string.IsNullOrEmpty(english)) return english;
        if (Strings.TryGetValue(english, out var exact)) return exact;
        foreach (var suffix in new[] { "…", ":" })
        {
            if (english.EndsWith(suffix) && Strings.TryGetValue(english[..^1], out var bare)) return bare + suffix;
        }
        foreach (var (pattern, template) in Patterns)
        {
            var match = pattern.Match(english);
            if (!match.Success) continue;
            var result = template;
            for (var group = match.Groups.Count - 1; group >= 1; group--)
            {
                result = result.Replace("{" + group + "}", T(match.Groups[group].Value));
            }
            return result;
        }
        return english;
    }

    /// <summary>Upper-cased for wheel labels in scripts that have capitals.</summary>
    public static string Upper(string text) => IsArabic ? text : text.ToUpper(CultureInfo.CurrentCulture);

    private static Dictionary<string, string> Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }
}
