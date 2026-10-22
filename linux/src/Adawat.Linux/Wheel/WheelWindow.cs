using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Threading;
using Adawat.Core;

namespace Adawat.Linux.Wheel;

/// <summary>Sizes of the wheel in device-independent pixels, the same as the Mac and Windows apps'.</summary>
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
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Rect(center.X - ro, center.Y - ro, ro * 2, ro * 2)),
                new EllipseGeometry(new Rect(center.X - ri, center.Y - ri, ri * 2, ri * 2)));
        }
        var step = 2 * Math.PI / count;
        var mid = -Math.PI / 2 + index * step;
        double start = mid - step / 2, end = mid + step / 2;
        Point P(double radius, double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        static Point Toward(Point from, Point to, double distance)
        {
            var length = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
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
                for (var i = 1; i <= segments; i++) g.LineTo(P(radius, a0 + (a1 - a0) * i / segments));
            }
            g.BeginFigure(Toward(I0, O0, innerCorner), true);
            g.LineTo(Toward(O0, I0, outerCorner));
            g.QuadraticBezierTo(O0, P(ro, o0 + outerCorner / ro));
            Arc(ro, o0 + outerCorner / ro, o1 - outerCorner / ro);
            g.QuadraticBezierTo(O1, Toward(O1, I1, outerCorner));
            g.LineTo(Toward(I1, O1, innerCorner));
            g.QuadraticBezierTo(I1, P(ri, i1 - innerCorner / ri));
            Arc(ri, i1 - innerCorner / ri, i0 + innerCorner / ri);
            g.QuadraticBezierTo(I0, Toward(I0, O0, innerCorner));
            g.EndFigure(true);
        }
        return geometry;
    }
}

