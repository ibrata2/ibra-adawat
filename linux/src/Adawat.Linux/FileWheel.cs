using Adawat.Linux.Desktop;
using Adawat.Linux.Wheel;

namespace Adawat.Linux;

/// <summary>The wheel for files chosen with "Open with Adawat" in a file manager, where there is no drag to follow.</summary>
public static class FileWheel
{
    private static WheelWindow? _wheel;

    public static void Present(IReadOnlyList<string> files)
    {
        var existing = files.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        if (existing.Count == 0) return;
        _wheel ??= new WheelWindow(clicking: true);
        var at = LastPointer.Position;
        if (at == default && _wheel.Screens.Primary is { } screen) at = screen.WorkingArea.Center;
        _wheel.ShowForFiles(existing, at);
    }
}
