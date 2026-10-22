namespace Adawat.Core.Documents;

/// <summary>Rich Text through WPF's own RTF reader and writer, which keep Arabic, bold, italic and tables.</summary>
public static class Rtf
{
    public static Doc Read(string path)
    {
        string? html = null;
        Sta.Run(() =>
        {
            var document = new System.Windows.Documents.FlowDocument();
            var range = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd);
            using var stream = File.OpenRead(path);
            range.Load(stream, System.Windows.DataFormats.Rtf);
            html = FlowHtml.From(document);
        });
        return HtmlReader.Read(html ?? "", Path.GetFileNameWithoutExtension(path));
    }

    public static void Write(Doc doc, string path)
    {
        var xaml = FlowHtml.ToXaml(doc);
        Sta.Run(() =>
        {
            var document = (System.Windows.Documents.FlowDocument)System.Windows.Markup.XamlReader.Parse(xaml);
            var range = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd);
            using var stream = File.Create(path);
            range.Save(stream, System.Windows.DataFormats.Rtf);
        });
    }
}

/// <summary>Runs WPF document work on a single-threaded apartment, as WPF requires.</summary>
public static class Sta
{
    public static void Run(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) { action(); return; }
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }
}
