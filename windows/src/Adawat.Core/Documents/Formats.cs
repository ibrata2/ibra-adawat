using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Markdig;

namespace Adawat.Core.Documents;

/// <summary>EPUB 3 books: chapters are read in spine order; written books get a navigation page, a cover title and embedded pictures.</summary>
public static class Epub
{
    public static Doc Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        string Text(string name) => new StreamReader(zip.GetEntry(name)?.Open() ?? throw new AdawatException("The EPUB does not identify a readable package document.")).ReadToEnd();
        var container = XDocument.Parse(Text("META-INF/container.xml"));
        var opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
                      ?? throw new AdawatException("The EPUB does not identify a readable package document.");
        var opf = XDocument.Parse(Text(opfPath));
        var folder = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item")
            .ToDictionary(e => e.Attribute("id")?.Value ?? "", e => e.Attribute("href")?.Value ?? "");
        var title = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? Path.GetFileNameWithoutExtension(path);
        var doc = new Doc { Title = title };
        var spine = opf.Descendants().Where(e => e.Name.LocalName == "itemref").Select(e => e.Attribute("idref")?.Value ?? "").ToList();
        foreach (var id in spine)
        {
            if (!manifest.TryGetValue(id, out var href)) continue;
            var chapterPath = Normalize(folder + href);
            var entry = zip.GetEntry(chapterPath);
            if (entry == null) continue;
            var html = new StreamReader(entry.Open()).ReadToEnd();
            var chapterFolder = chapterPath.Contains('/') ? chapterPath[..(chapterPath.LastIndexOf('/') + 1)] : "";
            if (doc.Blocks.Count > 0) doc.Blocks.Add(new PageBreak());
            HtmlReader.Read(html, title, src =>
            {
                var image = zip.GetEntry(Normalize(chapterFolder + src));
                if (image == null) return null;
                using var stream = image.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return (memory.ToArray(), Path.GetExtension(src).TrimStart('.').ToLowerInvariant() is "png" ? "png" : Path.GetExtension(src).TrimStart('.').ToLowerInvariant() is "gif" ? "gif" : "jpg");
            }, doc);
        }
        if (doc.Blocks.Count == 0) throw new AdawatException("The EPUB has no readable chapters.");
        return doc;
    }

    private static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (part != "." && part.Length > 0) parts.Add(part);
        }
        return string.Join('/', parts);
    }

    public static void Write(Doc doc, string path)
    {
        // Each page break starts a new chapter; pictures are stored as files beside the chapters.
        var chapters = new List<Doc> { new() { Title = doc.Title } };
        foreach (var block in doc.Blocks)
        {
            if (block is PageBreak) { chapters.Add(new Doc { Title = doc.Title }); continue; }
            chapters[^1].Blocks.Add(block);
        }
        chapters.RemoveAll(chapter => chapter.Blocks.Count == 0);
        if (chapters.Count == 0) chapters.Add(new Doc { Title = doc.Title });
        var rtl = Bidi.IsRightToLeft(doc.PlainText);
        var language = rtl ? "ar" : "en";
        var id = $"urn:uuid:{Guid.NewGuid()}";
        var images = new List<(string Name, Picture Picture)>();

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        // The mimetype entry comes first and uncompressed, as readers require.
        using (var mimetype = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())) mimetype.Write("application/epub+zip");
        Put(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);
        Put(zip, "OEBPS/style.css", DocText.Css(false).Replace("max-width: 46em; margin: 2em auto; padding: 0 1em;", "margin: 0 5%;"));
        for (var index = 0; index < chapters.Count; index++)
        {
            var body = new StringBuilder();
            DocText.Body(chapters[index], body, picture =>
            {
                var name = $"images/image{images.Count + 1}.{(picture.Extension is "png" or "gif" ? picture.Extension : "jpg")}";
                images.Add((name, picture));
                return name;
            });
            var heading = chapters[index].Blocks.OfType<Heading>().FirstOrDefault()?.Text ?? $"{doc.Title} {index + 1}";
            Put(zip, $"OEBPS/chapter{index + 1}.xhtml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{language}" lang="{language}" dir="{(rtl ? "rtl" : "ltr")}">
                <head><meta charset="utf-8"/><title>{DocText.Escape(heading)}</title><link rel="stylesheet" href="style.css"/></head>
                <body>
                {Xhtml(body.ToString())}
                </body>
                </html>
                """);
        }
        foreach (var (name, picture) in images)
        {
            using var stream = zip.CreateEntry($"OEBPS/{name}", CompressionLevel.NoCompression).Open();
            stream.Write(picture.Data);
        }
        var navItems = string.Join("\n", chapters.Select((chapter, i) =>
            $"<li><a href=\"chapter{i + 1}.xhtml\">{DocText.Escape(chapter.Blocks.OfType<Heading>().FirstOrDefault()?.Text ?? $"{doc.Title} {i + 1}")}</a></li>"));
        Put(zip, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{language}" lang="{language}" dir="{(rtl ? "rtl" : "ltr")}">
            <head><meta charset="utf-8"/><title>{DocText.Escape(doc.Title)}</title></head>
            <body><nav epub:type="toc" id="toc"><h1>{DocText.Escape(doc.Title)}</h1><ol>
            {navItems}
            </ol></nav></body>
            </html>
            """);
        var manifestItems = new StringBuilder("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n");
        for (var i = 0; i < chapters.Count; i++) manifestItems.Append($"<item id=\"c{i + 1}\" href=\"chapter{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
        for (var i = 0; i < images.Count; i++)
            manifestItems.Append($"<item id=\"img{i + 1}\" href=\"{images[i].Name}\" media-type=\"{(images[i].Name.EndsWith(".png") ? "image/png" : images[i].Name.EndsWith(".gif") ? "image/gif" : "image/jpeg")}\"/>\n");
        var spine = string.Concat(Enumerable.Range(1, chapters.Count).Select(i => $"<itemref idref=\"c{i}\"/>"));
        Put(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid" xml:lang="{language}"{(rtl ? " dir=\"rtl\"" : "")}>
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{id}</dc:identifier>
                <dc:title>{DocText.Escape(doc.Title)}</dc:title>
                <dc:language>{language}</dc:language>
                <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
              </metadata>
              <manifest>
            {manifestItems}  </manifest>
              <spine{(rtl ? " page-progression-direction=\"rtl\"" : "")}>{spine}</spine>
            </package>
            """);
    }

    /// <summary>HTML void tags closed for XHTML.</summary>
    private static string Xhtml(string html) => System.Text.RegularExpressions.Regex.Replace(html.Replace("<br>", "<br/>"), "<img([^>]*?)(?<!/)>", "<img$1/>");

    private static void Put(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}

/// <summary>OpenDocument text (LibreOffice): paragraphs, headings, lists and tables both ways.</summary>
public static class Odt
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    public static Doc Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new AdawatException($"{Path.GetFileName(path)} could not be read as a document");
        var content = XDocument.Load(entry.Open());
        var doc = new Doc { Title = Path.GetFileNameWithoutExtension(path) };
        var bold = content.Descendants(StyleNs + "style").Where(s => s.Descendants().Any(d => d.Attribute(Fo + "font-weight")?.Value == "bold"))
            .Select(s => s.Attribute(StyleNs + "name")?.Value).ToHashSet();
        var italic = content.Descendants(StyleNs + "style").Where(s => s.Descendants().Any(d => d.Attribute(Fo + "font-style")?.Value == "italic"))
            .Select(s => s.Attribute(StyleNs + "name")?.Value).ToHashSet();
        List<Span> Spans(XElement element)
        {
            var spans = new List<Span>();
            void Walk(XNode node, bool b, bool i)
            {
                if (node is XText text) { spans.Add(new Span(text.Value, b, i)); return; }
                if (node is not XElement e) return;
                if (e.Name == TextNs + "s") { spans.Add(new Span(new string(' ', int.TryParse(e.Attribute(TextNs + "c")?.Value, out var c) ? c : 1))); return; }
                if (e.Name == TextNs + "tab") { spans.Add(new Span("\t")); return; }
                if (e.Name == TextNs + "line-break") { spans.Add(new Span("\n")); return; }
                var style = e.Attribute(TextNs + "style-name")?.Value;
                foreach (var child in e.Nodes()) Walk(child, b || bold.Contains(style), i || italic.Contains(style));
            }
            foreach (var node in element.Nodes()) Walk(node, false, false);
            return spans;
        }
        var body = content.Descendants(Office + "text").FirstOrDefault();
        if (body == null) return doc;
        void Block(XElement e, int depth, bool ordered)
        {
            if (e.Name == TextNs + "h") doc.Blocks.Add(new Heading(int.TryParse(e.Attribute(TextNs + "outline-level")?.Value, out var l) ? l : 1, Spans(e)));
            else if (e.Name == TextNs + "p") { var s = Spans(e); if (s.Any(x => x.Text.Trim().Length > 0)) doc.Blocks.Add(new Paragraph(s)); }
            else if (e.Name == TextNs + "list")
            {
                foreach (var item in e.Elements(TextNs + "list-item"))
                {
                    foreach (var child in item.Elements())
                    {
                        if (child.Name == TextNs + "list") Block(child, depth + 1, ordered);
                        else doc.Blocks.Add(new ListItem(Spans(child), ordered, depth));
                    }
                }
            }
            else if (e.Name == TableNs + "table")
            {
                var rows = e.Descendants(TableNs + "table-row").Select(row => row.Elements(TableNs + "table-cell")
                    .Select(cell => cell.Elements().SelectMany(Spans).ToList()).ToList()).ToList();
                if (rows.Count > 0) doc.Blocks.Add(new Table(rows));
            }
            else foreach (var child in e.Elements()) Block(child, depth, ordered);
        }
        foreach (var element in body.Elements()) Block(element, 0, false);
        return doc;
    }

    public static void Write(Doc doc, string path)
    {
        var rtl = Bidi.IsRightToLeft(doc.PlainText);
        var text = new XElement(Office + "text");
        XElement Inline(XElement into, IEnumerable<Span> spans)
        {
            foreach (var span in spans)
            {
                var style = span.Bold && span.Italic ? "BI" : span.Bold ? "B" : span.Italic ? "I" : null;
                var parts = span.Text.Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0) into.Add(new XElement(TextNs + "line-break"));
                    if (style == null) into.Add(parts[i]);
                    else into.Add(new XElement(TextNs + "span", new XAttribute(TextNs + "style-name", style), parts[i]));
                }
            }
            return into;
        }
        XElement? list = null;
        bool? listOrdered = null;
        foreach (var block in doc.Blocks)
        {
            if (block is ListItem item)
            {
                if (list == null || listOrdered != item.Ordered)
                {
                    list = new XElement(TextNs + "list", new XAttribute(TextNs + "style-name", item.Ordered ? "Numbers" : "Bullets"));
                    text.Add(list);
                    listOrdered = item.Ordered;
                }
                list.Add(new XElement(TextNs + "list-item", Inline(new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", rtl ? "RTL" : "Standard")), item.Spans)));
                continue;
            }
            list = null;
            switch (block)
            {
                case Heading h:
                    text.Add(Inline(new XElement(TextNs + "h", new XAttribute(TextNs + "style-name", $"Heading_20_{Math.Clamp(h.Level, 1, 6)}"),
                                                 new XAttribute(TextNs + "outline-level", Math.Clamp(h.Level, 1, 6))), h.Spans));
                    break;
                case Paragraph p: text.Add(Inline(new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", rtl ? "RTL" : "Standard")), p.Spans)); break;
                case Quote q: text.Add(Inline(new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", "Quotations")), q.Spans)); break;
                case CodeBlock c:
                    foreach (var line in c.Code.TrimEnd().Split('\n')) text.Add(new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", "Code"), line.TrimEnd('\r')));
                    break;
                case Table t:
                    var columns = t.Rows.Max(r => r.Count);
                    var table = new XElement(TableNs + "table", new XElement(TableNs + "table-column", new XAttribute(TableNs + "number-columns-repeated", columns)));
                    foreach (var row in t.Rows)
                        table.Add(new XElement(TableNs + "table-row", row.Select(cell => new XElement(TableNs + "table-cell", Inline(new XElement(TextNs + "p"), cell)))));
                    text.Add(table);
                    break;
                case PageBreak: text.Add(new XElement(TextNs + "p", new XAttribute(TextNs + "style-name", "Break"))); break;
            }
        }
        var content = new XDocument(new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office), new XAttribute(XNamespace.Xmlns + "text", TextNs), new XAttribute(XNamespace.Xmlns + "table", TableNs),
            new XAttribute(XNamespace.Xmlns + "style", StyleNs), new XAttribute(XNamespace.Xmlns + "fo", Fo), new XAttribute(Office + "version", "1.3"),
            new XElement(Office + "automatic-styles",
                new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "B"), new XAttribute(StyleNs + "family", "text"), new XElement(StyleNs + "text-properties", new XAttribute(Fo + "font-weight", "bold"))),
                new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "I"), new XAttribute(StyleNs + "family", "text"), new XElement(StyleNs + "text-properties", new XAttribute(Fo + "font-style", "italic"))),
                new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "BI"), new XAttribute(StyleNs + "family", "text"), new XElement(StyleNs + "text-properties", new XAttribute(Fo + "font-weight", "bold"), new XAttribute(Fo + "font-style", "italic"))),
                new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "RTL"), new XAttribute(StyleNs + "family", "paragraph"), new XAttribute(StyleNs + "parent-style-name", "Standard"),
                    new XElement(StyleNs + "paragraph-properties", new XAttribute(StyleNs + "writing-mode", "rl-tb"), new XAttribute(Fo + "text-align", "end"))),
                new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "Break"), new XAttribute(StyleNs + "family", "paragraph"),
                    new XElement(StyleNs + "paragraph-properties", new XAttribute(Fo + "break-after", "page")))),
            new XElement(Office + "body", text)));
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mimetype = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())) mimetype.Write("application/vnd.oasis.opendocument.text");
        using (var stream = zip.CreateEntry("content.xml").Open()) content.Save(stream);
        using (var writer = new StreamWriter(zip.CreateEntry("META-INF/manifest.xml").Open()))
        {
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8"?>
                <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
                 <manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>
                 <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
                </manifest:manifest>
                """);
        }
    }
}

/// <summary>Markdown through Markdig (tables, task lists and the rest of GitHub's dialect).</summary>
public static class MarkdownFormat
{
    private static readonly Markdig.MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static Doc Read(string markdown, string title) => HtmlReader.Read(Markdig.Markdown.ToHtml(markdown, Pipeline), title);
}
