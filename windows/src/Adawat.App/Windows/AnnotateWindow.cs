using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Adawat.Core;
using Adawat.Core.Images;

namespace Adawat.App.Windows;

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
    public BitmapSource? Image { get; set; }

    public Rect Bounds(double aspect) => Tool switch
    {
        AnnotationTool.Draw when Points.Count > 0 => Inflate(new Rect(new Point(Points.Min(p => p.X), Points.Min(p => p.Y)), new Point(Points.Max(p => p.X), Points.Max(p => p.Y))), 0.015),
        AnnotationTool.Text => new Rect(A.X, A.Y, Math.Min(Math.Max(4, Text.Length) * 0.028 / Math.Max(aspect, 0.3), 1 - A.X), 0.07),
        AnnotationTool.Callout => new Rect(A.X - 0.035, A.Y - 0.035 * aspect, 0.07, 0.07 * aspect),
        AnnotationTool.Magnifier => new Rect(A.X - B.X, A.Y - B.X * aspect, B.X * 2, B.X * 2 * aspect),
        _ => Inflate(new Rect(A, B), 0.012),
    };

    private static Rect Inflate(Rect r, double by) { r.Inflate(by, by); return r; }

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
    public static void Draw(DrawingContext dc, IReadOnlyList<Annotation> annotations, Size size, BitmapSource? baseImage)
    {
        var shortSide = Math.Min(size.Width, size.Height);
        var line = Math.Max(2, shortSide * 0.007);
        Point P(Point p) => new(p.X * size.Width, p.Y * size.Height);
        Rect R(Point a, Point b) => new(P(a), P(b));
        foreach (var annotation in annotations)
        {
            var brush = new SolidColorBrush(annotation.Color);
            var pen = new Pen(brush, line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
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
                    dc.DrawLine(new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat }, from, basePoint);
                    var arrow = new StreamGeometry();
                    using (var g = arrow.Open())
                    {
                        g.BeginFigure(to, true, true);
                        g.LineTo(new Point(to.X - Math.Cos(angle - 0.45) * head, to.Y - Math.Sin(angle - 0.45) * head), true, true);
                        g.LineTo(new Point(to.X - Math.Cos(angle + 0.45) * head, to.Y - Math.Sin(angle + 0.45) * head), true, true);
                    }
                    dc.DrawGeometry(brush, null, arrow);
                    break;
                }
                case AnnotationTool.Highlight:
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(97, annotation.Color.R, annotation.Color.G, annotation.Color.B)), null, R(annotation.A, annotation.B));
                    break;
                case AnnotationTool.Rectangle:
                    dc.DrawRoundedRectangle(null, pen, R(annotation.A, annotation.B), line, line);
                    break;
                case AnnotationTool.Ellipse:
                {
                    var r = R(annotation.A, annotation.B);
                    dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                    break;
                }
                case AnnotationTool.Draw when annotation.Points.Count > 1:
                {
                    var stroke = new StreamGeometry();
                    using (var g = stroke.Open())
                    {
                        g.BeginFigure(P(annotation.Points[0]), false, false);
                        g.PolyLineTo(annotation.Points.Skip(1).Select(P).ToList(), true, true);
                    }
                    dc.DrawGeometry(null, pen, stroke);
                    break;
                }
                case AnnotationTool.Text when annotation.Text.Length > 0:
                {
                    var text = new FormattedText(annotation.Text, CultureInfo.CurrentUICulture,
                        Core.Documents.Bidi.IsRightToLeft(annotation.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), shortSide * 0.05, brush, 1.0)
                    { MaxTextWidth = Math.Max(shortSide * 0.1, size.Width - P(annotation.A).X) };
                    var origin = P(annotation.A);
                    if (text.FlowDirection == FlowDirection.RightToLeft) origin.X += text.Width;
                    dc.DrawText(text, origin);
                    break;
                }
                case AnnotationTool.Callout:
                {
                    var radius = shortSide * 0.032;
                    var c = P(annotation.A);
                    dc.DrawEllipse(brush, null, c, radius, radius);
                    var light = annotation.Color.R + annotation.Color.G + annotation.Color.B > 2.4 * 255;
                    var number = new FormattedText(annotation.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), radius * 1.15, light ? Brushes.Black : Brushes.White, 1.0);
                    dc.DrawText(number, new Point(c.X - number.Width / 2, c.Y - number.Height / 2));
                    break;
                }
                case AnnotationTool.Magnifier when baseImage != null:
                {
                    var c = P(annotation.A);
                    var radius = annotation.B.X * size.Width;
                    var circle = new EllipseGeometry(c, radius, radius);
                    dc.PushClip(circle);
                    // Twice the size, centred on the same spot.
                    dc.DrawImage(baseImage, new Rect(c.X - c.X * 2, c.Y - c.Y * 2, size.Width * 2, size.Height * 2));
                    dc.Pop();
                    dc.DrawGeometry(null, new Pen(Brushes.White, line * 1.2), circle);
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), line * 0.4), new EllipseGeometry(c, radius + line * 0.8, radius + line * 0.8));
                    break;
                }
                case AnnotationTool.Image when annotation.Image != null:
                    dc.DrawImage(annotation.Image, R(annotation.A, annotation.B));
                    break;
            }
        }
    }

    /// <summary>The full-size photo with its annotations burned in, as PNG bytes. Runs on the UI thread.</summary>
    public static byte[] Render(BitmapSource photo, IReadOnlyList<Annotation> annotations)
    {
        var size = new Size(photo.PixelWidth, photo.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(photo, new Rect(size));
            Draw(dc, annotations, size, photo);
        }
        var bitmap = new RenderTargetBitmap(photo.PixelWidth, photo.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}

/// <summary>The annotation canvas: the photo and its marks, drawn together, with the mouse adding and moving marks.</summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    public BitmapSource? Photo { get; set; }
    public List<Annotation> Annotations { get; } = [];
    public Annotation? Selected { get; set; }
    public AnnotationTool Tool { get; set; } = AnnotationTool.Arrow;
    public Color Color { get; set; } = Color.FromRgb(0xED, 0x42, 0x36);
    public event Action? SelectionChanged;
    private Point _down;
    private Annotation? _moving;
    private Annotation? _drawing;

    private double Aspect => Photo == null ? 1 : (double)Photo.PixelWidth / Photo.PixelHeight;

    public AnnotationCanvas()
    {
        Cursor = Cursors.Cross;
        Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.25 };
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Photo == null) return new Size(0, 0);
        var scale = Math.Min(available.Width / Photo.PixelWidth, available.Height / Photo.PixelHeight);
        if (double.IsInfinity(scale)) scale = 1;
        return new Size(Photo.PixelWidth * scale, Photo.PixelHeight * scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Photo == null) return;
        var size = RenderSize;
        dc.DrawImage(Photo, new Rect(size));
        AnnotationRenderer.Draw(dc, Annotations, size, Photo);
        if (Selected != null)
        {
            var b = Selected.Bounds(Aspect);
            dc.DrawRectangle(null, new Pen(Theme.Accent, 1.5) { DashStyle = DashStyles.Dash }, new Rect(b.X * size.Width, b.Y * size.Height, b.Width * size.Width, b.Height * size.Height));
        }
    }

    private Point Unit(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Point(Math.Clamp(p.X / RenderSize.Width, 0, 1), Math.Clamp(p.Y / RenderSize.Height, 0, 1));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
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
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
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
                case AnnotationTool.Magnifier: _drawing.B = new Point(Math.Max(0.03, (p - _drawing.A).Length), 0); break;
                case AnnotationTool.Callout or AnnotationTool.Text: _drawing.A = p; break;
                default: _drawing.B = p; break;
            }
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drawing is { } d)
        {
            var tiny = d.Tool is AnnotationTool.Arrow or AnnotationTool.Highlight or AnnotationTool.Rectangle or AnnotationTool.Ellipse && (d.B - d.A).Length < 0.01;
            if (d.Tool == AnnotationTool.Magnifier && d.B.X < 0.03) d.B = new Point(0.12, 0);
            if (tiny) { Annotations.Remove(d); Selected = null; }
        }
        _drawing = _moving = null;
        ReleaseMouseCapture();
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
        var canvas = new AnnotationCanvas { Photo = Pictures.ToBitmap(preview), Focusable = true };
        var stage = new Grid { Height = 420, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20)), FlowDirection = FlowDirection.LeftToRight };
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        canvas.Margin = new Thickness(8);
        stage.Children.Add(canvas);
        var hint = UI.Caption(Tools[0].Hint);
        var textBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        textBox.TextChanged += (_, _) => { if (canvas.Selected is { Tool: AnnotationTool.Text } t) { t.Text = textBox.Text; canvas.InvalidateVisual(); } };
        canvas.SelectionChanged += () =>
        {
            var isText = canvas.Selected is { Tool: AnnotationTool.Text };
            textBox.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
            if (isText && textBox.Text != canvas.Selected!.Text) { textBox.Text = canvas.Selected.Text; textBox.Focus(); }
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var toolButtons = new List<Border>();
        void PickTool(int index)
        {
            canvas.Tool = Tools[index].Tool;
            hint.Text = L10n.T(Tools[index].Hint);
            for (var i = 0; i < toolButtons.Count; i++) toolButtons[i].Background = i == index ? Theme.Accent : Theme.ButtonFill;
            for (var i = 0; i < toolButtons.Count; i++) ((TextBlock)toolButtons[i].Child).Foreground = i == index ? Brushes.White : Theme.Ink;
            if (Tools[index].Tool == AnnotationTool.Image) AddImage(canvas, null);
        }
        for (var i = 0; i < Tools.Length; i++)
        {
            var index = i;
            var button = new Border { Width = 34, Height = 28, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, ToolTip = L10n.T(Tools[i].Title),
                                      Child = UI.Icon(Tools[i].Glyph, 14) };
            ((TextBlock)button.Child).HorizontalAlignment = HorizontalAlignment.Center;
            button.MouseLeftButtonUp += (_, _) => PickTool(index);
            toolButtons.Add(button);
            toolbar.Children.Add(button);
        }
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var dots = new List<Border>();
        foreach (var color in Palette)
        {
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                canvas.Color = color;
                if (canvas.Selected != null) { canvas.Selected.Color = color; canvas.InvalidateVisual(); }
                foreach (var d in dots) d.BorderBrush = d == dot ? Theme.Accent : Theme.Hairline;
                foreach (var d in dots) d.BorderThickness = new Thickness(d == dot ? 2.5 : 1);
            };
            dots.Add(dot);
            colors.Children.Add(dot);
        }
        colors.Children.Add(UI.IconButton("", () => { if (canvas.Annotations.Count > 0) { canvas.Annotations.RemoveAt(canvas.Annotations.Count - 1); canvas.Selected = null; canvas.InvalidateVisual(); } }, "Undo last annotation").Pad(12, 0));
        colors.Children.Add(UI.IconButton("", () => { if (canvas.Selected != null) { canvas.Annotations.Remove(canvas.Selected); canvas.Selected = null; canvas.InvalidateVisual(); } }, "Delete selected annotation"));
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && System.Windows.Clipboard.ContainsImage()) AddImage(canvas, System.Windows.Clipboard.GetImage());
            if (e.Key == Key.Delete && !textBox.IsKeyboardFocused && canvas.Selected != null) { canvas.Annotations.Remove(canvas.Selected); canvas.Selected = null; canvas.InvalidateVisual(); }
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
                var png = Application.Current.Dispatcher.Invoke(() => AnnotationRenderer.Render(Pictures.FromBytes(full)!, marks));
                var format = ImageOps.EditedFormat(f[0]);
                var output = Output.PathFor(f[0], format.Ext, "annotated");
                await Task.Run(() => { using var image = new ImageMagick.MagickImage(png); ImageEngine.Save(image, format, output, 0.92); });
                return [output];
            });
        }, primary: true));
        PickTool(0);
        dots[0].BorderBrush = Theme.Accent;
        dots[0].BorderThickness = new Thickness(2.5);
        return window;
    }

    private static void AddImage(AnnotationCanvas canvas, BitmapSource? image)
    {
        if (image == null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp" };
            if (dialog.ShowDialog() != true) return;
            image = new BitmapImage(new Uri(dialog.FileName));
        }
        var aspect = canvas.Photo == null ? 1 : (double)canvas.Photo.PixelWidth / canvas.Photo.PixelHeight;
        var w = 0.3;
        var h = w * aspect * image.PixelHeight / Math.Max(1, image.PixelWidth);
        canvas.Annotations.Add(new Annotation { Tool = AnnotationTool.Image, Image = image, A = new Point(0.35, 0.35), B = new Point(0.35 + w, 0.35 + h) });
        canvas.InvalidateVisual();
    }
}
