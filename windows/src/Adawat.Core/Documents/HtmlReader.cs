using HtmlAgilityPack;

namespace Adawat.Core.Documents;

/// <summary>Reads HTML (web pages, EPUB chapters, Markdown after Markdig) into a <see cref="Doc"/>.</summary>
public static class HtmlReader
{
    /// <param name="resolveImage">Turns an img src into bytes and an extension, for pictures packed beside the page.</param>
    public static Doc Read(string html, string title = "Untitled", Func<string, (byte[] Data, string Ext)?>? resolveImage = null, Doc? into = null)
    {
        var page = new HtmlDocument();
        page.LoadHtml(html);
        var doc = into ?? new Doc { Title = title };
        var heading = page.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
        if (into == null && !string.IsNullOrEmpty(heading)) doc.Title = System.Net.WebUtility.HtmlDecode(heading);
        var body = page.DocumentNode.SelectSingleNode("//body") ?? page.DocumentNode;
        new Walker(doc, resolveImage).Blocks(body);
        return doc;
    }

    private sealed class Walker(Doc doc, Func<string, (byte[] Data, string Ext)?>? resolveImage)
    {
        private readonly List<Span> _pending = [];

        public void Blocks(HtmlNode node)
        {
            foreach (var child in node.ChildNodes) Block(child);
            Flush();
        }

        private void Block(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Text) { Inline(node, new Span("")); return; }
            if (node.NodeType != HtmlNodeType.Element) return;
            var name = node.Name.ToLowerInvariant();
            switch (name)
            {
                case "script" or "style" or "head" or "noscript" or "template": return;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Flush();
                    doc.Blocks.Add(new Heading(name[1] - '0', Spans(node)));
                    return;
                case "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "aside" or "figure" or "figcaption" or "center" or "nav":
                    if (node.ChildNodes.Any(child => IsBlock(child.Name)))
                    {
                        Flush();
                        foreach (var child in node.ChildNodes) Block(child);
                        Flush();
                    }
                    else
                    {
                        Flush();
                        var spans = Spans(node);
                        if (spans.Any(s => s.Text.Trim().Length > 0)) doc.Blocks.Add(new Paragraph(spans, AlignOf(node)));
                        foreach (var img in node.Descendants("img")) Image(img);
                    }
                    return;
                case "ul" or "ol":
                    Flush();
                    List(node, name == "ol", 0);
                    return;
                case "table":
                    Flush();
                    var rows = node.Descendants("tr").Select(tr => tr.ChildNodes.Where(c => c.Name is "td" or "th").Select(Spans).ToList()).Where(r => r.Count > 0).ToList();
                    if (rows.Count > 0) doc.Blocks.Add(new Table(rows, node.Descendants("th").Any()));
                    return;
                case "pre":
                    Flush();
                    doc.Blocks.Add(new CodeBlock(System.Net.WebUtility.HtmlDecode(node.InnerText)));
                    return;
                case "blockquote":
                    Flush();
                    doc.Blocks.Add(new Quote(Spans(node)));
                    return;
                case "img":
                    Flush();
                    Image(node);
                    return;
                case "hr":
                    Flush();
                    if (node.GetAttributeValue("class", "").Contains("break") || node.GetAttributeValue("style", "").Contains("page-break")) doc.Blocks.Add(new PageBreak());
                    return;
                case "br":
                    _pending.Add(new Span("\n"));
                    return;
                default:
                    if (node.GetAttributeValue("style", "").Contains("page-break-after") || node.GetAttributeValue("class", "") == "break")
                    {
                        Flush();
                        doc.Blocks.Add(new PageBreak());
                        return;
                    }
                    Inline(node, new Span(""));
                    return;
            }
        }

        private void List(HtmlNode list, bool ordered, int depth)
        {
            foreach (var li in list.ChildNodes.Where(c => c.Name == "li"))
            {
                var own = new List<Span>();
                foreach (var child in li.ChildNodes.Where(c => c.Name is not ("ul" or "ol"))) Collect(child, new Span(""), own);
                doc.Blocks.Add(new ListItem(Clean(own), ordered, depth));
                foreach (var nested in li.ChildNodes.Where(c => c.Name is "ul" or "ol")) List(nested, nested.Name == "ol", depth + 1);
            }
        }

