using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Threading;
using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>
/// Drags on GNOME under Wayland, reported by the Adawat GNOME Shell extension. The Shell sees every drag and the pointer;
/// it connects to $XDG_RUNTIME_DIR/adawat/drag.sock and writes one line per event:
/// "key shift|ctrl|alt X Y" when a key goes down during a drag, and "end" when the drag finishes.
/// </summary>
public sealed class ShellDragWatch : IDragWatch
{
    public event Action<ModifierKey, PixelPoint>? KeyDuringDrag;
    public event Action? DragEnded;

    private Socket? _listener;
    private volatile bool _dragging;

    public bool ButtonDown => _dragging;

    public static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run ? run : Path.GetTempPath(), "adawat", "drag.sock");

    public void Start()
    {
        if (_listener != null) return;
        var path = SocketPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(path));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _listener.Listen(8);
        _ = Accept(_listener);
    }

    private async Task Accept(Socket listener)
    {
        while (true)
        {
            Socket client;
            try { client = await listener.AcceptAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            _ = Read(client);
        }
    }

    private async Task Read(Socket client)
    {
        using var _ = client;
        using var stream = new NetworkStream(client);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        try
        {
            while (await reader.ReadLineAsync() is { } line) Handle(line.Trim());
        }
        catch (IOException) { }
    }

    private void Handle(string line)
    {
        if (File.Exists(Path.Combine(Settings.Folder, "debug-drag"))) Log.Write($"shell: {line}");
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        switch (parts[0])
        {
            case "begin":
                _dragging = true;
                break;
            case "key" when parts.Length >= 4 && int.TryParse(parts[2], out var x) && int.TryParse(parts[3], out var y):
                _dragging = true;
                var key = parts[1] switch { "shift" => ModifierKey.Shift, "ctrl" => ModifierKey.Ctrl, _ => ModifierKey.Alt };
                var point = new PixelPoint(x, y);
                LastPointer.Position = point;
                Dispatcher.UIThread.Post(() => KeyDuringDrag?.Invoke(key, point));
                break;
            case "end":
                _dragging = false;
                Dispatcher.UIThread.Post(() => DragEnded?.Invoke());
                break;
        }
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
        try { File.Delete(SocketPath); } catch (IOException) { }
    }
}
