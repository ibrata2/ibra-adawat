using System.Windows;
using System.Windows.Media;

namespace Adawat.App;

/// <summary>
/// Colours shared by every window, in one of three styles chosen in Settings (read once at start; changing it restarts the app):
/// glass (the default: Windows 11 acrylic behind translucent surfaces, IBMC red and blue), light and dark (plain Windows colours
/// with the Windows accent).
/// </summary>
public static class Theme
{
    public static readonly string Style = Core.Settings.Current.Style is "light" or "dark" ? Core.Settings.Current.Style : "glass";
    /// <summary>Acrylic needs Windows 11 22H2 or later; on older Windows the glass style keeps its colours over solid surfaces.</summary>
    public static readonly bool CanBlur = Environment.OSVersion.Version.Build >= 22621;
    public static readonly bool IsGlass = Style == "glass";
    public static readonly bool UsesBackdrop = IsGlass && CanBlur;
    public static readonly bool IsDark = Style == "dark" || (IsGlass && ReadDark());

    /// <summary>IBMC red and blue, from the app icon.</summary>
    public static readonly Color BrandRed = Color.FromRgb(0xE3, 0x19, 0x2B);
    public static readonly Color BrandBlue = Color.FromRgb(0x1E, 0x7B, 0xFF);
    public static readonly Color AccentColor = IsGlass ? BrandRed : ReadAccent();

    private static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Color Mix(Color c, Color with, double amount) => Color.FromRgb(
        (byte)(c.R + (with.R - c.R) * amount), (byte)(c.G + (with.G - c.G) * amount), (byte)(c.B + (with.B - c.B) * amount));
    private static Brush B(Color c) => Freeze(new SolidColorBrush(c));
    private static Color Light(byte a) => Color.FromArgb(a, 0xFF, 0xFF, 0xFF);
    private static Color Dark(byte a) => Color.FromArgb(a, 0, 0, 0);
    private static Brush Gradient(Color from, Color to) => Freeze(new LinearGradientBrush(from, to, 0));

    public static readonly Brush Accent = IsGlass ? Gradient(BrandRed, BrandBlue) : B(AccentColor);
    public static readonly Brush AccentSoft = IsGlass ? Gradient(A(0x40, BrandRed), A(0x40, BrandBlue)) : B(A(0x30, AccentColor));
    public static readonly Brush Mint = B(Mix(AccentColor, Colors.White, 0.35));
    public static readonly Brush Ink = B(IsDark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x14, 0x14, 0x18));
    public static readonly Brush Muted = B(IsDark ? Color.FromRgb(0xAE, 0xAE, 0xB6) : Color.FromRgb(0x5E, 0x5E, 0x66));

    /// <summary>Window background. In glass it is a thin tint over the acrylic (or a near-solid tint where there is no acrylic).</summary>
    public static readonly Brush Surface = !IsGlass
        ? B(IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF9, 0xF9, 0xF9))
        : UsesBackdrop
            ? B(IsDark ? Color.FromArgb(0x50, 0x0C, 0x0C, 0x14) : Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))
            : Freeze(new LinearGradientBrush(IsDark ? Color.FromRgb(0x1A, 0x14, 0x1C) : Color.FromRgb(0xFB, 0xF1, 0xF2),
                                             IsDark ? Color.FromRgb(0x10, 0x16, 0x24) : Color.FromRgb(0xEE, 0xF4, 0xFF), 45));
    /// <summary>For pop-ups that cannot have acrylic (layered windows): an opaque version of the surface.</summary>
    public static readonly Brush SolidSurface = !IsGlass ? Surface
        : Freeze(new LinearGradientBrush(IsDark ? Color.FromArgb(0xF4, 0x1C, 0x16, 0x1E) : Color.FromArgb(0xF6, 0xFD, 0xF4, 0xF5),
                                         IsDark ? Color.FromArgb(0xF4, 0x12, 0x18, 0x28) : Color.FromArgb(0xF6, 0xF0, 0xF5, 0xFF), 45));
    public static readonly Brush Card = B(IsGlass ? (IsDark ? Light(0x14) : Light(0x8C)) : IsDark ? Light(0x0F) : Dark(0x08));
    public static readonly Brush Hairline = B(IsGlass ? (IsDark ? Light(0x2A) : Dark(0x14)) : IsDark ? Light(0x1F) : Dark(0x16));
    public static readonly Brush ButtonFill = B(IsGlass ? (IsDark ? Light(0x1C) : Light(0x99)) : IsDark ? Light(0x15) : Dark(0x0C));
    public static readonly Brush ButtonPressed = B(IsGlass ? (IsDark ? Light(0x10) : Light(0x66)) : IsDark ? Light(0x0B) : Dark(0x16));
    public static readonly Brush ButtonHover = B(IsGlass ? (IsDark ? Light(0x2C) : Light(0xD0)) : IsDark ? Light(0x22) : Dark(0x12));
    public static readonly Brush AccentHover = IsGlass ? Gradient(Mix(BrandRed, Colors.White, 0.12), Mix(BrandBlue, Colors.White, 0.12))
                                                       : B(Mix(AccentColor, IsDark ? Colors.White : Colors.Black, 0.12));
    public static readonly Brush AccentPressed = IsGlass ? Gradient(Mix(BrandRed, Colors.Black, 0.18), Mix(BrandBlue, Colors.Black, 0.18))
                                                         : B(Mix(AccentColor, IsDark ? Colors.Black : Colors.White, 0.2));
    public static readonly Brush Selected = B(IsGlass ? (IsDark ? Light(0x30) : Light(0xEE)) : IsDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Colors.White);
    public static readonly Brush PetalFill = B(IsDark ? Color.FromArgb(0xF2, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xFB, 0xFB, 0xFB));
    public static readonly Brush PetalStroke = B(IsDark ? Light(0x26) : Dark(0x1E));
    public static readonly Brush PetalHoverStroke = B(A(0xA0, AccentColor));
    public static readonly Brush Disc = B(IsDark ? Color.FromArgb(0xC8, 0x1A, 0x1A, 0x1A) : Color.FromArgb(0xB8, 0xEE, 0xF0, 0xF2));
    public static readonly Brush CenterFill = B(IsDark ? Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    /// <summary>Gives a normal (not layered) window its style: the dark or light title bar, and in glass the acrylic backdrop
    /// across the whole window. Call before the window is shown.</summary>
    public static void Apply(Window window)
    {
        window.Background = Surface;
        window.Foreground = Ink;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Native.MatchTitleBar(handle, IsDark);
            if (!UsesBackdrop) return;
            if (System.Windows.Interop.HwndSource.FromHwnd(handle) is { } source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Native.Acrylic(handle);
        };
    }

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
