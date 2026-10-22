using System.Diagnostics;
using Adawat.Core;

namespace Adawat.Linux;

/// <summary>The app's three sounds, synthesised once: a soft tick on the wheel, a light chime when a job is done, and a low
/// tone when it fails. They follow the "Sound and haptic feedback" setting and play through PipeWire or PulseAudio.</summary>
public static class Sounds
{
    private static readonly Lazy<string> TickSound = new(() => Save("tick", Wave(0.035, t => Math.Min(1, t / 0.001) * Math.Exp(-t / 0.0055) * (Math.Sin(2 * Math.PI * 1900 * t) + 0.35 * Math.Sin(2 * Math.PI * 3800 * t)) * 0.5)));
    private static readonly Lazy<string> DoneSound = new(() => Save("done", Wave(0.32, t =>
    {
        double Note(double f, double at) => t < at ? 0 : Math.Exp(-(t - at) / 0.09) * Math.Min(1, (t - at) / 0.004) * Math.Sin(2 * Math.PI * f * (t - at));
        return (Note(1046.5, 0) + Note(1568, 0.07)) * 0.32;
    })));
    private static readonly Lazy<string> FailSound = new(() => Save("fail", Wave(0.28, t => Math.Min(1, t / 0.005) * Math.Exp(-t / 0.08) * Math.Sin(2 * Math.PI * 220 * t) * 0.45)));
    private static readonly Lazy<string?> Player = new(() => ProcessRunner.Find("pw-play") ?? ProcessRunner.Find("paplay") ?? ProcessRunner.Find("aplay"));

    public static void Tick() => Play(TickSound);
    public static void Done() => Play(DoneSound);
    public static void Failed() => Play(FailSound);

    private static void Play(Lazy<string> sound)
    {
        if (!Settings.Current.PlaySounds || Player.Value is not { } player) return;
        try
        {
            var info = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            if (Path.GetFileName(player) == "aplay") info.ArgumentList.Add("-q");
            info.ArgumentList.Add(sound.Value);
            Process.Start(info)?.Dispose();
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { }
    }

    private static string Save(string name, byte[] wave)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Adawat", "sounds");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{name}.wav");
        File.WriteAllBytes(path, wave);
        return path;
    }

    /// <summary>16-bit mono WAV at 44.1 kHz from a sample function of time.</summary>
    private static byte[] Wave(double seconds, Func<double, double> sample)
    {
        const int rate = 44100;
        var count = (int)(rate * seconds);
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + count * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);
        for (var i = 0; i < count; i++) writer.Write((short)(Math.Clamp(sample((double)i / rate), -1, 1) * short.MaxValue));
        return memory.ToArray();
    }
}
