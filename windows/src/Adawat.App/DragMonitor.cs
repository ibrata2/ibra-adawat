using System.Windows.Threading;
using Adawat.Core;

namespace Adawat.App;

/// <summary>
/// Notices a file drag and the key pressed during it, without any special permission: a low-level mouse hook says when
/// the left button goes down and moves, and the keys are read while it is held. Explorer's drag picture window confirms that
/// shell items, not a window or a text selection, are being dragged.
/// </summary>
public sealed class DragMonitor : IDisposable
{
    private readonly Native.HookProc _proc;
    private IntPtr _hook;
    private readonly DispatcherTimer _timer;
    private Native.POINT _downAt;
    private bool _buttonDown, _moved, _shiftWas, _ctrlWas, _altWas;

    /// <summary>A modifier key went down while files were being dragged, with the pointer position in screen pixels.</summary>
    public event Action<ModifierKey, System.Windows.Point>? KeyDuringDrag;
    /// <summary>The left button came back up: the drag ended, dropped somewhere or cancelled.</summary>
    public event Action? DragEnded;

    public bool IsDragging => _buttonDown && _moved;

    public DragMonitor()
    {
        _proc = Hook;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            switch (message)
            {
                case Native.WM_LBUTTONDOWN:
                    _buttonDown = true;
                    _moved = false;
                    _downAt = data.Point;
                    // Keys held before the drag started do not count; only a press during the drag opens a wheel.
                    _shiftWas = Native.IsDown(Native.VK_SHIFT);
                    _ctrlWas = Native.IsDown(Native.VK_CONTROL);
                    _altWas = Native.IsDown(Native.VK_MENU);
                    _timer.Start();
                    break;
                case Native.WM_MOUSEMOVE when _buttonDown && !_moved:
                    var dx = Math.Abs(data.Point.X - _downAt.X);
                    var dy = Math.Abs(data.Point.Y - _downAt.Y);
                    if (dx > Native.GetSystemMetrics(Native.SM_CXDRAG) || dy > Native.GetSystemMetrics(Native.SM_CYDRAG)) _moved = true;
                    break;
                case Native.WM_LBUTTONUP:
                    if (_buttonDown) Finish();
                    break;
            }
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Finish()
    {
        _buttonDown = false;
        _moved = false;
        _timer.Stop();
        DragEnded?.Invoke();
    }

    private void Poll()
    {
        // The hook can miss a button-up that another app swallowed; the key state is the truth.
        if (!Native.IsDown(Native.VK_LBUTTON))
        {
            if (_buttonDown) Finish();
            return;
        }
        // Pointers moved without input events (remote sessions, pen drivers, automation) never reach the hook.
        if (!_moved && Native.GetCursorPos(out var now)
            && (Math.Abs(now.X - _downAt.X) > Native.GetSystemMetrics(Native.SM_CXDRAG) || Math.Abs(now.Y - _downAt.Y) > Native.GetSystemMetrics(Native.SM_CYDRAG)))
            _moved = true;
        Check(Native.VK_SHIFT, ref _shiftWas, ModifierKey.Shift);
        Check(Native.VK_CONTROL, ref _ctrlWas, ModifierKey.Ctrl);
        Check(Native.VK_MENU, ref _altWas, ModifierKey.Alt);
    }

    private void Check(int key, ref bool was, ModifierKey modifier)
    {
        var down = Native.IsDown(key);
        if (down && !was && File.Exists(Path.Combine(Settings.Folder, "debug-drag"))) Log.Write($"key {modifier} moved={_moved} shellDrag={Native.ShellDragInProgress()}");
        if (down && !was && _moved && Native.ShellDragInProgress() && Native.GetCursorPos(out var point))
            KeyDuringDrag?.Invoke(modifier, new System.Windows.Point(point.X, point.Y));
        was = down;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
