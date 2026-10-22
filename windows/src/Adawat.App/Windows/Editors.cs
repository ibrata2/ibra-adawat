using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ImageMagick;

using FillRule = System.Windows.Media.FillRule;

namespace Adawat.App.Windows;

/// <summary>Pictures for previews: Magick images turned into WPF bitmaps.</summary>
public static class Pictures
{
    public static BitmapSource ToBitmap(MagickImage image)
    {
        var bytes = image.ToByteArray(MagickFormat.Png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource? FromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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

/// <summary>A picture inside a fixed area, letterboxed on a dark background, with a canvas over it in picture coordinates (0…1).</summary>
public class PictureStage : Grid
{
    protected readonly Image Picture = new() { Stretch = Stretch.Uniform };
    protected readonly Canvas Overlay = new() { ClipToBounds = true };
    private readonly Border _frame;

    public PictureStage(double height)
    {
        Height = height;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20));
        FlowDirection = FlowDirection.LeftToRight;
        _frame = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var inner = new Grid();
        inner.Children.Add(Picture);
        inner.Children.Add(Overlay);
        _frame.Child = inner;
        Children.Add(_frame);
        SizeChanged += (_, _) => Layout();
    }

    public BitmapSource? Source
    {
        get => Picture.Source as BitmapSource;
        set { Picture.Source = value; Layout(); }
    }

    /// <summary>The picture's displayed size; overlay coordinates use it.</summary>
    public Size Shown { get; private set; }

    private void Layout()
    {
        if (Picture.Source is not BitmapSource source || ActualWidth <= 0) return;
        var scale = Math.Min((ActualWidth - 16) / source.PixelWidth, (ActualHeight - 16) / source.PixelHeight);
        Shown = new Size(Math.Max(1, source.PixelWidth * scale), Math.Max(1, source.PixelHeight * scale));
        _frame.Width = Shown.Width;
        _frame.Height = Shown.Height;
        Overlay.Width = Shown.Width;
        Overlay.Height = Shown.Height;
        OnLayout();
    }

    protected virtual void OnLayout() { }

    /// <summary>Puts <paramref name="element"/> (a video player) under the overlay, in the picture's place.</summary>
    public void SetUnderlay(UIElement element, bool hidePicture)
    {
        var inner = (Grid)_frame.Child;
        inner.Children.Insert(0, element);
        Picture.Opacity = hidePicture ? 0 : 1;
    }

    public void ShowPicture(bool show) => Picture.Opacity = show ? 1 : 0;

    protected Point ToUnit(Point p) => new(Math.Clamp(p.X / Shown.Width, 0, 1), Math.Clamp(p.Y / Shown.Height, 0, 1));
    protected Point ToShown(double x, double y) => new(x * Shown.Width, y * Shown.Height);
}

/// <summary>The crop frame: drag inside to move it, drag a corner to resize it, with an optional fixed aspect ratio.</summary>
public sealed class CropEditor : PictureStage
{
    private readonly Rectangle _frameLine = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Transparent, Cursor = Cursors.SizeAll };
    private readonly System.Windows.Shapes.Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), IsHitTestVisible = false };
    private readonly List<Rectangle> _handles = [];
    private Rect _start;
    private Point _down;
    private int _dragging = -2;

    /// <summary>The crop in shares of the picture, top-left origin.</summary>
    public Rect Crop { get; private set; } = new(0, 0, 1, 1);
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
            var handle = new Rectangle { Width = 12, Height = 12, Fill = Theme.Accent, Stroke = Brushes.White, StrokeThickness = 1.5, Cursor = i % 3 == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW };
            var index = i;
            handle.MouseLeftButtonDown += (_, e) => Begin(index, e);
            _handles.Add(handle);
            Overlay.Children.Add(handle);
        }
        _frameLine.MouseLeftButtonDown += (_, e) => Begin(-1, e);
        Overlay.MouseMove += (_, e) => Drag(e);
        Overlay.MouseLeftButtonUp += (_, _) => { _dragging = -2; Overlay.ReleaseMouseCapture(); };
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
            Crop = new Rect((1 - w) / 2, (1 - h) / 2, w, h);
        }
        Draw();
        Changed?.Invoke();
    }

    public void SetCrop(Rect crop)
    {
        Crop = crop;
        Draw();
        Changed?.Invoke();
    }

    private void Begin(int index, MouseButtonEventArgs e)
    {
        _dragging = index;
        _start = Crop;
        _down = ToUnit(e.GetPosition(Overlay));
        Overlay.CaptureMouse();
        e.Handled = true;
    }

    private void Drag(MouseEventArgs e)
    {
        if (_dragging == -2 || e.LeftButton != MouseButtonState.Pressed) return;
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
            r = new Rect(new Point(left, top), new Point(right, bottom));
            if (Ratio is { } ratio)
            {
                var unitRatio = ratio * PixelHeight / PixelWidth;
                var h = r.Width / unitRatio;
                if (_dragging is 0 or 1) r = new Rect(r.Left, Math.Max(0, r.Bottom - h), r.Width, Math.Min(h, r.Bottom));
                else r = new Rect(r.Left, r.Top, r.Width, Math.Min(h, 1 - r.Top));
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
        var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
        shade.Children.Add(new RectangleGeometry(new Rect(0, 0, Shown.Width, Shown.Height)));
        shade.Children.Add(new RectangleGeometry(new Rect(a, b)));
        _shade.Data = shade;
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
    public sealed class Box { public Rect Rect; public Guid Id = Guid.NewGuid(); public object? Tag; }

    public List<Box> Boxes { get; } = [];
    public Box? Selected { get; private set; }
    public Brush Fill { get; set; } = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0));
    public event Action? Changed;
    public event Action? SelectionChanged;
    /// <summary>Which boxes to draw right now (video redaction shows only those active at the playhead).</summary>
    public Func<Box, bool> Visible { get; set; } = _ => true;

    private Point _down;
    private Rect _start;
    private enum Mode { None, Draw, Move, Resize }
    private Mode _mode;

    public BoxEditor(double height) : base(height)
    {
        Overlay.Background = Brushes.Transparent;
        Overlay.Cursor = Cursors.Cross;
        Overlay.MouseLeftButtonDown += (_, e) => Down(e);
        Overlay.MouseMove += (_, e) => Move(e);
        Overlay.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == Mode.Draw && Selected != null && (Selected.Rect.Width < 0.01 || Selected.Rect.Height < 0.01)) { Boxes.Remove(Selected); Select(null); }
            if (_mode != Mode.None) Changed?.Invoke();
            _mode = Mode.None;
            Overlay.ReleaseMouseCapture();
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

    public void Add(Rect rect, object? tag = null)
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

    private void Down(MouseButtonEventArgs e)
    {
        Focus();
        var p = ToUnit(e.GetPosition(Overlay));
        _down = p;
        var hit = Boxes.LastOrDefault(b => Visible(b) && Inflate(b.Rect, 0.012).Contains(p));
        if (hit != null)
        {
            Select(hit);
            _start = hit.Rect;
            var corner = new Point(hit.Rect.Right, hit.Rect.Bottom);
            _mode = Math.Abs(p.X - corner.X) < 0.025 && Math.Abs(p.Y - corner.Y) < 0.035 ? Mode.Resize : Mode.Move;
        }
        else
        {
            var box = new Box { Rect = new Rect(p, p) };
            Boxes.Add(box);
            Select(box);
            _mode = Mode.Draw;
        }
        Overlay.CaptureMouse();
        e.Handled = true;
    }

    private static Rect Inflate(Rect r, double by) { r.Inflate(by, by); return r; }

    private void Move(MouseEventArgs e)
    {
        if (_mode == Mode.None || Selected == null) return;
        var p = ToUnit(e.GetPosition(Overlay));
        switch (_mode)
        {
            case Mode.Draw: Selected.Rect = new Rect(_down, p); break;
            case Mode.Move:
                Selected.Rect = new Rect(Math.Clamp(_start.X + p.X - _down.X, 0, 1 - _start.Width), Math.Clamp(_start.Y + p.Y - _down.Y, 0, 1 - _start.Height), _start.Width, _start.Height);
                break;
            case Mode.Resize: Selected.Rect = new Rect(_start.TopLeft, new Point(Math.Max(_start.X + 0.01, p.X), Math.Max(_start.Y + 0.01, p.Y))); break;
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
                Width = Math.Max(1, box.Rect.Width * Shown.Width), Height = Math.Max(1, box.Rect.Height * Shown.Height), Fill = Fill,
                Stroke = box == Selected ? Theme.Accent : Brushes.White, StrokeThickness = box == Selected ? 2 : 1, IsHitTestVisible = false,
            };
            Canvas.SetLeft(shape, a.X);
            Canvas.SetTop(shape, a.Y);
            Overlay.Children.Add(shape);
            if (box == Selected)
            {
                var grip = new Rectangle { Width = 10, Height = 10, Fill = Theme.Accent, Stroke = Brushes.White, IsHitTestVisible = false };
                Canvas.SetLeft(grip, a.X + shape.Width - 5);
                Canvas.SetTop(grip, a.Y + shape.Height - 5);
                Overlay.Children.Add(grip);
            }
        }
    }
}
