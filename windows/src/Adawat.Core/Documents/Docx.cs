using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Adawat.Core.Documents;

/// <summary>Word documents: read into a <see cref="Doc"/>, and written with real heading styles, lists, tables, pictures
/// and right-to-left paragraphs for Arabic.</summary>
public static class Docx
{
    public static Doc Read(string path)
    {
        using var word = WordprocessingDocument.Open(path, false);
        var main = word.MainDocumentPart ?? throw new AdawatException($"{Path.GetFileName(path)} could not be read as a document");
        var doc = new Doc { Title = word.PackageProperties.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(path) };
        var body = main.Document?.Body;
        if (body == null) return doc;
        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Paragraph p: ReadParagraph(p, main, doc); break;
                case DocumentFormat.OpenXml.Wordprocessing.Table table:
                    var rows = table.Elements<TableRow>().Select(row => row.Elements<TableCell>()
                        .Select(cell => cell.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().SelectMany((para, i) =>
                            (i > 0 ? [new Span("\n")] : Array.Empty<Span>()).Concat(Runs(para, main))).ToList()).ToList()).ToList();
                    if (rows.Count > 0) doc.Blocks.Add(new Table(rows));
                    break;
            }
        }
        return doc;
    }

    private static void ReadParagraph(DocumentFormat.OpenXml.Wordprocessing.Paragraph p, MainDocumentPart main, Doc doc)
    {
        foreach (var drawing in p.Descendants<Drawing>())
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
            if (blip != null && main.GetPartById(blip) is ImagePart image)
            {
                using var stream = image.GetStream();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var ext = image.ContentType.Contains("png") ? "png" : image.ContentType.Contains("gif") ? "gif" : "jpg";
                doc.Blocks.Add(new Picture(memory.ToArray(), ext));
            }
        }
        if (p.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page)) doc.Blocks.Add(new PageBreak());
        var spans = Runs(p, main);
        if (spans.Count == 0 || spans.All(s => s.Text.Trim().Length == 0)) return;
        var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var outline = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(style[7..], out var level))
            doc.Blocks.Add(new Heading(level, spans));
        else if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
            doc.Blocks.Add(new Heading(1, spans));
        else if (outline is { } o && o < 6)
            doc.Blocks.Add(new Heading(o + 1, spans));
        else if (p.ParagraphProperties?.NumberingProperties is { } numbering)
        {
            var depth = numbering.NumberingLevelReference?.Val?.Value ?? 0;
            var ordered = IsOrdered(main, numbering.NumberingId?.Val?.Value ?? 0, depth);
            doc.Blocks.Add(new ListItem(spans, ordered, depth));
        }
        else
        {
            var justification = p.ParagraphProperties?.Justification?.Val?.Value;
            var align = justification == JustificationValues.Center ? Align.Center
                : justification == JustificationValues.Both ? Align.Justify
                : justification == JustificationValues.Right || justification == JustificationValues.End ? Align.End : Align.Start;
            doc.Blocks.Add(new Paragraph(spans, align));
        }
    }

    private static bool IsOrdered(MainDocumentPart main, int numberingId, int level)
    {
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numberingId);
        var abstractId = instance?.AbstractNumId?.Val?.Value;
        var definition = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
        var format = definition?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level)?.NumberingFormat?.Val?.Value;
        return format != null && format != NumberFormatValues.Bullet;
    }

    private static List<Span> Runs(OpenXmlElement container, MainDocumentPart main)
    {
        var spans = new List<Span>();
        // Only this paragraph's own runs: text boxes inside it hold paragraphs of their own.
        foreach (var run in container.Descendants<Run>().Where(r => r.Ancestors<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().FirstOrDefault() == container))
        {
            var text = string.Concat(run.ChildElements.Select(child => child switch
            {
                Text t => t.Text,
                TabChar => "\t",
                Break b when b.Type == null || b.Type.Value == BreakValues.TextWrapping => "\n",
                _ => "",
            }));
            if (text.Length == 0) continue;
            var props = run.RunProperties;
            string? link = null;
            if (run.Parent is Hyperlink hyperlink && hyperlink.Id?.Value is { } id)
                link = main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri.ToString();
            spans.Add(new Span(text, Bold: props?.Bold != null && props.Bold.Val?.Value != false, Italic: props?.Italic != null && props.Italic.Val?.Value != false,
                               Underline: props?.Underline?.Val?.Value is { } u && u != UnderlineValues.None, Link: link,
                               Code: props?.RunFonts?.Ascii?.Value is "Consolas" or "Courier New"));
        }
        return spans;
    }

    // MARK: Writing

    public static void Write(Doc doc, string path)
    {
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        word.PackageProperties.Title = doc.Title;
        word.PackageProperties.Creator = "Adawat";
        var main = word.AddMainDocumentPart();
        AddStyles(main);
        var numbering = AddNumbering(main);
        var body = new Body();
        uint pictureId = 1;
        foreach (var block in doc.Blocks)
        {
            switch (block)
            {
                case Heading h: body.Append(Para(h.Spans, main, $"Heading{Math.Clamp(h.Level, 1, 6)}", rtl: Bidi.IsRightToLeft(h.Text))); break;
                case Paragraph p: body.Append(Para(p.Spans, main, null, p.Align, Bidi.IsRightToLeft(p.Text))); break;
                case Quote q: body.Append(Para(q.Spans, main, "Quote", rtl: Bidi.IsRightToLeft(q.Text))); break;
                case ListItem item:
                    var list = Para(item.Spans, main, "ListParagraph", rtl: Bidi.IsRightToLeft(item.Text));
                    list.ParagraphProperties!.NumberingProperties = new NumberingProperties(
                        new NumberingLevelReference { Val = Math.Min(item.Depth, 8) }, new NumberingId { Val = item.Ordered ? numbering.Ordered : numbering.Bullets });
                    body.Append(list);
                    break;
                case CodeBlock code:
                    foreach (var line in code.Code.TrimEnd().Split('\n')) body.Append(Para([new Span(line.TrimEnd('\r'), Code: true)], main, "Code"));
                    break;
                case Table t: body.Append(TableOf(t, main)); break;
                case Picture pic: body.Append(PictureParagraph(pic, main, pictureId++)); break;
                case PageBreak: body.Append(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new Run(new Break { Type = BreakValues.Page }))); break;
            }
        }
        body.Append(new SectionProperties(new PageSize { Width = 12240, Height = 15840 },
                                          new PageMargin { Top = 1440, Bottom = 1440, Left = 1440U, Right = 1440U, Header = 720U, Footer = 720U }));
        main.Document = new Document(body);
        main.Document.Save();
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph Para(List<Span> spans, MainDocumentPart main, string? style, Align align = Align.Start, bool rtl = false)
    {
        var props = new ParagraphProperties();
        if (style != null) props.ParagraphStyleId = new ParagraphStyleId { Val = style };
        if (rtl) props.BiDi = new BiDi();
        if (align != Align.Start)
            props.Justification = new Justification { Val = align switch { Align.Center => JustificationValues.Center, Align.Justify => JustificationValues.Both, _ => rtl ? JustificationValues.Left : JustificationValues.Right } };
        var paragraph = new DocumentFormat.OpenXml.Wordprocessing.Paragraph(props);
        foreach (var span in spans)
        {
            var parts = span.Text.Split('\n');
            var run = new Run();
            var runProps = new RunProperties();
            if (span.Bold) runProps.Append(new Bold(), new BoldComplexScript());
            if (span.Italic) runProps.Append(new Italic(), new ItalicComplexScript());
            if (span.Underline) runProps.Append(new Underline { Val = UnderlineValues.Single });
            if (span.Code) runProps.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            if (rtl || Bidi.IsRightToLeft(span.Text)) runProps.Append(new RightToLeftText());
            if (span.Link != null) runProps.Append(new RunStyle { Val = "Hyperlink" });
            if (runProps.HasChildren) run.Append(runProps);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) run.Append(new Break());
                run.Append(new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
            }
            if (span.Link != null && Uri.TryCreate(span.Link, UriKind.Absolute, out var uri))
            {
                var relation = main.AddHyperlinkRelationship(uri, true);
                paragraph.Append(new Hyperlink(run) { Id = relation.Id });
            }
            else paragraph.Append(run);
        }
        return paragraph;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Table TableOf(Table t, MainDocumentPart main)
    {
        var table = new DocumentFormat.OpenXml.Wordprocessing.Table(new TableProperties(
            new TableStyle { Val = "TableGrid" }, new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 },
                             new LeftBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                             new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        for (var r = 0; r < t.Rows.Count; r++)
        {
            var row = new TableRow();
            foreach (var cell in t.Rows[r])
            {
                var spans = r == 0 && t.HeaderRow ? cell.Select(s => s with { Bold = true }).ToList() : cell;
                row.Append(new TableCell(Para(spans, main, null, rtl: Bidi.IsRightToLeft(string.Concat(cell.Select(s => s.Text))))));
            }
            table.Append(row);
        }
        return table;
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Paragraph PictureParagraph(Picture pic, MainDocumentPart main, uint id)
    {
        var type = pic.Extension switch { "png" => ImagePartType.Png, "gif" => ImagePartType.Gif, _ => ImagePartType.Jpeg };
        var data = pic.Data;
        if (pic.Extension is "webp" or "svg" or "heic" or "avif")
        {
            using var image = new ImageMagick.MagickImage(pic.Data);
            data = image.ToByteArray(ImageMagick.MagickFormat.Png);
            type = ImagePartType.Png;
        }
        var part = main.AddImagePart(type);
        using (var stream = new MemoryStream(data)) part.FeedData(stream);
        var (w, h) = (pic.PixelWidth, pic.PixelHeight);
        if (w <= 0 || h <= 0)
        {
            var info = new ImageMagick.MagickImageInfo(data);
            (w, h) = ((int)info.Width, (int)info.Height);
        }
        // Fit the 6.5-inch text column at 96 dpi.
        const long emuPerPixel = 9525, maxWidth = 6 * 914400 + 457200;
        long cx = w * emuPerPixel, cy = h * emuPerPixel;
        if (cx > maxWidth) { cy = cy * maxWidth / cx; cx = maxWidth; }
        var relation = main.GetIdOfPart(part);
        var graphic = new A.Graphic(new A.GraphicData(
            new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = $"Picture {id}" }, new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed = relation }, new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });
        var inline = new DW.Inline(new DW.Extent { Cx = cx, Cy = cy }, new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                                   new DW.DocProperties { Id = id, Name = $"Picture {id}", Description = pic.Alt },
                                   new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }), graphic)
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 };
        return new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new Run(new Drawing(inline)));
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = new Styles(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Arial" },
                                                                 new FontSize { Val = "22" }, new FontSizeComplexScript { Val = "22" })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));
        styles.Append(new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });
        int[] sizes = [40, 32, 28, 26, 24, 22];
        for (var level = 1; level <= 6; level++)
        {
            styles.Append(new Style(new StyleName { Val = $"heading {level}" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "80" }, new OutlineLevel { Val = level - 1 }),
                new StyleRunProperties(new Bold(), new BoldComplexScript(), new FontSize { Val = sizes[level - 1].ToString() }, new FontSizeComplexScript { Val = sizes[level - 1].ToString() },
                                       new Color { Val = "1F1F2E" }))
            { Type = StyleValues.Paragraph, StyleId = $"Heading{level}" });
        }
        styles.Append(new Style(new StyleName { Val = "List Paragraph" }, new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new Indentation { Left = "720" }, new ContextualSpacing())) { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });
        styles.Append(new Style(new StyleName { Val = "Quote" }, new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new Indentation { Left = "720" }), new StyleRunProperties(new Italic(), new Color { Val = "555555" })) { Type = StyleValues.Paragraph, StyleId = "Quote" });
        styles.Append(new Style(new StyleName { Val = "Code" }, new BasedOn { Val = "Normal" },
            new StyleParagraphProperties(new SpacingBetweenLines { After = "0" }), new StyleRunProperties(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" }, new FontSize { Val = "20" }))
        { Type = StyleValues.Paragraph, StyleId = "Code" });
        styles.Append(new Style(new StyleName { Val = "Hyperlink" }, new StyleRunProperties(new Color { Val = "2F5BD6" }, new Underline { Val = UnderlineValues.Single }))
        { Type = StyleValues.Character, StyleId = "Hyperlink" });
        styles.Append(new Style(new StyleName { Val = "Table Grid" }) { Type = StyleValues.Table, StyleId = "TableGrid" });
        main.AddNewPart<StyleDefinitionsPart>().Styles = styles;
    }

    private static (int Bullets, int Ordered) AddNumbering(MainDocumentPart main)
    {
        AbstractNum Definition(int id, bool ordered)
        {
            var definition = new AbstractNum { AbstractNumberId = id };
            for (var level = 0; level < 9; level++)
            {
                definition.Append(new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = ordered ? NumberFormatValues.Decimal : NumberFormatValues.Bullet },
                    new LevelText { Val = ordered ? $"%{level + 1}." : (level % 2 == 0 ? "•" : "◦") },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new PreviousParagraphProperties(new Indentation { Left = (720 + 360 * level).ToString(), Hanging = "360" })) { LevelIndex = level });
            }
            return definition;
        }
        var numbering = new Numbering(Definition(0, false), Definition(1, true),
                                      new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 },
                                      new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 2 });
        main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;
        return (1, 2);
    }
}
