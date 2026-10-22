using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Adawat.Linux;

/// <summary>The wheel icon, from the app's assets.</summary>
public static class AppIcon
{
    private static WindowIcon? _window;

    public static WindowIcon Window => _window ??= new WindowIcon(AssetLoader.Open(new Uri("avares://adawat/Assets/icons/adawat-256.png")));

    public static Bitmap Bitmap(int size = 256) => new(AssetLoader.Open(new Uri($"avares://adawat/Assets/icons/adawat-{size}.png")));
}
