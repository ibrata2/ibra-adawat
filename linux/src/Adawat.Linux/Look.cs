using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Adawat.Linux;

/// <summary>Colours and type shared by every window, matching the Mac and Windows apps: light surfaces, one violet accent.</summary>
public static class Look
{
    public static readonly Color AccentColor = Color.FromRgb(0x5B, 0x47, 0xE6);
    public static readonly IImmutableSolidColorBrush Accent = new ImmutableSolidColorBrush(AccentColor);
    public static readonly IImmutableSolidColorBrush AccentHover = new ImmutableSolidColorBrush(Color.FromRgb(0x52, 0x3F, 0xD8));
    public static readonly IImmutableSolidColorBrush AccentPressed = new ImmutableSolidColorBrush(Color.FromRgb(0x48, 0x36, 0xC4));
    public static readonly IImmutableSolidColorBrush AccentSoft = new ImmutableSolidColorBrush(Color.FromArgb(0x26, 0x5B, 0x47, 0xE6));
    public static readonly IImmutableSolidColorBrush Mint = new ImmutableSolidColorBrush(Color.FromRgb(0x7C, 0xF2, 0xC8));
    public static readonly IImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x28, 0x2B, 0x29));
    public static readonly IImmutableSolidColorBrush Muted = new ImmutableSolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));
    public static readonly IImmutableSolidColorBrush Surface = new ImmutableSolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB));
    public static readonly IImmutableSolidColorBrush Card = new ImmutableSolidColorBrush(Color.FromArgb(0x0B, 0, 0, 0));
    public static readonly IImmutableSolidColorBrush Hairline = new ImmutableSolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0));
    public static readonly IImmutableSolidColorBrush ButtonFill = new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0, 0, 0));
    public static readonly IImmutableSolidColorBrush ButtonHover = new ImmutableSolidColorBrush(Color.FromArgb(0x1C, 0, 0, 0));
    public static readonly IImmutableSolidColorBrush ButtonPressed = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0, 0, 0));
    public static readonly IImmutableSolidColorBrush PetalFill = new ImmutableSolidColorBrush(Color.FromArgb(0xF0, 0xF8, 0xF9, 0xFA));
    public static readonly IImmutableSolidColorBrush PetalStroke = new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0x5D, 0x66, 0x66));
    public static readonly IImmutableSolidColorBrush Disc = new ImmutableSolidColorBrush(Color.FromArgb(0xB8, 0xEE, 0xF0, 0xF2));
    public static readonly IImmutableSolidColorBrush Failure = new ImmutableSolidColorBrush(Color.FromRgb(0xD8, 0x3B, 0x3B));
    public static readonly IImmutableSolidColorBrush White = new ImmutableSolidColorBrush(Colors.White);
    public static readonly IImmutableSolidColorBrush Black = new ImmutableSolidColorBrush(Colors.Black);
    public static readonly IImmutableSolidColorBrush Clear = new ImmutableSolidColorBrush(Colors.Transparent);

    /// <summary>The bundled Noto Sans and Noto Sans Arabic, so the windows look the same on every distribution; then the
    /// desktop's own sans-serif.</summary>
    public static readonly FontFamily Font = new("avares://adawat/Assets/fonts#Noto Sans, avares://adawat/Assets/fonts#Noto Sans Arabic UI, sans-serif");
    public static readonly FontFamily Display = Font;
    public static readonly FontFamily Icons = new("avares://adawat/Assets/fonts#Material Icons Round");

    public static FlowDirection Direction => Core.L10n.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public static BoxShadows Shadow(double blur, double y, double opacity, Color? color = null)
    {
        var c = color ?? Colors.Black;
        return new BoxShadows(new BoxShadow { OffsetY = y, Blur = blur, Color = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B) });
    }
}
