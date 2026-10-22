using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageMagick;
using Path = Avalonia.Controls.Shapes.Path;

namespace Adawat.Linux.Windows;

/// <summary>Pictures for previews: Magick images turned into Avalonia bitmaps.</summary>
public static class Pictures
{
    public static Bitmap ToBitmap(MagickImage image)
    {
        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        return new Bitmap(stream);
    }

    public static Bitmap? FromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    /// <summary>A preview-sized copy of the photo (upright) and its full pixel size.</summary>
    public static (MagickImage Preview, int Width, int Height) LoadPreview(string path, int maxPixels = 1400)
    {
        var info = Core.Images.ImageEngine.Load(path);
        int w = (int)info.Width, h = (int)info.Height;
        if (Math.Max(w, h) > maxPixels) info.Resize(new MagickGeometry((uint)maxPixels, (uint)maxPixels));
        return (info, w, h);
    }
}

/// <summary>A rectangle in shares of a picture that can be changed in place, as the editors move and resize them.</summary>
public struct Area
{
    public double X, Y, Width, Height;

    public Area(double x, double y, double width, double height) { X = x; Y = y; Width = Math.Max(0, width); Height = Math.Max(0, height); }

    public Area(Point a, Point b) : this(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) { }

    public readonly double Left => X;
    public readonly double Top => Y;
    public readonly double Right => X + Width;
    public readonly double Bottom => Y + Height;
    public readonly Point TopLeft => new(X, Y);

    public readonly bool Contains(Point p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public readonly Area Inflated(double by) => new(X - by, Y - by, Width + by * 2, Height + by * 2);

    public readonly Rect ToRect() => new(X, Y, Width, Height);
}

/// <summary>A picture inside a fixed area, letterboxed on a dark background, with a canvas over it in picture coordinates (0…1).</summary>
public class PictureStage : Panel
{
    protected readonly Image Picture = new() { Stretch = Stretch.Uniform };
    protected readonly Canvas Overlay = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Border _frame;
    private readonly Panel _inner = new();

    public PictureStage(double height)
    {
        Height = height;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20));
        FlowDirection = FlowDirection.LeftToRight;
        _frame = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _inner.Children.Add(Picture);
        _inner.Children.Add(Overlay);
        _frame.Child = _inner;
        Children.Add(_frame);
        SizeChanged += (_, _) => Layout();
    }

    public Bitmap? Source
    {
        get => Picture.Source as Bitmap;
        set { Picture.Source = value; Layout(); }
    }

    /// <summary>The picture's displayed size; overlay coordinates use it.</summary>
    public Size Shown { get; private set; }

    private void Layout()
    {
        if (Picture.Source is not Bitmap source || Bounds.Width <= 0) return;
        var pixels = source.PixelSize;
        var scale = Math.Min((Bounds.Width - 16) / pixels.Width, (Bounds.Height - 16) / pixels.Height);
        Shown = new Size(Math.Max(1, pixels.Width * scale), Math.Max(1, pixels.Height * scale));
        _frame.Width = Shown.Width;
        _frame.Height = Shown.Height;
        Overlay.Width = Shown.Width;
        Overlay.Height = Shown.Height;
        OnLayout();
    }

    protected virtual void OnLayout() { }

    /// <summary>Puts <paramref name="element"/> (a video picture) under the overlay, in the picture's place.</summary>
    public void SetUnderlay(Control element, bool hidePicture)
    {
        _inner.Children.Insert(0, element);
        Picture.Opacity = hidePicture ? 0 : 1;
    }

    public void ShowPicture(bool show) => Picture.Opacity = show ? 1 : 0;

    protected Point ToUnit(Point p) => new(Math.Clamp(p.X / Shown.Width, 0, 1), Math.Clamp(p.Y / Shown.Height, 0, 1));
    protected Point ToShown(double x, double y) => new(x * Shown.Width, y * Shown.Height);
}