        private void Image(HtmlNode img)
        {
            var src = img.GetAttributeValue("src", "");
            if (src.Length == 0) return;
            (byte[] Data, string Ext)? picture = null;
            if (src.StartsWith("data:"))
            {
                var comma = src.IndexOf(',');
                var meta = src[5..Math.Max(5, comma)];
                if (comma > 0 && meta.Contains("base64"))
                {
                    try { picture = (Convert.FromBase64String(src[(comma + 1)..]), meta.Contains("png") ? "png" : meta.Contains("gif") ? "gif" : meta.Contains("webp") ? "webp" : "jpg"); }
                    catch (FormatException) { }
                }
            }
            else
            {
                picture = resolveImage?.Invoke(System.Net.WebUtility.UrlDecode(src));
            }
            if (picture is { } p) doc.Blocks.Add(new Picture(p.Data, p.Ext, Alt: img.GetAttributeValue("alt", "")));
        }

        private void Inline(HtmlNode node, Span style) => Collect(node, style, _pending);

        private List<Span> Spans(HtmlNode node)
        {
            var spans = new List<Span>();
            foreach (var child in node.ChildNodes) Collect(child, new Span(""), spans);
            return Clean(spans);
        }

        private static void Collect(HtmlNode node, Span style, List<Span> into)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = System.Net.WebUtility.HtmlDecode(node.InnerText);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                if (text.Length > 0) into.Add(style with { Text = text });
                return;
            }
            if (node.NodeType != HtmlNodeType.Element) return;
            var name = node.Name.ToLowerInvariant();
            if (name is "script" or "style" or "img") return;
            if (name == "br") { into.Add(new Span("\n")); return; }
            var css = node.GetAttributeValue("style", "");
            var next = style with
            {
                Bold = style.Bold || name is "b" or "strong" || css.Contains("font-weight:bold") || css.Contains("font-weight: bold") || css.Contains("font-weight:700"),
                Italic = style.Italic || name is "i" or "em" || css.Contains("italic"),
                Underline = style.Underline || name == "u" || css.Contains("underline"),
                Code = style.Code || name is "code" or "kbd" or "tt",
                Link = name == "a" && node.GetAttributeValue("href", "") is { Length: > 0 } href && !href.StartsWith('#') ? href : style.Link,
            };
            foreach (var child in node.ChildNodes) Collect(child, next, into);
        }

        private static List<Span> Clean(List<Span> spans)
        {
            // Trim the edges and merge neighbours that look the same.
            var merged = new List<Span>();
            foreach (var span in spans)
            {
                if (merged.Count > 0 && merged[^1] with { Text = "" } == span with { Text = "" }) merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
                else merged.Add(span);
            }
            if (merged.Count > 0) merged[0] = merged[0] with { Text = merged[0].Text.TrimStart() };
            if (merged.Count > 0) merged[^1] = merged[^1] with { Text = merged[^1].Text.TrimEnd() };
            return merged.Where(s => s.Text.Length > 0).ToList();
        }

        private void Flush()
        {
            var spans = Clean([.. _pending]);
            _pending.Clear();
            if (spans.Any(s => s.Text.Trim().Length > 0)) doc.Blocks.Add(new Paragraph(spans));
        }

        private static bool IsBlock(string name) => name is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "ul" or "ol" or "table" or "pre"
            or "blockquote" or "section" or "article" or "img" or "hr" or "figure" or "header" or "footer" or "main";

        private static Align AlignOf(HtmlNode node)
        {
            var css = (node.GetAttributeValue("style", "") + " " + node.GetAttributeValue("align", "")).ToLowerInvariant();
            if (css.Contains("center")) return Align.Center;
            if (css.Contains("justify")) return Align.Justify;
            if (css.Contains("right") || css.Contains("end")) return Align.End;
            return Align.Start;
        }
    }
}
