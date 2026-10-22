using System.Text;
using Adawat.Core;
using Adawat.Core.Media;

namespace Adawat.Cli;

/// <summary>
/// adawat-cli list &lt;files…&gt;            the wheel choices for these files
/// adawat-cli run &lt;id&gt; &lt;files…&gt;        runs one choice with its saved defaults
/// adawat-cli doctor                     versions, tools and encoders, for bug reports
/// Add --lang ar or --lang en to any command to choose the language of its messages.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var list = args.ToList();
        var lang = list.IndexOf("--lang");
        if (lang >= 0 && lang + 1 < list.Count)
        {
            L10n.Override(list[lang + 1]);
            list.RemoveRange(lang, 2);
        }
        if (list.Count == 0) return Usage();
        try
        {
            return list[0].TrimStart('-') switch
            {
                "list" => List(list.Skip(1).Select(Path.GetFullPath).ToList()),
                "run" when list.Count >= 3 => Run(list[1], list.Skip(2).Select(Path.GetFullPath).ToList()).GetAwaiter().GetResult(),
                "doctor" => Doctor().GetAwaiter().GetResult(),
                _ => Usage(),
            };
        }
        catch (AdawatException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: adawat-cli list <files…> | adawat-cli run <operation-id> <files…> | adawat-cli doctor   [--lang ar|en]");
        return 2;
    }

    private static int List(IReadOnlyList<string> files)
    {
        foreach (var mode in Enum.GetValues<WheelMode>())
            Console.WriteLine($"{mode}: {string.Join(" ", Catalog.For(mode, files).Select(op => op.Id))}");
        return 0;
    }

    private static async Task<int> Run(string id, IReadOnlyList<string> files)
    {
        var operation = Catalog.Find(id, files);
        if (operation == null)
        {
            var available = Enum.GetValues<WheelMode>().SelectMany(mode => Catalog.For(mode, files)).Select(op => op.Id);
            Console.Error.WriteLine($"No operation {id} for these files. Available: {string.Join(" ", available)}");
            return 2;
        }
        var lastLine = "";
        var context = new OperationContext(text =>
        {
            if (text == lastLine) return;
            lastLine = text;
            Console.Error.WriteLine(text);
        });
        var outputs = await operation.Run(files, context);
        if (context.Message != null) Console.Error.WriteLine(L10n.T(context.Message));
        foreach (var output in outputs) Console.WriteLine(output);
        return 0;
    }

    private static async Task<int> Doctor()
    {
        Console.WriteLine($"Adawat {typeof(Catalog).Assembly.GetName().Version} · {Environment.OSVersion.VersionString} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Language: {(L10n.IsArabic ? "ar" : "en")} · .NET {Environment.Version}");
        var ffmpeg = ProcessRunner.Find("ffmpeg");
        var ffprobe = ProcessRunner.Find("ffprobe");
        Console.WriteLine($"ffmpeg: {ffmpeg ?? "not found"}");
        Console.WriteLine($"ffprobe: {ffprobe ?? "not found"}");
        if (ffmpeg != null)
        {
            var version = await ProcessRunner.Run(ffmpeg, ["-hide_banner", "-version"]);
            Console.WriteLine("  " + version.Output.Split('\n').FirstOrDefault()?.Trim());
            var encoders = await ProcessRunner.Run(ffmpeg, ["-hide_banner", "-encoders"]);
            var wanted = new[] { "libx264", "libx265", "libvpx-vp9", "libopus", "libmp3lame", "libvorbis", "aac", "flac" };
            Console.WriteLine("  encoders: " + string.Join(" ", wanted.Select(e => encoders.Output.Contains($" {e} ") ? e : $"({e} missing)")));
        }
        var formats = Core.Images.ImageFormat.All.Where(f => f.CanWrite).Select(f => f.Name);
        Console.WriteLine($"Image output: {string.Join(", ", formats)}");
#if WINDOWS
        Console.WriteLine($"Office: Word {(Core.Documents.Office.HasWord ? "yes" : "no")} · PowerPoint {(Core.Documents.Office.HasPowerPoint ? "yes" : "no")} · LibreOffice {(Core.Documents.Office.LibreOffice != null ? "yes" : "no")}");
        Console.WriteLine($"WebView2: {(WebViewVersion() ?? "not found")}");
#else
        Console.WriteLine($"Desktop: {Desktop.Name} · {(Desktop.IsWayland ? "Wayland" : "X11")}");
        foreach (var tool in new[] { "soffice", "tesseract", "weasyprint", "wl-copy", "xclip", "gdbus", "xdg-open" })
            Console.WriteLine($"{tool}: {ProcessRunner.Find(tool) ?? "not found"}");
        Console.WriteLine($"Downloads: {Output.Downloads}");
#endif
        return 0;
    }
#if WINDOWS

    private static string? WebViewVersion()
    {
        try { return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception) { return null; }
    }
#endif
}
