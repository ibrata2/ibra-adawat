using System.Net;
using System.Text;

namespace Adawat.Core.Documents;

/// <summary>A document every format reads into and writes out of: headings, paragraphs, lists, tables, pictures and
/// page breaks, with bold, italic, underline, links and code in the text. Small on purpose: it holds what survives
/// a round trip through every format Adawat writes.</summary>
public sealed class Doc
{
    public string Title { get; set; } = "Untitled";
    public List<Block> Blocks { get; } = [];

    public string PlainText => string.Join(Environment.NewLine + Environment.NewLine, Blocks.Select(block => block.Text).Where(text => text.Length > 0));

    public static Doc FromText(string text, string title = "Untitled")
    {
        var doc = new Doc { Title = title };
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n"))
        {
            var lines = paragraph.Trim('\n');
            if (lines.Length == 0) continue;
            doc.Blocks.Add(new Paragraph([new Span(lines)]));
        }
        return doc;
    }
}

public abstract record Block
{
    public abstract string Text { get; }
}

public sealed record Span(string Text, bool Bold = false, bool Italic = false, bool Underline = false, string? Link = null, bool Code = false);

public enum Align { Start, Center, End, Justify }

public sealed record Paragraph(List<Span> Spans, Align Align = Align.Start) : Block
{
    public override string Text => string.Concat(Spans.Select(s => s.Text));
}

public sealed record Heading(int Level, List<Span> Spans) : Block
{
    public override string Text => string.Concat(Spans.Select(s => s.Text));
}

public sealed record ListItem(List<Span> Spans, bool Ordered, int Depth = 0) : Block
{
    public override string Text => string.Concat(Spans.Select(s => s.Text));
}

public sealed record Table(List<List<List<Span>>> Rows, bool HeaderRow = true) : Block
{
    public override string Text => string.Join("\n", Rows.Select(row => string.Join("\t", row.Select(cell => string.Concat(cell.Select(s => s.Text))))));
}

public sealed record Picture(byte[] Data, string Extension, int PixelWidth = 0, int PixelHeight = 0, string Alt = "") : Block
{
    public override string Text => "";
    public string Mime => Extension switch { "png" => "image/png", "gif" => "image/gif", "webp" => "image/webp", "svg" => "image/svg+xml", _ => "image/jpeg" };
}

public sealed record CodeBlock(string Code) : Block
{
    public override string Text => Code;
}

public sealed record Quote(List<Span> Spans) : Block
{
    public override string Text => string.Concat(Spans.Select(s => s.Text));
}

public sealed record PageBreak : Block
{
    public override string Text => "";
}

/// <summary>Arabic and Hebrew paragraphs read right to left; this decides from the first strong letter.</summary>
public static class Bidi
{
    public static bool IsRightToLeft(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= 'ﻼ') return true;
            if (char.IsLetter(c)) return false;
        }
        return false;
    }
}

