using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>The clipboard through Avalonia while the app runs, offering files the way GNOME Files and other file managers
/// paste them. Avalonia's clipboard belongs to a window; the toast window serves.</summary>
public sealed class ClipboardHost : IClipboardHost
{
    private static readonly DataFormat<byte[]> GnomeFiles = DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");
    private static readonly DataFormat<byte[]> UriList = DataFormat.CreateBytesPlatformFormat("text/uri-list");

    private static IClipboard? Board => TopLevel.GetTopLevel(Hud.Shared)?.Clipboard;

    public void SetText(string text) => OnUi(async () => { if (Board is { } board) await board.SetTextAsync(text); });

    public void SetFiles(IReadOnlyList<string> paths) => OnUi(async () =>
    {
        if (Board is not { } board) return;
        var uris = paths.Select(path => new Uri(path).AbsoluteUri).ToList();
        var item = new DataTransferItem();
        item.Set(GnomeFiles, System.Text.Encoding.UTF8.GetBytes("copy\n" + string.Join("\n", uris)));
        item.Set(UriList, System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
        item.SetText(string.Join("\n", paths));
        var transfer = new DataTransfer();
        transfer.Add(item);
        await board.SetDataAsync(transfer);
    });

    public void SetPng(byte[] png) => OnUi(async () =>
    {
        if (Board is not { } board) return;
        using var stream = new MemoryStream(png);
        await board.SetBitmapAsync(new Bitmap(stream));
    });

    public string? GetText()
    {
        if (Dispatcher.UIThread.CheckAccess()) return null;
        return Dispatcher.UIThread.InvokeAsync(async () => Board is { } board ? await board.TryGetTextAsync() : null).GetAwaiter().GetResult();
    }

    private static void OnUi(Func<Task> work) => Dispatcher.UIThread.Post(async () =>
    {
        try { await work(); }
        catch (Exception e) { Log.Write($"clipboard: {e.Message}"); }
    });
}
