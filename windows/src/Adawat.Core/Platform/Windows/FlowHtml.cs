using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace Adawat.Core.Documents;

/// <summary>Bridges WPF FlowDocuments (what RTF reads into) and Adawat's documents.</summary>
public static class FlowHtml
{
    /// <summary>A FlowDocument as simple HTML, keeping headings by size, bold, italic, underline, lists and tables.</summary>
    public static string From(FlowDocument document)
    {
        var html = new StringBuilder("<html><body>");
        var baseSize = document.Blocks.OfType<System.Windows.Documents.Paragraph>().Select(p => p.FontSize).DefaultIfEmpty(12).Min();
        foreach (var block in document.Blocks) Block(block, html, baseSize);
        return html.Append("</body></html>").ToString();
    }

    private static void Block(System.Windows.Documents.Block block, StringBuilder html, double baseSize)
    {
        switch (block)
        {
            case System.Windows.Documents.Paragraph p:
                var size = p.Inlines.OfType<Run>().Select(r => r.FontSize).DefaultIfEmpty(p.FontSize).Max();
                var tag = size >= baseSize * 1.7 ? "h1" : size >= baseSize * 1.4 ? "h2" : size >= baseSize * 1.2 ? "h3" : "p";
                html.Append('<').Append(tag).Append('>');
                foreach (var inline in p.Inlines) Inline(inline, html);
                html.Append("</").Append(tag).Append('>');
                break;
            case List list:
                var listTag = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? "ol" : "ul";
                html.Append('<').Append(listTag).Append('>');
                foreach (var item in list.ListItems)
                {
                    html.Append("<li>");
                    foreach (var inner in item.Blocks)
                    {
                        if (inner is System.Windows.Documents.Paragraph ip) foreach (var inline in ip.Inlines) Inline(inline, html);
                        else Block(inner, html, baseSize);
                    }
                    html.Append("</li>");
                }
                html.Append("</").Append(listTag).Append('>');
                break;
            case System.Windows.Documents.Table table:
                html.Append("<table>");
                foreach (var group in table.RowGroups)
                foreach (var row in group.Rows)
                {
                    html.Append("<tr>");
                    foreach (var cell in row.Cells)
                    {
                        html.Append("<td>");
                        foreach (var inner in cell.Blocks.OfType<System.Windows.Documents.Paragraph>()) foreach (var inline in inner.Inlines) Inline(inline, html);
                        html.Append("</td>");
                    }
                    html.Append("</tr>");
                }
                html.Append("</table>");
                break;
            case Section section:
                foreach (var inner in section.Blocks) Block(inner, html, baseSize);
                break;
        }
    }

    private static void Inline(Inline inline, StringBuilder html)
    {
        switch (inline)
        {
            case Run run:
                var text = SecurityElement.Escape(run.Text) ?? "";
                if (run.FontWeight.ToOpenTypeWeight() >= 600) text = $"<b>{text}</b>";
                if (run.FontStyle == FontStyles.Italic) text = $"<i>{text}</i>";
                if (run.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true) text = $"<u>{text}</u>";
                html.Append(text);
                break;
            case LineBreak:
                html.Append("<br>");
                break;
            case Hyperlink link:
                html.Append($"<a href=\"{SecurityElement.Escape(link.NavigateUri?.ToString() ?? "")}\">");
                foreach (var inner in link.Inlines) Inline(inner, html);
                html.Append("</a>");
                break;
            case System.Windows.Documents.Span span:
                var bold = span is System.Windows.Documents.Bold || span.FontWeight.ToOpenTypeWeight() >= 600;
                var italic = span is System.Windows.Documents.Italic || span.FontStyle == FontStyles.Italic;
                if (bold) html.Append("<b>");
                if (italic) html.Append("<i>");
                foreach (var inner in span.Inlines) Inline(inner, html);
                if (italic) html.Append("</i>");
                if (bold) html.Append("</b>");
                break;
        }
    }

    /// <summary>A document as FlowDocument XAML, for writing RTF.</summary>
    public static string ToXaml(Doc doc)
    {
        var xaml = new StringBuilder("<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" FontFamily=\"Calibri, Arial\" FontSize=\"14.67\">");
        string Spans(IEnumerable<Span> spans) => string.Concat(spans.Select(span =>
        {
            var parts = (SecurityElement.Escape(span.Text) ?? "").Split('\n');
            var text = string.Join("<LineBreak/>", parts.Select(p => $"<Run>{p}</Run>"));
            if (span.Bold) text = $"<Bold>{text}</Bold>";
            if (span.Italic) text = $"<Italic>{text}</Italic>";
            if (span.Underline) text = $"<Underline>{text}</Underline>";
            return text;
        }));
        string Dir(string text) => Bidi.IsRightToLeft(text) ? " FlowDirection=\"RightToLeft\"" : "";
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Heading h:
                    var size = h.Level switch { 1 => 26, 2 => 21, 3 => 18, _ => 16 };
                    xaml.Append($"<Paragraph FontSize=\"{size}\" FontWeight=\"Bold\"{Dir(h.Text)}>{Spans(h.Spans)}</Paragraph>");
                    break;
                case Paragraph p: xaml.Append($"<Paragraph{Dir(p.Text)}>{Spans(p.Spans)}</Paragraph>"); break;
                case Quote q: xaml.Append($"<Paragraph Margin=\"30,0,0,0\" FontStyle=\"Italic\"{Dir(q.Text)}>{Spans(q.Spans)}</Paragraph>"); break;
                case ListItem item:
                    xaml.Append($"<List MarkerStyle=\"{(item.Ordered ? "Decimal" : "Disc")}\" Margin=\"{20 + item.Depth * 20},0,0,0\"><ListItem><Paragraph{Dir(item.Text)}>{Spans(item.Spans)}</Paragraph></ListItem></List>");
                    break;
                case CodeBlock c: xaml.Append($"<Paragraph FontFamily=\"Consolas\">{SecurityElement.Escape(c.Code)?.Replace("\n", "<LineBreak/>")}</Paragraph>"); break;
                case Table t:
                    xaml.Append("<Table CellSpacing=\"0\" BorderBrush=\"Gray\" BorderThickness=\"1\"><TableRowGroup>");
                    foreach (var row in t.Rows)
                        xaml.Append("<TableRow>").Append(string.Concat(row.Select(cell => $"<TableCell BorderBrush=\"Gray\" BorderThickness=\"0.5\" Padding=\"4\"><Paragraph>{Spans(cell)}</Paragraph></TableCell>"))).Append("</TableRow>");
                    xaml.Append("</TableRowGroup></Table>");
                    break;
                case PageBreak: xaml.Append("<Paragraph BreakPageBefore=\"True\"/>"); break;
            }
        }
        return xaml.Append("</FlowDocument>").ToString();
    }
}
