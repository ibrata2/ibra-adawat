using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adawat.Core;
using Adawat.Core.Images;

namespace Adawat.Linux.Windows;

public enum AnnotationTool { Arrow, Highlight, Text, Rectangle, Ellipse, Draw, Callout, Magnifier, Image }

/// <summary>A mark on the picture. Positions are shares (0…1) with a top-left origin, so the same marks draw on the preview and on the full-size photo.</summary>
public sealed class Annotation
{
    public required AnnotationTool Tool { get; init; }
    public Color Color { get; set; }
    public Point A { get; set; }
    public Point B { get; set; }
    public List<Point> Points { get; } = [];
    public string Text { get; set; } = "";
    public int Number { get; set; }
    public Bitmap? Image { get; set; }

    public Rect Bounds(double aspect) => Tool switch
    {
        AnnotationTool.Draw when Points.Count > 0 => new Rect(new Point(Points.Min(p => p.X), Points.Min(p => p.Y)), new Point(Points.Max(p => p.X), Points.Max(p => p.Y))).Inflate(0.015),
        AnnotationTool.Text => new Rect(A.X, A.Y, Math.Min(Math.Max(4, Text.Length) * 0.028 / Math.Max(aspect, 0.3), 1 - A.X), 0.07),
        AnnotationTool.Callout => new Rect(A.X - 0.035, A.Y - 0.035 * aspect, 0.07, 0.07 * aspect),
        AnnotationTool.Magnifier => new Rect(A.X - B.X, A.Y - B.X * aspect, B.X * 2, B.X * 2 * aspect),
        _ => new Rect(A, B).Normalize().Inflate(0.012),
    };

    public void Move(Vector by)
    {
        A += by;
        if (Tool is not (AnnotationTool.Magnifier or AnnotationTool.Callout or AnnotationTool.Text)) B += by;
        for (var i = 0; i < Points.Count; i++) Points[i] += by;
    }
}

/// <summary>Draws annotations at any size: the window's preview and the saved photo use the same code.</summary>
public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, IReadOnlyList<Annotation> annotations, Size size, Bitmap? baseImage)
    {
        var shortSide = Math.Min(size.Width, size.Height);
        var line = Math.Max(2, shortSide * 0.007);
        Point P(Point p) => new(p.X * size.Width, p.Y * size.Height);
        Rect R(Point a, Point b) => new Rect(P(a), P(b)).Normalize();
        foreach (var annotation in annotations)
        {
            var brush = new SolidColorBrush(annotation.Color);
            var pen = new Pen(brush, line) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            switch (annotation.Tool)
            {
                case AnnotationTool.Arrow:
                {
                    var from = P(annotation.A);
                    var to = P(annotation.B);
                    var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
                    var width = line * 1.3;
                    var head = Math.Max(width * 4.5, 10);
                    var basePoint = new Point(to.X - Math.Cos(angle) * head * 0.8, to.Y - Math.Sin(angle) * head * 0.8);
                    dc.DrawLine(new Pen(brush, width) { LineCap = PenLineCap.Round }, from, basePoint);
                    var arrow = new StreamGeometry();
                    using (var g = arrow.Open())
                    {
                        g.BeginFigure(to, true);
                        g.LineTo(new Point(to.X - Math.Cos(angle - 0.45) * head, to.Y - Math.Sin(angle - 0.45) * head));
                        g.LineTo(new Point(to.X - Math.Cos(angle + 0.45) * head, to.Y - Math.Sin(angle + 0.45) * head));
                        g.EndFigure(true);
                    }
                    dc.DrawGeometry(brush, null, arrow);
                    break;
                }
                case AnnotationTool.Highlight:
                    dc.FillRectangle(new SolidColorBrush(Color.FromArgb(97, annotation.Color.R, annotation.Color.G, annotation.Color.B)), R(annotation.A, annotation.B));
                    break;
                case AnnotationTool.Rectangle:
                    dc.DrawRectangle(null, pen, R(annotation.A, annotation.B), line, line);
                    break;
                case AnnotationTool.Ellipse:
                {
                    var r = R(annotation.A, annotation.B);
                    dc.DrawEllipse(null, pen, r.Center, r.Width / 2, r.Height / 2);
                    break;
                }
                case AnnotationTool.Draw when annotation.Points.Count > 1:
                {
                    var stroke = new StreamGeometry();
                    using (var g = stroke.Open())
                    {
                        g.BeginFigure(P(annotation.Points[0]), false);
                        foreach (var point in annotation.Points.Skip(1)) g.LineTo(P(point));
                        g.EndFigure(false);
                    }
                    dc.DrawGeometry(null, pen, stroke);
                    break;
                }
                case AnnotationTool.Text when annotation.Text.Length > 0:
                {
                    var text = new FormattedText(annotation.Text, CultureInfo.CurrentUICulture,
                        Core.Documents.Bidi.IsRightToLeft(annotation.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        new Typeface(Look.Font, FontStyle.Normal, FontWeight.Bold), shortSide * 0.05, brush)
                    { MaxTextWidth = Math.Max(shortSide * 0.1, size.Width - P(annotation.A).X) };
                    dc.DrawText(text, P(annotation.A));
                    break;
                }
                case AnnotationTool.Callout:
                {
                    var radius = shortSide * 0.032;
                    var c = P(annotation.A);
                    dc.DrawEllipse(brush, null, c, radius, radius);
                    var light = annotation.Color.R + annotation.Color.G + annotation.Color.B > 2.4 * 255;
                    var number = new FormattedText(annotation.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(Look.Font, FontStyle.Normal, FontWeight.Bold), radius * 1.15, light ? Brushes.Black : Brushes.White);
                    dc.DrawText(number, new Point(c.X - number.Width / 2, c.Y - number.Height / 2));
                    break;
                }
                case AnnotationTool.Magnifier when baseImage != null:
                {
                    var c = P(annotation.A);
                    var radius = annotation.B.X * size.Width;
                    var circle = new EllipseGeometry(new Rect(c.X - radius, c.Y - radius, radius * 2, radius * 2));
                    using (dc.PushGeometryClip(circle))
                    {
                        // Twice the size, centred on the same spot.
                        dc.DrawImage(baseImage, new Rect(c.X - c.X * 2, c.Y - c.Y * 2, size.Width * 2, size.Height * 2));
                    }
                    dc.DrawGeometry(null, new Pen(Brushes.White, line * 1.2), circle);
                    var ring = radius + line * 0.8;
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), line * 0.4), c, ring, ring);
                    break;
                }
                case AnnotationTool.Image when annotation.Image != null:
                    dc.DrawImage(annotation.Image, R(annotation.A, annotation.B));
                    break;
            }
        }
    }

    /// <summary>The full-size photo with its annotations burned in, as PNG bytes. Runs on the UI thread.</summary>
    public static byte[] Render(Bitmap photo, IReadOnlyList<Annotation> annotations)
    {
        var size = new Size(photo.PixelSize.Width, photo.PixelSize.Height);
        using var bitmap = new RenderTargetBitmap(photo.PixelSize, new Vector(96, 96));
        using (var dc = bitmap.CreateDrawingContext())
        {
            dc.DrawImage(photo, new Rect(size));
            Draw(dc, annotations, size, photo);
        }
        using var memory = new MemoryStream();
        bitmap.Save(memory);
        return memory.ToArray();
    }
}