/// <summary>
/// The wheel: a borderless, never-focused window under the pointer that accepts the drag. It learns which files are being
/// dragged when the drag enters it, lights the petal under the pointer, and runs that choice on drop.
/// </summary>
public sealed class WheelWindow : Window
{
    private readonly Panel _root = new() { Width = WheelGeometry.Size, Height = WheelGeometry.Size, Background = Brushes.Transparent };
    // Petals are redrawn as the hover moves; the steady background takes the drag, and the angle decides the petal.
    private readonly Canvas _petals = new() { Width = WheelGeometry.Size, Height = WheelGeometry.Size, IsHitTestVisible = false };
    private readonly TextBlock _center = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black, MaxWidth = 64 };
    private IReadOnlyList<AdawatOperation> _operations = [];
    private IReadOnlyList<string> _files = [];
    private int? _hovered;
    private bool _shown;
    // Opened from a file manager's menu rather than during a drag: petals are clicked, and keys switch the wheel.
    private bool _clicking;

    public WheelMode Mode { get; private set; }

    /// <param name="clicking">For the file manager's menu, where the wheel takes clicks and keys; otherwise it follows drags.</param>
    public WheelWindow(bool clicking = false)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Adawat Wheel";
        // Petals sit by angle; mirroring would swap them with their drop zones.
        FlowDirection = FlowDirection.LeftToRight;

        const double discRadius = WheelGeometry.OuterRadius + 6.5;
        var middle = WheelGeometry.Size / 2;
        var disc = new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Rect(middle - discRadius, middle - discRadius, discRadius * 2, discRadius * 2)),
                new EllipseGeometry(new Rect(middle - WheelGeometry.InnerRadius + 6, middle - WheelGeometry.InnerRadius + 6, (WheelGeometry.InnerRadius - 6) * 2, (WheelGeometry.InnerRadius - 6) * 2))),
            Fill = Look.Disc, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 18, OffsetY = 5, OffsetX = 0, Opacity = 0.18, Color = Color.FromRgb(0x26, 0x34, 0x38) },
        };
        var centerPill = new Border
        {
            Child = _center, Background = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(24),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6), MinHeight = 32, MinWidth = 70, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = Look.Shadow(6, 2, 0.1),
        };
        _center.VerticalAlignment = VerticalAlignment.Center;
        _root.Children.Add(disc);
        _root.Children.Add(_petals);
        _root.Children.Add(centerPill);
        _root.RenderTransformOrigin = RelativePoint.Center;
        _root.RenderTransform = TransformOperations.Parse("scale(0.9)");
        _root.Transitions =
        [
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new BackEaseOut() },
        ];
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        DragDrop.SetAllowDrop(_root, true);
        _root.AddHandler(DragDrop.DragEnterEvent, (_, e) => OnDrag(e, entering: true));
        _root.AddHandler(DragDrop.DragOverEvent, (_, e) => OnDrag(e, entering: false));
        _root.AddHandler(DragDrop.DragLeaveEvent, (_, e) =>
        {
            // Moving between the disc and the centre label also raises DragLeave; only leaving the wheel clears the hover.
            var p = e.GetPosition(_root);
            if (p.X <= 0 || p.Y <= 0 || p.X >= _root.Bounds.Width || p.Y >= _root.Bounds.Height) SetHovered(null);
        });
        _root.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(e));
        if (!clicking) Desktop.X11Window.SkipWindowManager(this);
        _root.PointerMoved += (_, e) => { if (_clicking) SetHovered(WheelGeometry.Index(e.GetPosition(_root), _operations.Count)); };
        _root.PointerReleased += (_, e) =>
        {
            if (!_clicking) return;
            var index = WheelGeometry.Index(e.GetPosition(_root), _operations.Count);
            if (index == null)
            {
                // The centre label switches between the three wheels.
                var distance = Math.Sqrt(Math.Pow(e.GetPosition(_root).X - WheelGeometry.Size / 2, 2) + Math.Pow(e.GetPosition(_root).Y - WheelGeometry.Size / 2, 2));
                if (distance < WheelGeometry.InnerRadius) Switch((WheelMode)(((int)Mode + 1) % 3));
                else HideWheel();
                return;
            }
            var operation = _operations[index.Value];
            var files = _files;
            HideWheel();
            Dispatcher.UIThread.Post(() => Runner.Start(operation, files), DispatcherPriority.Background);
        };
        KeyDown += (_, e) =>
        {
            if (!_clicking) return;
            var settings = Settings.Current;
            ModifierKey? key = e.Key switch
            {
                Key.LeftShift or Key.RightShift => ModifierKey.Shift,
                Key.LeftCtrl or Key.RightCtrl => ModifierKey.Ctrl,
                Key.LeftAlt or Key.RightAlt => ModifierKey.Alt,
                _ => null,
            };
            if (e.Key == Key.Escape) HideWheel();
            else if (key == settings.ConvertKey) Switch(WheelMode.Convert);
            else if (key == settings.ToolsKey) Switch(WheelMode.Tools);
            else if (key == settings.ActionsKey) Switch(WheelMode.Actions);
        };
        Deactivated += (_, _) => { if (_clicking) HideWheel(); };
        Content = _root;
    }

    /// <summary>Opens the wheel for files chosen in a file manager, at the pointer when it is known.</summary>
    public void ShowForFiles(IReadOnlyList<string> files, PixelPoint at)
    {
        _clicking = true;
        ShowActivated = true;
        Show(WheelMode.Convert, at);
        _files = files;
        Switch(WheelMode.Convert);
        Activate();
    }

    private void Switch(WheelMode mode)
    {
        Mode = mode;
        _operations = Catalog.For(mode, _files);
        _hovered = null;
        Draw();
        Sounds.Tick();
    }

    /// <summary>Opens (or switches) the wheel for <paramref name="mode"/> centred on <paramref name="screenPixels"/>.</summary>
    /// <summary>Puts the drag wheel on screen once, out of sight. GNOME under Wayland does not draw an X11 window that first maps
    /// during a drag until the drop, but it does move and redraw one that is already mapped, so the wheel never unmaps:
    /// it waits off screen and moves under the pointer when a key is pressed.</summary>
    public void Park()
    {
        if (_clicking) return;
        // A first drawing with real petals loads the fonts and glyphs now rather than on the first drag.
        _files = ["warm-up.jpg"];
        _operations = Catalog.For(WheelMode.Convert, _files);
        Draw();
        Position = Parked;
        if (!IsVisible) base.Show();
        Position = Parked;
        _files = [];
        _operations = [];
        Draw();
    }

    private static readonly PixelPoint Parked = new(-4000, -4000);

    /// <summary>Opens (or switches) the wheel for <paramref name="mode"/> centred on <paramref name="screenPixels"/>.</summary>
    public void Show(WheelMode mode, PixelPoint screenPixels)
    {
        Debug($"show {mode} at {screenPixels} shown={_shown} visible={IsVisible}");
        Mode = mode;
        if (!_shown)
        {
            _files = [];
            _operations = [];
            _hovered = null;
            Draw();
            Place(screenPixels);
            if (!IsVisible) base.Show();
            Place(screenPixels);
            _shown = true;
            Opacity = 1;
            _root.RenderTransform = TransformOperations.Parse("scale(1)");
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
        Debug($"hide shown={_shown}");
        if (!_shown) return;
        _shown = false;
        var clicking = _clicking;
        _clicking = false;
        ShowActivated = false;
        Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            if (_shown) return;
            _root.RenderTransform = TransformOperations.Parse("scale(0.9)");
            // The file manager's wheel closes; the drag wheel goes back to waiting off screen, still mapped.
            if (clicking) base.Hide();
            else
            {
                Position = Parked;
                Opacity = 1;
            }
        }, TimeSpan.FromMilliseconds(140));
    }

    public bool IsWheelShown => _shown;

    private static void Debug(string text)
    {
        if (File.Exists(System.IO.Path.Combine(Settings.Folder, "debug-drag"))) Log.Write($"wheel: {text}");
    }

    /// <summary>Centres the window on the pointer, kept inside the work area of that screen.</summary>
    private void Place(PixelPoint screenPixels)
    {
        var screen = Screens.ScreenFromPoint(screenPixels) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var size = (int)Math.Round(WheelGeometry.Size * scale);
        var work = screen?.WorkingArea ?? new PixelRect(0, 0, 100000, 100000);
        var x = Math.Clamp(screenPixels.X - size / 2, work.X, Math.Max(work.X, work.Right - size));
        var y = Math.Clamp(screenPixels.Y - size / 2, work.Y, Math.Max(work.Y, work.Bottom - size));
        Position = new PixelPoint(x, y);
    }

    private void OnDrag(DragEventArgs e, bool entering)
    {
        e.Handled = true;
        var entered = entering ? Files(e) : [];
        if (entering) Debug($"drag enter {entered.Count} files");
        if (entering && entered is { Count: > 0 } files)
        {
            _files = files;
            _operations = Catalog.For(Mode, _files);
            _hovered = null;
            Draw();
        }
        var index = _operations.Count > 0 ? WheelGeometry.Index(e.GetPosition(_root), _operations.Count) : null;
        SetHovered(index);
        // Never "move": the file manager would delete the originals after the drop.
        e.DragEffects = index == null ? DragDropEffects.None
                      : e.DragEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
                      : e.DragEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
    }

    private void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        Debug($"drop on {_hovered}");
        var operation = _hovered is { } index && index < _operations.Count ? _operations[index] : null;
        e.DragEffects = operation != null ? DragDropEffects.Copy : DragDropEffects.None;
        var files = Files(e) is { Count: > 0 } dropped ? dropped : _files;
        HideWheel();
        if (operation == null || files.Count == 0) return;
        // Start after the drag has fully ended, so windows the operation opens can take focus.
        Dispatcher.UIThread.Post(() => Runner.Start(operation, files), DispatcherPriority.Background);
    }

    /// <summary>The local paths of the files being dragged.</summary>
    public static IReadOnlyList<string> Files(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList() ?? [];

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
            var petal = new Path
            {
                Data = WheelGeometry.Petal(index, count), Fill = hovered ? Look.Accent : Look.PetalFill,
                Stroke = hovered ? new SolidColorBrush(Color.FromArgb(0x70, 0x3E, 0x2E, 0xAA)) : Look.PetalStroke, StrokeThickness = 0.75,
            };
            var group = new Canvas { Width = WheelGeometry.Size, Height = WheelGeometry.Size };
            group.Children.Add(petal);
            var label = Label(operation, hovered, count);
            var position = WheelGeometry.Position(index, count);
            label.Measure(Size.Infinity);
            Canvas.SetLeft(label, position.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, position.Y - label.DesiredSize.Height / 2);
            group.Children.Add(label);
            if (hovered && count > 1)
            {
                // The hovered petal moves a few pixels outward along its centre line.
                var angle = -Math.PI / 2 + (double)index / count * 2 * Math.PI;
                group.RenderTransform = new TranslateTransform(Math.Cos(angle) * 5, Math.Sin(angle) * 5);
                group.Effect = new DropShadowEffect { Color = Look.AccentColor, BlurRadius = 12, OffsetY = 3, OffsetX = 0, Opacity = 0.35 };
            }
            else
            {
                group.Effect = new DropShadowEffect { BlurRadius = 10, OffsetY = 3, OffsetX = 0, Opacity = 0.12, Color = Color.FromRgb(0x26, 0x34, 0x38) };
            }
            _petals.Children.Add(group);
        }
        var current = _hovered is { } h && h < count ? _operations[h] : null;
        var isTool = current is { Badge: null };
        _center.Text = current?.WheelLabel ?? (_files.Count > 0 && count == 0 ? L10n.Upper(L10n.T("NO OPTIONS")) : L10n.Upper(L10n.T(ModeTitle(Mode))));
        _center.FontFamily = Look.Font;
        _center.FontSize = isTool || current == null ? 10 : 13;
        _center.FontWeight = isTool || current == null ? FontWeight.SemiBold : FontWeight.Medium;
        _center.FlowDirection = Look.Direction;
    }

    /// <summary>The wheel's drawing for a given state, detached from any window, for snapshots and the welcome guide.</summary>
    public Control Preview(WheelMode mode, IReadOnlyList<string> files, int? hovered)
    {
        Mode = mode;
        _files = files;
        _operations = Catalog.For(mode, files);
        _hovered = hovered;
        _root.Transitions = null;
        _root.RenderTransform = null;
        Draw();
        Content = null;
        return _root;
    }

    public static string ModeTitle(WheelMode mode) => mode switch { WheelMode.Convert => "Convert", WheelMode.Tools => "Tools", _ => "Actions" };

    private static Control Label(AdawatOperation operation, bool hovered, int count)
    {
        var color = hovered ? Brushes.White : Brushes.Black;
        if (operation.Badge is { } badge)
        {
            return new TextBlock
            {
                Text = badge.ToUpperInvariant(), FontFamily = Look.Font, FontWeight = FontWeight.SemiBold,
                FontSize = count > 8 ? 12.5 : 14.5, Foreground = color, IsHitTestVisible = false,
            };
        }
        var stack = new StackPanel { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = Icons.Glyph(operation.Icon), FontFamily = Look.Icons, FontSize = 17, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = operation.WheelLabel, FontFamily = Look.Font, FontWeight = FontWeight.SemiBold, FontSize = 9.5, Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 66, TextTrimming = TextTrimming.CharacterEllipsis,
            FlowDirection = Look.Direction, TextAlignment = TextAlignment.Center,
        });
        return stack;
    }
}

public static class WheelPreview
{
    public static Control Make(WheelMode mode, IReadOnlyList<string> files, int? hovered) => new WheelWindow().Preview(mode, files, hovered);
}
