using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adawat.Core;

public enum OutputLocation { NextToOriginal, Downloads, Desktop }

public enum CompressPreset { Light, Balanced, Strong }

public enum VideoQuality { High, Balanced, Small }

public enum ModifierKey { Shift, Ctrl, Alt, None }

/// <summary>Everything the user can change, saved as JSON in %APPDATA%\Adawat\settings.json (~/.config/Adawat on Linux).</summary>
public sealed class Settings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IbraAdawat");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Current { get; } = Load();

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Alt;
    public OutputLocation OutputLocation { get; set; } = OutputLocation.NextToOriginal;
    public bool ShowResultsInFloat { get; set; }
    public bool PlaySounds { get; set; } = true;
    public double ImageQuality { get; set; } = 0.85;
    public int ResizeMaxPixels { get; set; } = 2048;
    public VideoQuality VideoQuality { get; set; } = VideoQuality.Balanced;
    public CompressPreset CompressPreset { get; set; } = CompressPreset.Balanced;
    public int CompressImageMaxPixels { get; set; }
    public int CompressVideoMaxHeight { get; set; }
    public bool HasSeenWelcome { get; set; }
    /// <summary>"ar" or "en". The first run follows Windows' display language.</summary>
    public string Language { get; set; } = "ar";

    // IBRA additions
    public bool UseDragWheel { get; set; }
    public string TranscribeSpoken { get; set; } = "auto";
    public string TranscribeTarget { get; set; } = "";
    public string TranscribeModel { get; set; } = "medium";
    public bool TranscribeBurn { get; set; }
    public string WatermarkLogo { get; set; } = "";
    public string WatermarkText { get; set; } = "";
    public string WatermarkPosition { get; set; } = "bottom-right";
    public double WatermarkScale { get; set; } = 0.18;
    public double WatermarkOpacity { get; set; } = 0.85;
    public string SocialSize { get; set; } = "story";
    public int StorySeconds { get; set; } = 60;
    public int UpscaleFactor { get; set; } = 2;
    public double SlideSeconds { get; set; } = 3;
    public string DownloadQuality { get; set; } = "best";

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (IOException) { }
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new();
    }
}