/// <summary>The annotation canvas: the photo and its marks, drawn together, with the mouse adding and moving marks.</summary>
public sealed class AnnotationCanvas : Control
{
    public Bitmap? Photo { get; set; }
    public List<Annotation> Annotations { get; } = [];
    public Annotation? Selected { get; set; }
    public AnnotationTool Tool { get; set; } = AnnotationTool.Arrow;
    public Color Color { get; set; } = Color.FromRgb(0xED, 0x42, 0x36);
    public event Action? SelectionChanged;
    private Point _down;
    private Annotation? _moving;
    private Annotation? _drawing;

    private double Aspect => Photo == null ? 1 : (double)Photo.PixelSize.Width / Photo.PixelSize.Height;

    public AnnotationCanvas()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Effect = new DropShadowEffect { BlurRadius = 6, OffsetX = 0, OffsetY = 1, Opacity = 0.25 };
        Focusable = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Photo == null) return new Size(0, 0);
        var scale = Math.Min(available.Width / Photo.PixelSize.Width, available.Height / Photo.PixelSize.Height);
        if (double.IsInfinity(scale)) scale = 1;
        return new Size(Photo.PixelSize.Width * scale, Photo.PixelSize.Height * scale);
    }

    public override void Render(DrawingContext dc)
    {
        if (Photo == null) return;
        var size = Bounds.Size;
        dc.DrawImage(Photo, new Rect(size));
        AnnotationRenderer.Draw(dc, Annotations, size, Photo);
        if (Selected != null)
        {
            var b = Selected.Bounds(Aspect);
            dc.DrawRectangle(null, new Pen(Look.Accent, 1.5) { DashStyle = DashStyle.Dash }, new Rect(b.X * size.Width, b.Y * size.Height, b.Width * size.Width, b.Height * size.Height));
        }
    }

    private Point Unit(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(Math.Clamp(p.X / Bounds.Width, 0, 1), Math.Clamp(p.Y / Bounds.Height, 0, 1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var p = Unit(e);
        _down = p;
        var hit = Annotations.LastOrDefault(a => a.Bounds(Aspect).Contains(p));
        if (hit != null && Tool != AnnotationTool.Draw)
        {
            Selected = _moving = hit;
            SelectionChanged?.Invoke();
        }
        else
        {
            var annotation = new Annotation { Tool = Tool, Color = Color, A = p, B = p };
            if (Tool == AnnotationTool.Draw) annotation.Points.Add(p);
            if (Tool == AnnotationTool.Callout) annotation.Number = Annotations.Count(a => a.Tool == AnnotationTool.Callout) + 1;
            if (Tool == AnnotationTool.Magnifier) annotation.B = new Point(0.0, 0);
            if (Tool == AnnotationTool.Image) return;
            Annotations.Add(annotation);
            Selected = _drawing = annotation;
            SelectionChanged?.Invoke();
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = Unit(e);
        if (_moving != null)
        {
            _moving.Move(p - _down);
            _down = p;
        }
        else if (_drawing != null)
        {
            switch (_drawing.Tool)
            {
                case AnnotationTool.Draw: _drawing.Points.Add(p); break;
                case AnnotationTool.Magnifier: _drawing.B = new Point(Math.Max(0.03, Point.Distance(p, _drawing.A)), 0); break;
                case AnnotationTool.Callout or AnnotationTool.Text: _drawing.A = p; break;
                default: _drawing.B = p; break;
            }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drawing is { } d)
        {
            var tiny = d.Tool is AnnotationTool.Arrow or AnnotationTool.Highlight or AnnotationTool.Rectangle or AnnotationTool.Ellipse && Point.Distance(d.B, d.A) < 0.01;
            if (d.Tool == AnnotationTool.Magnifier && d.B.X < 0.03) d.B = new Point(0.12, 0);
            if (tiny) { Annotations.Remove(d); Selected = null; }
        }
        _drawing = _moving = null;
        e.Pointer.Capture(null);
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }
}

/// <summary>Annotate: tools, colours, a text box for text marks, undo and delete; the saved copy has the marks burned in.</summary>
public static class AnnotateWindow
{
    private static readonly (AnnotationTool Tool, string Glyph, string Title, string Hint)[] Tools =
    [
        (AnnotationTool.Arrow, "", "Arrow", "Drag from the tail toward the arrowhead. Drag an annotation to move it."),
        (AnnotationTool.Highlight, "", "Highlight", "Drag over the image to highlight."),
        (AnnotationTool.Text, "", "Text", "Click to add a text box, then type below. Drag a box to move it."),
        (AnnotationTool.Rectangle, "", "Rectangle", "Drag on the image to draw. Drag an annotation to move it."),
        (AnnotationTool.Ellipse, "", "Ellipse", "Drag on the image to draw. Drag an annotation to move it."),
        (AnnotationTool.Draw, "", "Draw", "Drag to draw freehand. Undo removes the last stroke."),
        (AnnotationTool.Callout, "", "Numbered Callout", "Click to add numbered callouts; drag to move them. Their size stays fixed."),
        (AnnotationTool.Magnifier, "", "Magnifier", "Drag out from the spot to magnify. Drag a magnifier to move it."),
        (AnnotationTool.Image, "", "Image", "Paste an image with Ctrl+V or choose one. Drag it to move."),
    ];

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xED, 0x42, 0x36), Color.FromRgb(0xFF, 0x52, 0x00), Color.FromRgb(0xFF, 0xCC, 0x1A), Color.FromRgb(0x33, 0xB8, 0x59),
        Color.FromRgb(0x29, 0x78, 0xF5), Colors.White, Color.FromRgb(0x14, 0x14, 0x14),
    ];

    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var (preview, _, _) = Pictures.LoadPreview(file, 1600);
        var window = new ToolWindow("Annotate Photo", 720);
        var canvas = new AnnotationCanvas { Photo = Pictures.ToBitmap(preview) };
        var stage = new Panel { Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20)), FlowDirection = FlowDirection.LeftToRight };
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        canvas.Margin = new Thickness(8);
        stage.Children.Add(canvas);
        var hint = UI.Caption(Tools[0].Hint);
        var textBox = new TextBox { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        textBox.TextChanged += (_, _) => { if (canvas.Selected is { Tool: AnnotationTool.Text } t) { t.Text = textBox.Text ?? ""; canvas.InvalidateVisual(); } };
        canvas.SelectionChanged += () =>
        {
            var isText = canvas.Selected is { Tool: AnnotationTool.Text };
            textBox.IsVisible = isText;
            if (isText && textBox.Text != canvas.Selected!.Text) { textBox.Text = canvas.Selected.Text; textBox.Focus(); }
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var toolButtons = new List<Border>();
        void PickTool(int index)
        {
            canvas.Tool = Tools[index].Tool;
            hint.Text = L10n.T(Tools[index].Hint);
            for (var i = 0; i < toolButtons.Count; i++) toolButtons[i].Background = i == index ? Look.Accent : Look.ButtonFill;
            for (var i = 0; i < toolButtons.Count; i++) ((TextBlock)toolButtons[i].Child!).Foreground = i == index ? Brushes.White : Look.Ink;
            if (Tools[index].Tool == AnnotationTool.Image) _ = ChooseImage(window, canvas);
        }
        for (var i = 0; i < Tools.Length; i++)
        {
            var index = i;
            var icon = UI.Icon(Tools[i].Glyph, 14);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            var button = new Border { Width = 34, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 4, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = icon };
            ToolTip.SetTip(button, L10n.T(Tools[i].Title));
            button.PointerReleased += (_, _) => PickTool(index);
            toolButtons.Add(button);
            toolbar.Children.Add(button);
        }
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var dots = new List<Border>();
        foreach (var color in Palette)
        {
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0), BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
            dot.PointerReleased += (_, _) =>
            {
                canvas.Color = color;
                if (canvas.Selected != null) { canvas.Selected.Color = color; canvas.InvalidateVisual(); }
                foreach (var d in dots) d.BorderBrush = d == dot ? Look.Accent : Look.Hairline;
                foreach (var d in dots) d.BorderThickness = new Thickness(d == dot ? 2.5 : 1);
            };
            dots.Add(dot);
            colors.Children.Add(dot);
        }
        colors.Children.Add(UI.IconButton("", () => { if (canvas.Annotations.Count > 0) { canvas.Annotations.RemoveAt(canvas.Annotations.Count - 1); canvas.Selected = null; canvas.InvalidateVisual(); } }, "Undo last annotation").Pad(12, 0));
        colors.Children.Add(UI.IconButton("", () => { if (canvas.Selected != null) { canvas.Annotations.Remove(canvas.Selected); canvas.Selected = null; canvas.InvalidateVisual(); } }, "Delete selected annotation"));
        window.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control && window.Clipboard is { } board && await board.TryGetBitmapAsync() is { } pasted) AddImage(canvas, pasted);
            if (e.Key == Key.Delete && !textBox.IsFocused && canvas.Selected != null) { canvas.Annotations.Remove(canvas.Selected); canvas.Selected = null; canvas.InvalidateVisual(); }
        };
        var stack = new StackPanel();
        stack.Children.Add(stage);
        stack.Children.Add(toolbar);
        stack.Children.Add(colors);
        stack.Children.Add(textBox);
        stack.Children.Add(hint.Pad(0, 6));
        window.Body = stack;
        window.Leading(UI.Button("Reset", () => { canvas.Annotations.Clear(); canvas.Selected = null; canvas.InvalidateVisual(); }));
        window.Trailing(UI.Button("Save Annotated Copy", () =>
        {
            var marks = canvas.Annotations.ToList();
            window.Close();
            Runner.Run("Annotate", [file], async (f, c) =>
            {
                c.Report("Saving annotated copy");
                var full = await Task.Run(() => { using var image = ImageEngine.Load(f[0]); return image.ToByteArray(ImageMagick.MagickFormat.Png); });
                var png = await Dispatcher.UIThread.InvokeAsync(() => AnnotationRenderer.Render(Pictures.FromBytes(full)!, marks));
                var format = ImageOps.EditedFormat(f[0]);
                var output = Output.PathFor(f[0], format.Ext, "annotated");
                await Task.Run(() => { using var image = new ImageMagick.MagickImage(png); ImageEngine.Save(image, format, output, 0.92); });
                return [output];
            });
        }, primary: true));
        PickTool(0);
        dots[0].BorderBrush = Look.Accent;
        dots[0].BorderThickness = new Thickness(2.5);
        return window;
    }

    private static async Task ChooseImage(Window window, AnnotationCanvas canvas)
    {
        if (await Dialogs.OpenImage(window, "Choose an image") is not { } picked) return;
        try { AddImage(canvas, new Bitmap(picked)); }
        catch (Exception) { Hud.Shared.ShowMessage(L10n.T($"Could not read {Path.GetFileName(picked)}"), Hud.Style.Failure); }
    }

    private static void AddImage(AnnotationCanvas canvas, Bitmap image)
    {
        var aspect = canvas.Photo == null ? 1 : (double)canvas.Photo.PixelSize.Width / canvas.Photo.PixelSize.Height;
        var w = 0.3;
        var h = w * aspect * image.PixelSize.Height / Math.Max(1, image.PixelSize.Width);
        canvas.Annotations.Add(new Annotation { Tool = AnnotationTool.Image, Image = image, A = new Point(0.35, 0.35), B = new Point(0.35 + w, 0.35 + h) });
        canvas.InvalidateVisual();
    }
}
