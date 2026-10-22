using System.Collections.Specialized;

namespace Adawat.Core;

/// <summary>The clipboard, from any thread: WPF needs a single-threaded apartment, so work hops onto one.</summary>
public static class Clipboard
{
    public static void CopyText(string text) => Sta(() => System.Windows.Clipboard.SetText(text));

    public static void CopyFiles(IEnumerable<string> paths)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        Sta(() => System.Windows.Clipboard.SetFileDropList(list));
    }

    public static void CopyImage(System.Windows.Media.Imaging.BitmapSource image) => Sta(() => System.Windows.Clipboard.SetImage(image));

    public static string? Text() { string? text = null; Sta(() => text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null); return text; }

    private static void Sta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) { Retry(action); return; }
        var thread = new Thread(() => Retry(action));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    /// <summary>Another app may hold the clipboard for a moment.</summary>
    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5) { Thread.Sleep(60); }
        }
    }
}