/// <summary>The crop frame: drag inside to move it, drag a corner to resize it, with an optional fixed aspect ratio.</summary>
public sealed class CropEditor : PictureStage
{
    private readonly Rectangle _frameLine = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll) };
    private readonly Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), IsHitTestVisible = false };
    private readonly List<Rectangle> _handles = [];
    private Area _start;
    private Point _down;
    private int _dragging = -2;

    /// <summary>The crop in shares of the picture, top-left origin.</summary>
    public Area Crop { get; private set; } = new(0, 0, 1, 1);
    /// <summary>Width over height in picture pixels, or null for free.</summary>
    public double? Ratio { get; private set; }
    public int PixelWidth { get; set; } = 1;
    public int PixelHeight { get; set; } = 1;
    public event Action? Changed;

    public CropEditor(double height) : base(height)
    {
        Overlay.Children.Add(_shade);
        Overlay.Children.Add(_frameLine);
        for (var i = 0; i < 4; i++)
        {
            var handle = new Rectangle
            {
                Width = 12, Height = 12, Fill = Look.Accent, Stroke = Brushes.White, StrokeThickness = 1.5,
                Cursor = new Cursor(i % 3 == 0 ? StandardCursorType.TopLeftCorner : StandardCursorType.TopRightCorner),
            };
            var index = i;
            handle.PointerPressed += (_, e) => Begin(index, e);
            _handles.Add(handle);
            Overlay.Children.Add(handle);
        }
        _frameLine.PointerPressed += (_, e) => Begin(-1, e);
        Overlay.PointerMoved += (_, e) => Drag(e);
        Overlay.PointerReleased += (_, e) => { _dragging = -2; e.Pointer.Capture(null); };
    }

    public void SetRatio(double? ratio)
    {
        Ratio = ratio;
        if (ratio is { } r)
        {
            // The largest centred crop with this shape.
            var pictureRatio = (double)PixelWidth / PixelHeight;
            double w = 1, h = 1;
            if (r > pictureRatio) h = pictureRatio / r; else w = r / pictureRatio;
            Crop = new Area((1 - w) / 2, (1 - h) / 2, w, h);
        }
        Draw();
        Changed?.Invoke();
    }

    public void SetCrop(Area crop)
    {
        Crop = crop;
        Draw();
        Changed?.Invoke();
    }

    private void Begin(int index, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;
        _dragging = index;
        _start = Crop;
        _down = ToUnit(e.GetPosition(Overlay));
        e.Pointer.Capture(Overlay);
        e.Handled = true;
    }

    private void Drag(PointerEventArgs e)
    {
        if (_dragging == -2 || !e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;
        var p = ToUnit(e.GetPosition(Overlay));
        double dx = p.X - _down.X, dy = p.Y - _down.Y;
        var r = _start;
        if (_dragging == -1)
        {
            r.X = Math.Clamp(_start.X + dx, 0, 1 - r.Width);
            r.Y = Math.Clamp(_start.Y + dy, 0, 1 - r.Height);
        }
        else
        {
            // Corners 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right; the opposite corner stays put.
            double left = _start.Left, top = _start.Top, right = _start.Right, bottom = _start.Bottom;
            if (_dragging is 0 or 2) left = Math.Clamp(left + dx, 0, right - 0.03); else right = Math.Clamp(right + dx, left + 0.03, 1);
            if (_dragging is 0 or 1) top = Math.Clamp(top + dy, 0, bottom - 0.03); else bottom = Math.Clamp(bottom + dy, top + 0.03, 1);
            r = new Area(new Point(left, top), new Point(right, bottom));
            if (Ratio is { } ratio)
            {
                var unitRatio = ratio * PixelHeight / PixelWidth;
                var h = r.Width / unitRatio;
                if (_dragging is 0 or 1) r = new Area(r.Left, Math.Max(0, r.Bottom - h), r.Width, Math.Min(h, r.Bottom));
                else r = new Area(r.Left, r.Top, r.Width, Math.Min(h, 1 - r.Top));
                r.Width = r.Height * unitRatio;
                if (_dragging is 0 or 2) r.X = _start.Right - r.Width;
            }
        }
        Crop = r;
        Draw();
        Changed?.Invoke();
    }

    protected override void OnLayout() => Draw();

    private void Draw()
    {
        if (Shown.Width <= 1) return;
        var a = ToShown(Crop.Left, Crop.Top);
        var b = ToShown(Crop.Right, Crop.Bottom);
        Canvas.SetLeft(_frameLine, a.X);
        Canvas.SetTop(_frameLine, a.Y);
        _frameLine.Width = Math.Max(1, b.X - a.X);
        _frameLine.Height = Math.Max(1, b.Y - a.Y);
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Shown.Width, Shown.Height)), new RectangleGeometry(new Rect(a, b)));
        Point[] corners = [a, new(b.X, a.Y), new(a.X, b.Y), b];
        for (var i = 0; i < 4; i++)
        {
            Canvas.SetLeft(_handles[i], corners[i].X - 6);
            Canvas.SetTop(_handles[i], corners[i].Y - 6);
        }
    }
}

/// <summary>Boxes drawn over a picture: drag on empty space to draw one, drag a box to move it, drag its corner to resize it,
/// Delete removes the selected one. Coordinates are shares of the picture.</summary>
public sealed class BoxEditor : PictureStage
{
    public sealed class Box { public Area Rect; public Guid Id = Guid.NewGuid(); public object? Tag; }

