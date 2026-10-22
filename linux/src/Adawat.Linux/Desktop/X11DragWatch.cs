using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>
/// Watches drags on X11 through a second connection to the X server, with no special permission: the pointer and keys are
/// read sixty times a second while the left button is held, and XFixes reports when a drag source takes the
/// XdndSelection, which every toolkit does as a file drag starts. Keys held before the drag started do not count.
/// </summary>
public sealed class X11DragWatch : IDragWatch
{
    public event Action<ModifierKey, PixelPoint>? KeyDuringDrag;
    public event Action? DragEnded;

    private IntPtr _display;
    private IntPtr _root;
    private IntPtr _xdndSelection;
    private int _fixesEvent;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _buttonDown;

    public bool ButtonDown => _buttonDown;

    public void Start()
    {
        if (_thread != null) return;
        _display = X.XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) { Log.Write("X11: cannot open the display; the wheel will not open on drags"); return; }
        _root = X.XDefaultRootWindow(_display);
        _xdndSelection = X.XInternAtom(_display, "XdndSelection", false);
        if (X.XFixesQueryExtension(_display, out _fixesEvent, out _))
            X.XFixesSelectSelectionInput(_display, _root, _xdndSelection, X.XFixesSetSelectionOwnerNotifyMask);
        else
            Log.Write("X11: no XFixes; drags are recognised by the selection owner alone");
        X.XFlush(_display);
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Adawat X11 drags" };
        _thread.Start();
    }

    private void Loop()
    {
        var eventBuffer = new byte[256];
        bool wasDown = false, moved = false, dragging = false;
        int downX = 0, downY = 0;
        uint maskAtStart = 0, lastMask = 0;
        while (_running)
        {
            // XFixes says when a drag source took the XdndSelection: that is the moment a drag of data starts.
            var ownerChanged = false;
            while (X.XPending(_display) > 0)
            {
                X.XNextEvent(_display, eventBuffer);
                var type = BitConverter.ToInt32(eventBuffer, 0);
                if (_fixesEvent != 0 && type == _fixesEvent + X.XFixesSelectionNotify) ownerChanged = true;
            }
            if (!X.XQueryPointer(_display, _root, out _, out _, out var x, out var y, out _, out _, out var mask))
            {
                Thread.Sleep(50);
                continue;
            }
            LastPointer.Position = new PixelPoint(x, y);
            var down = (mask & X.Button1Mask) != 0;
            if (down && !wasDown)
            {
                moved = false;
                dragging = false;
                downX = x;
                downY = y;
                maskAtStart = mask & X.Modifiers;
                lastMask = maskAtStart;
            }
            if (down)
            {
                if (!moved && (Math.Abs(x - downX) > 6 || Math.Abs(y - downY) > 6)) moved = true;
                if (ownerChanged) dragging = true;
                // Without XFixes, any owner of XdndSelection while the button is held counts.
                if (_fixesEvent == 0 && !dragging) dragging = X.XGetSelectionOwner(_display, _xdndSelection) != IntPtr.Zero;
                var mods = mask & X.Modifiers;
                var pressed = mods & ~lastMask & ~maskAtStart;
                lastMask = mods;
                if (pressed != 0 && moved && dragging)
                {
                    var key = (pressed & X.ShiftMask) != 0 ? ModifierKey.Shift : (pressed & X.ControlMask) != 0 ? ModifierKey.Ctrl : ModifierKey.Alt;
                    var point = new PixelPoint(x, y);
                    LastPointer.Position = point;
                    Dispatcher.UIThread.Post(() => KeyDuringDrag?.Invoke(key, point));
                }
            }
            _buttonDown = down;
            if (!down && wasDown)
            {
                LastPointer.Position = new PixelPoint(x, y);
                if (dragging) Dispatcher.UIThread.Post(() => DragEnded?.Invoke());
                dragging = false;
            }
            wasDown = down;
            Thread.Sleep(down ? 16 : 40);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        if (_display != IntPtr.Zero) X.XCloseDisplay(_display);
        _display = IntPtr.Zero;
    }

    private static class X
    {
        public const uint ShiftMask = 1, ControlMask = 4, Mod1Mask = 8, Button1Mask = 1 << 8;
        public const uint Modifiers = ShiftMask | ControlMask | Mod1Mask;
        public const int XFixesSelectionNotify = 0;
        public const ulong XFixesSetSelectionOwnerNotifyMask = 1;

        [DllImport("libX11.so.6")] public static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] public static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11.so.6")] public static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
        [DllImport("libX11.so.6")] public static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
        [DllImport("libX11.so.6")] public static extern int XFlush(IntPtr display);
        [DllImport("libX11.so.6")] public static extern int XPending(IntPtr display);
        [DllImport("libX11.so.6")] public static extern int XNextEvent(IntPtr display, byte[] eventReturn);
        [DllImport("libX11.so.6")]
        public static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
                                                out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        [DllImport("libXfixes.so.3")] public static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
        [DllImport("libXfixes.so.3")] public static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window, IntPtr selection, ulong eventMask);
    }
}
