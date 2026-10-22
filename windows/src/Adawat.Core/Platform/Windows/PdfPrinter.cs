using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Adawat.Core.Documents;

/// <summary>Prints HTML to PDF with Microsoft Edge's engine (WebView2, part of Windows 11), which lays out Arabic,
/// mixed scripts and tables exactly as a browser would.</summary>
public static class PdfPrinter
{
    private static readonly string UserData = Path.Combine(Settings.Folder, "WebView2");

    public static Task Print(string html, string output, bool landscape = false)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await PrintOnThisThread(html, output, landscape);
                    done.SetResult();
                }
                catch (Exception e)
                {
                    done.SetException(e is WebView2RuntimeNotFoundException
                        ? new AdawatException("Microsoft Edge WebView2 is needed to make PDFs. Install it from microsoft.com/edge/webview2.")
                        : e);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    private static async Task PrintOnThisThread(string html, string output, bool landscape)
    {
        // A message-only window is enough to host the browser; nothing appears on screen.
        var window = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("AdawatPdf")
        {
            Width = 1, Height = 1, WindowStyle = 0, ParentWindow = new IntPtr(-3),
        });
        try
        {
            Directory.CreateDirectory(UserData);
            var environment = await CoreWebView2Environment.CreateAsync(null, UserData);
            var controller = await environment.CreateCoreWebView2ControllerAsync(window.Handle);
            var web = controller.CoreWebView2;
            web.Settings.IsScriptEnabled = false;
            var loaded = new TaskCompletionSource<bool>();
            web.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            // Large documents go through a file; NavigateToString is limited to 2 MB.
            using var temp = Output.Temp();
            var page = temp.File("page.html");
            await File.WriteAllTextAsync(page, html);
            web.Navigate(new Uri(page).AbsoluteUri);
            if (!await loaded.Task) throw new AdawatException("The document could not be printed as PDF");
            var settings = environment.CreatePrintSettings();
            settings.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
            var ok = await web.PrintToPdfAsync(output, settings);
            controller.Close();
            if (!ok || !File.Exists(output)) throw new AdawatException("The document could not be printed as PDF");
        }
        finally
        {
            window.Dispose();
        }
    }
}
