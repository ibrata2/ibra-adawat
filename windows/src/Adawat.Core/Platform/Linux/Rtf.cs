namespace Adawat.Core.Documents;

/// <summary>Rich Text through LibreOffice, which reads and writes RTF with Arabic, bold, italic and tables.</summary>
public static class Rtf
{
    public static Doc Read(string path)
    {
        using var temp = Output.Temp();
        var html = Convert(path, "html:XHTML Writer File:UTF8", temp, "html");
        return HtmlReader.Read(File.ReadAllText(html), Path.GetFileNameWithoutExtension(path));
    }

    public static void Write(Doc doc, string path)
    {
        using var temp = Output.Temp();
        var source = temp.File($"{Path.GetFileNameWithoutExtension(path)}.html");
        File.WriteAllText(source, DocText.Html(doc));
        File.Move(Convert(source, "rtf:Rich Text Format", temp, "rtf", writer: true), path, true);
    }

    private static string Convert(string path, string filter, TempFolder temp, string extension, bool writer = false)
    {
        var soffice = Office.LibreOffice ?? throw new AdawatException($"Rich Text needs LibreOffice. Install it with: {Desktop.Install("libreoffice-writer", pacman: "libreoffice-fresh")}");
        var folder = temp.File("out");
        Directory.CreateDirectory(folder);
        // HTML opens in Writer/Web unless the Writer import filter is named; RTF written from Writer/Web loses its styles.
        string[] infilter = writer ? ["--infilter=HTML (StarWriter)"] : [];
        var result = Task.Run(() => ProcessRunner.Run(soffice, ["--headless", "--norestore", .. infilter, "--convert-to", filter, "--outdir", folder, path])).GetAwaiter().GetResult();
        var output = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}.{extension}");
        if (result.ExitCode != 0 || !File.Exists(output)) throw new AdawatException($"LibreOffice could not convert {Path.GetFileName(path)}.");
        return output;
    }
}

/// <summary>Windows runs document work on a single-threaded apartment; Linux has no apartments, so the work just runs.</summary>
public static class Sta
{
    public static void Run(Action action) => action();
}
