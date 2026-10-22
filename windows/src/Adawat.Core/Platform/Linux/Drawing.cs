namespace Adawat.Core.Graphics;

/// <summary>The few WPF value types the shared code lays QR codes out with, with the same names and behaviour, so Qr.cs
/// compiles unchanged on Linux.</summary>
public readonly record struct Color(byte A, byte R, byte G, byte B)
{
    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
}

public static class Colors
{
    public static readonly Color Black = Color.FromRgb(0, 0, 0);
    public static readonly Color White = Color.FromRgb(255, 255, 255);
    public static readonly Color Transparent = Color.FromArgb(0, 255, 255, 255);
}

public readonly record struct Size(double Width, double Height);

/// <summary>A mutable rectangle like System.Windows.Rect: Inflate and Offset change it in place.</summary>
public struct Rect
{
    public double X, Y, Width, Height;

    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    public readonly double Left => X;
    public readonly double Top => Y;
    public readonly double Right => X + Width;
    public readonly double Bottom => Y + Height;

    public void Inflate(double width, double height)
    {
        X -= width;
        Y -= height;
        Width = Math.Max(0, Width + width * 2);
        Height = Math.Max(0, Height + height * 2);
    }

    public void Offset(double x, double y)
    {
        X += x;
        Y += y;
    }

    public readonly bool IntersectsWith(Rect other) =>
        other.Left <= Right && other.Right >= Left && other.Top <= Bottom && other.Bottom >= Top;
}
