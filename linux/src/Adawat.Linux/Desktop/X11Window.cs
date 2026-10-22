using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Adawat.Linux.Desktop;

/// <summary>
/// Two X11 window settings Avalonia has no property for. Override-redirect windows skip the window manager: they map at
/// once, exactly where asked and above other windows, even while a drag holds the pointer, which GNOME otherwise defers.
/// An empty input shape lets clicks pass through, for the toast.
/// </summary>
public static class X11Window
{
    private static IntPtr _display;

    private static IntPtr Display => _display != IntPtr.Zero ? _display : _display = XOpenDisplay(IntPtr.Zero);

    private static IntPtr Handle(Window window) =>
        window.TryGetPlatformHandle() is { HandleDescriptor: "XID" } handle ? handle.Handle : IntPtr.Zero;

    /// <summary>Call before the window is first shown; it takes effect when the window maps.</summary>
    public static void SkipWindowManager(Window window)
    {
        var id = Handle(window);
        if (id == IntPtr.Zero || Display == IntPtr.Zero) return;
        var attributes = new XSetWindowAttributes { override_redirect = 1 };
        XChangeWindowAttributes(Display, id, CWOverrideRedirect, ref attributes);
        XFlush(Display);
    }

    public static void ClickThrough(Window window)
    {
        var id = Handle(window);
        if (id == IntPtr.Zero || Display == IntPtr.Zero) return;
        XShapeCombineRectangles(Display, id, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0);
        XFlush(Display);
    }

    private const ulong CWOverrideRedirect = 1 << 9;
    private const int ShapeInput = 2, ShapeSet = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public IntPtr background_pixmap;
        public UIntPtr background_pixel;
        public IntPtr border_pixmap;
        public UIntPtr border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public UIntPtr backing_planes;
        public UIntPtr backing_pixel;
        public int save_under;
        public IntPtr event_mask;
        public IntPtr do_not_propagate_mask;
        public int override_redirect;
        public IntPtr colormap;
        public IntPtr cursor;
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XChangeWindowAttributes(IntPtr display, IntPtr window, ulong valueMask, ref XSetWindowAttributes attributes);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr rectangles, int count, int operation, int ordering);
}
