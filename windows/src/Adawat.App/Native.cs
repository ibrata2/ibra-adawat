using System.Runtime.InteropServices;

namespace Adawat.App;

/// <summary>The few Win32 calls WPF does not wrap: low-level mouse hooks, key state, window styles and cursor position.</summary>
internal static partial class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEMOVE = 0x0200;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LBUTTON = 0x01;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT Point; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? name);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int key);

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string? className, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    public const int SM_CXDRAG = 68, SM_CYDRAG = 69;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2, DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PlaySound(byte[] sound, IntPtr module, uint flags);

    public const uint SND_MEMORY = 0x0004, SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    /// <summary>The work area (screen minus taskbar), in pixels, of the monitor showing <paramref name="x"/>, <paramref name="y"/>.</summary>
    public static RECT WorkArea(int x, int y)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT { X = x, Y = y }, 2), ref info);
        return info.Work;
    }

    /// <summary>Explorer shows the picture of what is being dragged in a window of this class, so its presence means
    /// files (or other shell items) are on the move.</summary>
    public static bool ShellDragInProgress()
    {
        var window = FindWindow("SysDragImage", null);
        return window != IntPtr.Zero && IsWindowVisible(window);
    }

    /// <summary>Keeps a window from taking focus or showing in Alt+Tab.</summary>
    public static void MakeToolWindow(IntPtr window, bool clickThrough = false)
    {
        var style = (long)GetWindowLongPtr(window, GWL_EXSTYLE);
        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        if (clickThrough) style |= WS_EX_TRANSPARENT; else style &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(style));
    }

    public static void RoundCorners(IntPtr window)
    {
        var preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
