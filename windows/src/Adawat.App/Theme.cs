using System.Windows;
using System.Windows.Media;

namespace Adawat.App;

/// <summary>Colours and type shared by every window, IBRA أدوات: dark surfaces, one red accent.</summary>
public static class Theme
{
    public static readonly Color AccentColor = Color.FromRgb(0xE3, 0x19, 0x2B);
    public static readonly Brush Accent = Freeze(new SolidColorBrush(AccentColor));
    public static readonly Brush AccentSoft = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xE3, 0x19, 0x2B)));
    public static readonly Brush Mint = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F)));
    public static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF4)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0xA4)));
    public static readonly Brush Surface = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)));
    public static readonly Brush Card = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Hairline = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush ButtonFill = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush ButtonPressed = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush PetalFill = Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1B, 0x1F)));
    public static readonly Brush PetalStroke = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xE3, 0x19, 0x2B)));
    public static readonly Brush Disc = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0x0C, 0x0C, 0x0E)));

    public static readonly Brush ButtonHover = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush AccentHover = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x2C, 0x3D)));
    public static readonly Brush AccentPressed = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0x10, 0x20)));
    public static readonly Brush Selected = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x34)));
    public static readonly Brush CenterFill = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x16, 0x16, 0x19)));

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI, Tahoma");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI, Tahoma");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static FlowDirection Direction => Core.L10n.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