/// <summary>Writes a <see cref="Doc"/> as HTML, Markdown or plain text.</summary>
public static class DocText
{
    public static string Html(Doc doc, bool forPrint = false, Func<Picture, string>? pictureSource = null)
    {
        var html = new StringBuilder();
        var rtl = Bidi.IsRightToLeft(doc.PlainText);
        html.Append($"<!DOCTYPE html>\n<html lang=\"{(rtl ? "ar" : "en")}\" dir=\"{(rtl ? "rtl" : "ltr")}\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append($"<title>{Escape(doc.Title)}</title>\n<style>\n{Css(forPrint)}\n</style>\n</head>\n<body>\n");
        Body(doc, html, pictureSource);
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    public static string Css(bool forPrint) => (forPrint ? "@page { size: Letter; margin: 1in; }\n" : "body { max-width: 46em; margin: 2em auto; padding: 0 1em; }\n") +
        "body { font-family: 'Segoe UI', 'Noto Naskh Arabic', 'Arial', sans-serif; font-size: 11pt; line-height: 1.55; color: #1b1b1b; }\n" +
        "h1, h2, h3, h4 { line-height: 1.25; margin: 1.1em 0 0.4em; }\n" +
        "table { border-collapse: collapse; margin: 0.8em 0; } td, th { border: 1px solid #bbb; padding: 4px 8px; vertical-align: top; }\n" +
        "th { background: #f1f1f4; } img { max-width: 100%; height: auto; } pre, code { font-family: Consolas, monospace; font-size: 0.92em; }\n" +
        "pre { background: #f5f5f7; padding: 0.8em; overflow: auto; white-space: pre-wrap; } blockquote { border-inline-start: 3px solid #ccc; margin: 0.8em 0; padding-inline-start: 1em; color: #555; }\n" +
        ".break { page-break-after: always; break-after: page; }";

    public static void Body(Doc doc, StringBuilder html, Func<Picture, string>? pictureSource = null)
    {
        var openList = (bool?)null;
        foreach (var block in doc.Blocks)
        {
            if (block is ListItem item)
            {
                if (openList != item.Ordered)
                {
                    if (openList != null) html.Append(openList == true ? "</ol>\n" : "</ul>\n");
                    html.Append(item.Ordered ? "<ol>\n" : "<ul>\n");
                    openList = item.Ordered;
                }
                html.Append($"<li{Dir(item.Text)}>{Inline(item.Spans)}</li>\n");
                continue;
            }
            if (openList != null) { html.Append(openList == true ? "</ol>\n" : "</ul>\n"); openList = null; }
            switch (block)
            {
                case Heading h:
                    var level = Math.Clamp(h.Level, 1, 6);
                    html.Append($"<h{level}{Dir(h.Text)}>{Inline(h.Spans)}</h{level}>\n");
                    break;
                case Paragraph p:
                    var align = p.Align switch { Align.Center => " style=\"text-align:center\"", Align.End => " style=\"text-align:end\"", Align.Justify => " style=\"text-align:justify\"", _ => "" };
                    html.Append($"<p{Dir(p.Text)}{align}>{Inline(p.Spans).Replace("\n", "<br>")}</p>\n");
                    break;
                case Table t:
                    html.Append("<table>\n");
                    for (var r = 0; r < t.Rows.Count; r++)
                    {
                        var tag = r == 0 && t.HeaderRow ? "th" : "td";
                        html.Append("<tr>").Append(string.Concat(t.Rows[r].Select(cell => $"<{tag}{Dir(string.Concat(cell.Select(s => s.Text)))}>{Inline(cell)}</{tag}>"))).Append("</tr>\n");
                    }
                    html.Append("</table>\n");
                    break;
                case Picture pic:
                    var src = pictureSource?.Invoke(pic) ?? $"data:{pic.Mime};base64,{Convert.ToBase64String(pic.Data)}";
                    html.Append($"<p><img src=\"{src}\" alt=\"{Escape(pic.Alt)}\"></p>\n");
                    break;
                case CodeBlock code:
                    html.Append($"<pre><code>{Escape(code.Code)}</code></pre>\n");
                    break;
                case Quote quote:
                    html.Append($"<blockquote{Dir(quote.Text)}>{Inline(quote.Spans)}</blockquote>\n");
                    break;
                case PageBreak:
                    html.Append("<div class=\"break\"></div>\n");
                    break;
            }
        }
        if (openList != null) html.Append(openList == true ? "</ol>\n" : "</ul>\n");
    }

    private static string Dir(string text) => Bidi.IsRightToLeft(text) ? " dir=\"rtl\"" : "";

    public static string Inline(IEnumerable<Span> spans)
    {
        var html = new StringBuilder();
        foreach (var span in spans)
        {
            var text = Escape(span.Text);
            if (span.Code) text = $"<code>{text}</code>";
            if (span.Bold) text = $"<strong>{text}</strong>";
            if (span.Italic) text = $"<em>{text}</em>";
            if (span.Underline) text = $"<u>{text}</u>";
            if (span.Link != null) text = $"<a href=\"{Escape(span.Link)}\">{text}</a>";
            html.Append(text);
        }
        return html.ToString();
    }

    public static string Escape(string text) => WebUtility.HtmlEncode(text);

    public static string Markdown(Doc doc)
    {
        var md = new StringBuilder();
        var number = 0;
        foreach (var block in doc.Blocks)
        {
            if (block is not ListItem) number = 0;
            switch (block)
            {
                case Heading h: md.Append(new string('#', Math.Clamp(h.Level, 1, 6))).Append(' ').Append(MdInline(h.Spans)).Append("\n\n"); break;
                case Paragraph p: md.Append(MdInline(p.Spans).Replace("\n", "  \n")).Append("\n\n"); break;
                case ListItem item:
                    md.Append(new string(' ', item.Depth * 2)).Append(item.Ordered ? $"{++number}. " : "- ").Append(MdInline(item.Spans)).Append('\n');
                    break;
                case Table t:
                    for (var r = 0; r < t.Rows.Count; r++)
                    {
                        md.Append("| ").Append(string.Join(" | ", t.Rows[r].Select(cell => MdInline(cell).Replace("|", "\\|")))).Append(" |\n");
                        if (r == 0) md.Append('|').Append(string.Concat(t.Rows[0].Select(_ => " --- |"))).Append('\n');
                    }
                    md.Append('\n');
                    break;
                case Picture pic: md.Append($"![{pic.Alt}](data:{pic.Mime};base64,{Convert.ToBase64String(pic.Data)})\n\n"); break;
                case CodeBlock code: md.Append("```\n").Append(code.Code.TrimEnd()).Append("\n```\n\n"); break;
                case Quote quote: md.Append("> ").Append(MdInline(quote.Spans)).Append("\n\n"); break;
                case PageBreak: md.Append("\n---\n\n"); break;
            }
            if (block is ListItem) continue;
        }
        return md.ToString().TrimEnd() + "\n";
    }

    private static string MdInline(IEnumerable<Span> spans) => string.Concat(spans.Select(span =>
    {
        if (span.Text.Length == 0) return "";
        var text = span.Code ? $"`{span.Text}`" : span.Text.Replace("*", "\\*").Replace("_", "\\_");
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return text;
        var lead = text[..text.IndexOf(trimmed[0])];
        var trail = text[(text.LastIndexOf(trimmed[^1]) + 1)..];
        if (span.Bold) trimmed = $"**{trimmed}**";
        if (span.Italic) trimmed = $"*{trimmed}*";
        if (span.Link != null) trimmed = $"[{trimmed}]({span.Link})";
        return lead + trimmed + trail;
    }));
}
