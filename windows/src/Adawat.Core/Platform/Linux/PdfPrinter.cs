namespace Adawat.Core.Documents;

/// <summary>Prints HTML to PDF with WeasyPrint, which lays out Arabic, mixed scripts and tables with HarfBuzz and honours
/// @page rules. Chromium or LibreOffice are used when WeasyPrint is missing.</summary>
public static class PdfPrinter
{
    public static async Task Print(string html, string output, bool landscape = false)
    {
        if (landscape) html = html.Replace("</head>", "<style>@page { size: landscape; }</style>\n</head>");
        using var temp = Output.Temp();
        var page = temp.File("page.html");
        await File.WriteAllTextAsync(page, html);
        if (ProcessRunner.Find("weasyprint") is { } weasyprint)
        {
            var result = await ProcessRunner.Run(weasyprint, ["--quiet", "--presentational-hints", page, output]);
            if (result.ExitCode == 0 && File.Exists(output)) return;
        }
        foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "microsoft-edge" })
        {
            if (ProcessRunner.Find(name) is not { } browser) continue;
            var result = await ProcessRunner.Run(browser, ["--headless", "--disable-gpu", "--no-pdf-header-footer", "--no-first-run",
                                                           $"--user-data-dir={temp.File("profile")}", $"--print-to-pdf={output}", new Uri(page).AbsoluteUri]);
            if (result.ExitCode == 0 && File.Exists(output)) return;
        }
        if (Office.LibreOffice is { } soffice)
        {
            var result = await ProcessRunner.Run(soffice, ["--headless", "--norestore", "--convert-to", "pdf:writer_web_pdf_Export", "--outdir", temp.Path, page]);
            var pdf = temp.File("page.pdf");
            if (result.ExitCode == 0 && File.Exists(pdf)) { File.Move(pdf, output, true); return; }
        }
        throw new AdawatException($"Making PDFs needs WeasyPrint. Install it with: {Desktop.Install("weasyprint", pacman: "python-weasyprint")}");
    }
}
