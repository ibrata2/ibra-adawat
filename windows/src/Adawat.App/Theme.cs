using System.Windows;
using System.Windows.Media;

namespace Adawat.App;

/// <summary>
/// Colours shared by every window, taken from Windows itself: light or dark as set in Settings › Personalization › Colors,
/// and the accent colour chosen there. Read once when the app starts.
/// </summary>
public static class Theme
{
    public static readonly bool IsDark = ReadDark();
    public static readonly Color AccentColor = ReadAccent();

    private static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Mix(Color c, Color with, double amount) => Color.FromRgb(
        (byte)(c.R + (with.R - c.R) * amount), (byte)(c.G + (with.G - c.G) * amount), (byte)(c.B + (with.B - c.B) * amount));
    private static Brush B(Color c) => Freeze(new SolidColorBrush(c));
    private static Color Light(byte a) => Color.FromArgb(a, 0xFF, 0xFF, 0xFF);
    private static Color Dark(byte a) => Color.FromArgb(a, 0, 0, 0);

    public static readonly Brush Accent = B(AccentColor);
    public static readonly Brush AccentSoft = B(A(0x30, AccentColor));
    public static readonly Brush Mint = B(Mix(AccentColor, Colors.White, 0.35));
    public static readonly Brush Ink = B(IsDark ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x1B, 0x1B, 0x1B));
    public static readonly Brush Muted = B(IsDark ? Color.FromRgb(0xA6, 0xA6, 0xA6) : Color.FromRgb(0x6B, 0x6B, 0x6B));
    public static readonly Brush Surface = B(IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF9, 0xF9, 0xF9));
    public static readonly Brush Card = B(IsDark ? Light(0x0F) : Dark(0x08));
    public static readonly Brush Hairline = B(IsDark ? Light(0x1F) : Dark(0x16));
    public static readonly Brush ButtonFill = B(IsDark ? Light(0x15) : Dark(0x0C));
    public static readonly Brush ButtonPressed = B(IsDark ? Light(0x0B) : Dark(0x16));
    public static readonly Brush ButtonHover = B(IsDark ? Light(0x22) : Dark(0x12));
    public static readonly Brush AccentHover = B(Mix(AccentColor, IsDark ? Colors.White : Colors.Black, 0.12));
    public static readonly Brush AccentPressed = B(Mix(AccentColor, IsDark ? Colors.Black : Colors.White, 0.2));
    public static readonly Brush Selected = B(IsDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Colors.White);
    public static readonly Brush PetalFill = B(IsDark ? Color.FromArgb(0xF2, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xFB, 0xFB, 0xFB));
    public static readonly Brush PetalStroke = B(IsDark ? Light(0x26) : Dark(0x1E));
    public static readonly Brush PetalHoverStroke = B(A(0xA0, AccentColor));
    public static readonly Brush Disc = B(IsDark ? Color.FromArgb(0xC8, 0x1A, 0x1A, 0x1A) : Color.FromArgb(0xB8, 0xEE, 0xF0, 0xF2));
    public static readonly Brush CenterFill = B(IsDark ? Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    private static bool ReadDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The Windows accent colour (stored as 0xAABBGGRR); Windows blue when it cannot be read.</summary>
    private static Color ReadAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int value)
            {
                var v = unchecked((uint)value);
                var color = Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
                if (color.R + color.G + color.B > 60) return color;
            }
        }
        catch (Exception) { }
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

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
