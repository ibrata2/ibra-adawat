using Avalonia.Controls;
using Avalonia.Media;
using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>What the wheel can do on this desktop, said once in Settings and the welcome guide.</summary>
public static class DesktopSupport
{
    public static string? Message => ShellExtension.Current switch
    {
        ShellExtension.State.NeedsSignIn => "Sign out and back in once so GNOME loads the Adawat extension. Until then, right-click files and choose Open with Adawat.",
        ShellExtension.State.Missing => "The Adawat extension for GNOME is missing. Right-click files and choose Open with Adawat to open the wheel.",
        ShellExtension.State.NotNeeded when Core.Desktop.IsWayland => "On this desktop the wheel opens from the file manager: right-click files and choose Open with Adawat.",
        _ => null,
    };

    public static Control? Note() => Message is { } text
        ? new Border
        {
            Background = Look.AccentSoft, CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(10, 8), Margin = new Avalonia.Thickness(0, 6, 0, 0),
            Child = UI.Text(text, 11.5, color: Look.Ink, wrap: true),
        }
        : null;
}
