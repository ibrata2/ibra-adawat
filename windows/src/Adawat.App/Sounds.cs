using Adawat.Core;

namespace Adawat.App;

/// <summary>The app's three sounds, synthesised once in memory: a soft tick on the wheel, a light chime when a job is done,
/// and a low tone when it fails. They follow the "Sound and haptic feedback" setting.</summary>
public static class Sounds
{
    private static readonly byte[] TickSound = Wave(0.035, t => Math.Min(1, t / 0.001) * Math.Exp(-t / 0.0055) * (Math.Sin(2 * Math.PI * 1900 * t) + 0.35 * Math.Sin(2 * Math.PI * 3800 * t)) * 0.5);
    private static readonly byte[] DoneSound = Wave(0.32, t =>
    {
        double Note(double f, double at) => t < at ? 0 : Math.Exp(-(t - at) / 0.09) * Math.Min(1, (t - at) / 0.004) * Math.Sin(2 * Math.PI * f * (t - at));
        return (Note(1046.5, 0) + Note(1568, 0.07)) * 0.32;
    });
    private static readonly byte[] FailSound = Wave(0.28, t => Math.Min(1, t / 0.005) * Math.Exp(-t / 0.08) * Math.Sin(2 * Math.PI * 220 * t) * 0.45);

    public static void Tick() => Play(TickSound);
    public static void Done() => Play(DoneSound);
    public static void Failed() => Play(FailSound);

    private static void Play(byte[] sound)
    {
        if (!Settings.Current.PlaySounds) return;
        Native.PlaySound(sound, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
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
