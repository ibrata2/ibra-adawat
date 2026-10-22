using Avalonia;
using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>Tells the app that a key was pressed while files were being dragged, and where the pointer was, in screen
/// pixels. X11 sessions are watched directly; GNOME on Wayland lets no app see the pointer outside its windows, so a
/// small GNOME Shell extension reports drags over a socket instead.</summary>
public interface IDragWatch : IDisposable
{
    event Action<ModifierKey, PixelPoint>? KeyDuringDrag;
    event Action? DragEnded;
    /// <summary>True while the left button is still held, as far as the watcher knows.</summary>
    bool ButtonDown { get; }
    void Start();
}

public static class DragWatch
{
    /// <summary>The watcher this session can use, or null when the wheel cannot open on drags here.</summary>
    public static IDragWatch? ForSession()
    {
        if (!Core.Desktop.IsWayland) return new X11DragWatch();
        if (Core.Desktop.IsGnome) return new ShellDragWatch();
        return null;
    }
}

/// <summary>The last pointer position any watcher saw, for placing toasts and Floats on the right screen.</summary>
public static class LastPointer
{
    public static PixelPoint Position { get; set; }
}
