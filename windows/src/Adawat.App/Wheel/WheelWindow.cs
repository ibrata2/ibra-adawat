using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Adawat.Core;

namespace Adawat.App.Wheel;

/// <summary>Sizes of the wheel in device-independent pixels, the same as the Mac app's.</summary>
public static class WheelGeometry
{
    public const double Size = 300, OuterRadius = 119.5, InnerRadius = 48.6;
    public static double LabelRadius => (InnerRadius + OuterRadius) / 2;

    /// <summary>Centre of petal <paramref name="index"/> of <paramref name="count"/>, starting at the top and going clockwise.</summary>
    public static Point Position(int index, int count)
    {
        var angle = -Math.PI / 2 + (double)index / Math.Max(count, 1) * 2 * Math.PI;
        return new Point(Size / 2 + LabelRadius * Math.Cos(angle), Size / 2 + LabelRadius * Math.Sin(angle));
    }

    /// <summary>The petal under <paramref name="point"/> (top-left origin), or null over the centre label.</summary>
    public static int? Index(Point point, int count)
    {
        if (count <= 0) return null;
        double dx = point.X - Size / 2, dy = point.Y - Size / 2;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= InnerRadius - 8 || distance > OuterRadius + 18) return null;
        var angle = Math.Atan2(dy, dx) + Math.PI / 2;
        if (angle < 0) angle += 2 * Math.PI;
        var step = 2 * Math.PI / count;
        return (int)((angle + step / 2) / step) % count;
    }

    /// <summary>One rounded annular sector, separated from its neighbours by a small gap.</summary>
    public static Geometry Petal(int index, int count)
    {
        var center = new Point(Size / 2, Size / 2);
        const double ri = InnerRadius, ro = OuterRadius, gap = 2, innerCorner = 7, outerCorner = 20;
        if (count == 1)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(center, ro, ro));
            ring.Children.Add(new EllipseGeometry(center, ri, ri));
            ring.Freeze();
            return ring;
        }
        var step = 2 * Math.PI / count;
        var mid = -Math.PI / 2 + index * step;
        double start = mid - step / 2, end = mid + step / 2;
        Point P(double radius, double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        static Point Toward(Point from, Point to, double distance)
        {
            var length = (to - from).Length;
            if (length <= 0) return from;
            var t = Math.Min(distance / length, 0.5);
            return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
        double i0 = start + Math.Asin(gap / ri), o0 = start + Math.Asin(gap / ro), o1 = end - Math.Asin(gap / ro), i1 = end - Math.Asin(gap / ri);
        Point I0 = P(ri, i0), O0 = P(ro, o0), O1 = P(ro, o1), I1 = P(ri, i1);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            void Arc(double radius, double a0, double a1)
            {
                const int segments = 28;
                for (var i = 1; i <= segments; i++) g.LineTo(P(radius, a0 + (a1 - a0) * i / segments), true, true);
            }
            g.BeginFigure(Toward(I0, O0, innerCorner), true, true);
            g.LineTo(Toward(O0, I0, outerCorner), true, true);
            g.QuadraticBezierTo(O0, P(ro, o0 + outerCorner / ro), true, true);
            Arc(ro, o0 + outerCorner / ro, o1 - outerCorner / ro);
            g.QuadraticBezierTo(O1, Toward(O1, I1, outerCorner), true, true);
            g.LineTo(Toward(I1, O1, innerCorner), true, true);
            g.QuadraticBezierTo(I1, P(ri, i1 - innerCorner / ri), true, true);
            Arc(ri, i1 - innerCorner / ri, i0 + innerCorner / ri);
            g.QuadraticBezierTo(I0, Toward(I0, O0, innerCorner), true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>
/// The wheel: a borderless, never-focused window under the pointer that accepts the drag. It learns which files are being
/// dragged when the drag enters it, lights the petal under the pointer, and runs that choice on drop.
/// </summary>
public sealed class WheelWindow : Window
{
    private readonly Grid _root = new() { Width = WheelGeometry.Size, Height = WheelGeometry.Size, Background = Brushes.Transparent };
    // Petals are redrawn as the hover moves; were they hit-testable, the element under the pointer would vanish mid-drag and
    // Windows would show "no drop". The steady background takes the drag, and the angle decides the petal.
    private readonly Canvas _petals = new() { Width = WheelGeometry.Size, Height = WheelGeometry.Size, IsHitTestVisible = false };
    private readonly TextBlock _center = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Ink, MaxWidth = 64 };
    private readonly ScaleTransform _scale = new(0.9, 0.9, WheelGeometry.Size / 2, WheelGeometry.Size / 2);
    private IReadOnlyList<AdawatOperation> _operations = [];
    private IReadOnlyList<string> _files = [];
    private int? _hovered;
    private bool _shown;

    public WheelMode Mode { get; private set; }

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        // Petals sit by angle; mirroring would swap them with their drop zones.
        FlowDirection = FlowDirection.LeftToRight;

        var disc = new Ellipse
        {
            Width = (WheelGeometry.OuterRadius + 6.5) * 2, Height = (WheelGeometry.OuterRadius + 6.5) * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1,
            Fill = Theme.Disc, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Direction = 270, Opacity = 0.18, Color = Color.FromRgb(0x26, 0x34, 0x38) },
        };
        var hole = new EllipseGeometry(new Point(WheelGeometry.Size / 2, WheelGeometry.Size / 2), WheelGeometry.InnerRadius - 6, WheelGeometry.InnerRadius - 6);
        disc.OpacityMask = new DrawingBrush(new GeometryDrawing(Brushes.Black, null,
            new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(WheelGeometry.Size / 2, WheelGeometry.Size / 2), WheelGeometry.OuterRadius + 6.5, WheelGeometry.OuterRadius + 6.5), hole)))
        { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(WheelGeometry.Size / 2 - WheelGeometry.OuterRadius - 6.5, WheelGeometry.Size / 2 - WheelGeometry.OuterRadius - 6.5, (WheelGeometry.OuterRadius + 6.5) * 2, (WheelGeometry.OuterRadius + 6.5) * 2) };
        var centerPill = new Border
        {
            Child = _center, Background = Theme.CenterFill, CornerRadius = new CornerRadius(24),
            BorderBrush = Theme.PetalStroke, BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6), MinHeight = 32, MinWidth = 70, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = 0.1 },
        };
        _center.VerticalAlignment = VerticalAlignment.Center;
        _root.Children.Add(disc);
        _root.Children.Add(_petals);
        _root.Children.Add(centerPill);
        _root.RenderTransform = _scale;
        _root.AllowDrop = true;
        _root.DragEnter += (_, e) => OnDrag(e, entering: true);
        _root.DragOver += (_, e) => OnDrag(e, entering: false);
        _root.DragLeave += (_, e) =>
        {
            // Moving between the disc and the centre label also raises DragLeave; only leaving the wheel clears the hover.
            var p = e.GetPosition(_root);
            if (p.X <= 0 || p.Y <= 0 || p.X >= _root.ActualWidth || p.Y >= _root.ActualHeight) SetHovered(null);
        };
        _root.Drop += OnDrop;
        Content = _root;
        SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Opens (or switches) the wheel for <paramref name="mode"/> centred on <paramref name="screenPixels"/>.</summary>
    public void Show(WheelMode mode, Point screenPixels)
    {
        Mode = mode;
        if (!_shown)
        {
            _files = [];
            _operations = [];
            Opacity = 0;
            if (!IsLoaded) { Left = -10000; Top = -10000; base.Show(); }
            Place(screenPixels);
            base.Show();
            _shown = true;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            var grow = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        else if (_files.Count > 0)
        {
            _operations = Catalog.For(mode, _files);
        }
        _hovered = null;
        Draw();
        Sounds.Tick();
    }

    public void HideWheel()
    {
        if (!_shown) return;
        _shown = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => { if (!_shown) base.Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    public bool IsWheelShown => _shown;

    /// <summary>Centres the window on the pointer, kept inside the work area of that screen.</summary>
    private void Place(Point screenPixels)
    {
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var scale = toDevice.M11 <= 0 ? 1 : toDevice.M11;
        var screen = Native.WorkArea((int)screenPixels.X, (int)screenPixels.Y);
        var size = WheelGeometry.Size * scale;
        var x = Math.Clamp(screenPixels.X - size / 2, screen.Left, screen.Right - size);
        var y = Math.Clamp(screenPixels.Y - size / 2, screen.Top, screen.Bottom - size);
        Left = x / scale;
        Top = y / scale;
    }

    private void OnDrag(DragEventArgs e, bool entering)
    {
        e.Handled = true;
        if (entering && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            _files = files;
            _operations = Catalog.For(Mode, _files);
            _hovered = null;
            Draw();
        }
        var index = _operations.Count > 0 ? WheelGeometry.Index(e.GetPosition(_root), _operations.Count) : null;
        SetHovered(index);
        // Never "move": Explorer would delete the originals after the drop.
        e.Effects = index != null && e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
                  : index != null && e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var operation = _hovered is { } index && index < _operations.Count ? _operations[index] : null;
        e.Effects = operation != null ? DragDropEffects.Copy : DragDropEffects.None;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? _files.ToArray();
        HideWheel();
        if (operation == null || files.Length == 0) return;
        // Start after the drag has fully ended, so windows the operation opens can take focus.
        Dispatcher.BeginInvoke(() => Runner.Start(operation, files), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SetHovered(int? index)
    {
        if (index == _hovered) return;
        _hovered = index;
        Draw();
        if (index != null) Sounds.Tick();
    }

    private void Draw()
    {
        _petals.Children.Clear();
        var count = _operations.Count;
        for (var index = 0; index < count; index++)
        {
            var hovered = index == _hovered;
            var operation = _operations[index];
            var petal = new System.Windows.Shapes.Path
            {
                Data = WheelGeometry.Petal(index, count), Fill = hovered ? Theme.Accent : Theme.PetalFill,
                Stroke = hovered ? Theme.PetalHoverStroke : Theme.PetalStroke, StrokeThickness = 0.75,
            };
            var group = new Canvas();
            group.Children.Add(petal);
            var label = Label(operation, hovered, count);
            var position = WheelGeometry.Position(index, count);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, position.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, position.Y - label.DesiredSize.Height / 2);
            group.Children.Add(label);
            if (hovered && count > 1)
            {
                // The hovered petal moves a few pixels outward along its centre line.
                var angle = -Math.PI / 2 + (double)index / count * 2 * Math.PI;
                group.RenderTransform = new TranslateTransform(Math.Cos(angle) * 5, Math.Sin(angle) * 5);
                group.Effect = new DropShadowEffect { Color = Theme.AccentColor, BlurRadius = 12, ShadowDepth = 3, Direction = 270, Opacity = 0.35 };
            }
            else
            {
                group.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 270, Opacity = 0.12, Color = Color.FromRgb(0x26, 0x34, 0x38) };
            }
            _petals.Children.Add(group);
        }
        var current = _hovered is { } h && h < count ? _operations[h] : null;
        var isTool = current is { Badge: null };
        _center.Text = current?.WheelLabel ?? (_files.Count > 0 && count == 0 ? L10n.Upper(L10n.T("NO OPTIONS")) : L10n.Upper(L10n.T(ModeTitle(Mode))));
        _center.FontFamily = Theme.Font;
        _center.FontSize = isTool || current == null ? 10 : 13;
        _center.FontWeight = isTool || current == null ? FontWeights.SemiBold : FontWeights.Medium;
    }

    /// <summary>The wheel's drawing for a given state, detached from any window, for snapshots and the welcome guide.</summary>
    public FrameworkElement Preview(WheelMode mode, IReadOnlyList<string> files, int? hovered)
    {
        Mode = mode;
        _files = files;
        _operations = Catalog.For(mode, files);
        _hovered = hovered;
        _scale.ScaleX = _scale.ScaleY = 1;
        Draw();
        Content = null;
        return _root;
    }

    public static string ModeTitle(WheelMode mode) => mode switch { WheelMode.Convert => "Convert", WheelMode.Tools => "Tools", _ => "Actions" };

    private static FrameworkElement Label(AdawatOperation operation, bool hovered, int count)
    {
        var color = hovered ? Brushes.White : Theme.Ink;
        if (operation.Badge is { } badge)
        {
            return new TextBlock
            {
                Text = badge.ToUpperInvariant(), FontFamily = Theme.Font, FontWeight = FontWeights.SemiBold,
                FontSize = count > 8 ? 12.5 : 14.5, Foreground = color, IsHitTestVisible = false,
            };
        }
        var stack = new StackPanel { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = operation.Icon, FontFamily = Theme.Icons, FontSize = 15, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = operation.WheelLabel, FontFamily = Theme.Font, FontWeight = FontWeights.SemiBold, FontSize = 9.5, Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), MaxWidth = 66, TextTrimming = TextTrimming.CharacterEllipsis,
            FlowDirection = Theme.Direction,
        });
        return stack;
    }
}

public static class WheelPreview
{
    public static FrameworkElement Make(WheelMode mode, IReadOnlyList<string> files, int? hovered) => new WheelWindow().Preview(mode, files, hovered);
}