    public List<Box> Boxes { get; } = [];
    public Box? Selected { get; private set; }
    public IBrush BoxFill { get; set; } = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0));
    public event Action? Changed;
    public event Action? SelectionChanged;
    /// <summary>Which boxes to draw right now (video redaction shows only those active at the playhead).</summary>
    public Func<Box, bool> Visible { get; set; } = _ => true;

    private Point _down;
    private Area _start;
    private enum Mode { None, Draw, Move, Resize }
    private Mode _mode;

    public BoxEditor(double height) : base(height)
    {
        Overlay.Cursor = new Cursor(StandardCursorType.Cross);
        Overlay.PointerPressed += (_, e) => Down(e);
        Overlay.PointerMoved += (_, e) => Move(e);
        Overlay.PointerReleased += (_, e) =>
        {
            if (_mode == Mode.Draw && Selected != null && (Selected.Rect.Width < 0.01 || Selected.Rect.Height < 0.01)) { Boxes.Remove(Selected); Select(null); }
            if (_mode != Mode.None) Changed?.Invoke();
            _mode = Mode.None;
            e.Pointer.Capture(null);
            Draw();
        };
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key is Key.Delete or Key.Back) DeleteSelected(); };
    }

    public void DeleteSelected()
    {
        if (Selected == null) return;
        Boxes.Remove(Selected);
        Select(null);
        Changed?.Invoke();
        Draw();
    }

    public void Add(Area rect, object? tag = null)
    {
        var box = new Box { Rect = rect, Tag = tag };
        Boxes.Add(box);
        Select(box);
        Changed?.Invoke();
        Draw();
    }

    public void Select(Box? box)
    {
        Selected = box;
        SelectionChanged?.Invoke();
        Draw();
    }

    private void Down(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;
        Focus();
        var p = ToUnit(e.GetPosition(Overlay));
        _down = p;
        var hit = Boxes.LastOrDefault(b => Visible(b) && b.Rect.Inflated(0.012).Contains(p));
        if (hit != null)
        {
            Select(hit);
            _start = hit.Rect;
            _mode = Math.Abs(p.X - hit.Rect.Right) < 0.025 && Math.Abs(p.Y - hit.Rect.Bottom) < 0.035 ? Mode.Resize : Mode.Move;
        }
        else
        {
            var box = new Box { Rect = new Area(p, p) };
            Boxes.Add(box);
            Select(box);
            _mode = Mode.Draw;
        }
        e.Pointer.Capture(Overlay);
        e.Handled = true;
    }

    private void Move(PointerEventArgs e)
    {
        if (_mode == Mode.None || Selected == null) return;
        var p = ToUnit(e.GetPosition(Overlay));
        switch (_mode)
        {
            case Mode.Draw: Selected.Rect = new Area(_down, p); break;
            case Mode.Move:
                Selected.Rect = new Area(Math.Clamp(_start.X + p.X - _down.X, 0, 1 - _start.Width), Math.Clamp(_start.Y + p.Y - _down.Y, 0, 1 - _start.Height), _start.Width, _start.Height);
                break;
            case Mode.Resize: Selected.Rect = new Area(_start.TopLeft, new Point(Math.Max(_start.X + 0.01, p.X), Math.Max(_start.Y + 0.01, p.Y))); break;
        }
        Draw();
    }

    protected override void OnLayout() => Draw();

    public void Draw()
    {
        Overlay.Children.Clear();
        foreach (var box in Boxes.Where(Visible))
        {
            var a = ToShown(box.Rect.Left, box.Rect.Top);
            var shape = new Rectangle
            {
                Width = Math.Max(1, box.Rect.Width * Shown.Width), Height = Math.Max(1, box.Rect.Height * Shown.Height), Fill = BoxFill,
                Stroke = box == Selected ? Look.Accent : Brushes.White, StrokeThickness = box == Selected ? 2 : 1, IsHitTestVisible = false,
            };
            Canvas.SetLeft(shape, a.X);
            Canvas.SetTop(shape, a.Y);
            Overlay.Children.Add(shape);
            if (box == Selected)
            {
                var grip = new Rectangle { Width = 10, Height = 10, Fill = Look.Accent, Stroke = Brushes.White, IsHitTestVisible = false };
                Canvas.SetLeft(grip, a.X + shape.Width - 5);
                Canvas.SetTop(grip, a.Y + shape.Height - 5);
                Overlay.Children.Add(grip);
            }
        }
    }
}
