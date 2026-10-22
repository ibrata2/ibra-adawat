using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Adawat.Core;
using Adawat.Linux.Desktop;

namespace Adawat.Linux;

/// <summary>Every open Float.</summary>
public static class Floats
{
    private static readonly List<FloatWindow> Open = [];

    public static bool Any => Open.Count > 0;

    public static void Create(IReadOnlyList<string> files)
    {
        var window = new FloatWindow(files);
        window.Closed += (_, _) => Open.Remove(window);
        Open.Add(window);
        window.Show();
    }

    /// <summary>Results go into the most recent Float, or a new one.</summary>
    public static void ShowResults(IReadOnlyList<string> files)
    {
        if (Open.LastOrDefault() is { } last) { last.Add(files); last.Activate(); }
        else Create(files);
    }

    public static void CloseAll()
    {
        foreach (var window in Open.ToList()) window.Close();
    }
}

/// <summary>
/// A small floating shelf for files: drop files on it to keep them at hand, drag them out to any app, double-click to open,
/// and pin it to the edge of the screen. Closing it never deletes the files.
/// </summary>
public sealed class FloatWindow : Window
{
    private readonly ObservableCollection<string> _files = [];
    private readonly WrapPanel _grid = new() { Margin = new Thickness(8) };
    private readonly TextBlock _count = UI.Text("", 11, FontWeight.SemiBold, Look.Muted);
    private readonly TextBlock _empty = UI.Text("Drop files here", 12, color: Look.Muted);
    private readonly HashSet<string> _selected = [];
    private Point? _pressAt;
    private PointerPressedEventArgs? _press;
    private bool _docked;
    private PixelRect _undocked;

    public FloatWindow(IReadOnlyList<string> files)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Look.Clear;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = true;
        Width = 300;
        Height = 260;
        MinWidth = 200;
        MinHeight = 150;
        FlowDirection = Look.Direction;
        FontFamily = Look.Font;
        Title = L10n.T("Float");
        Icon = AppIcon.Window;
        var at = LastPointer.Position;
        if (at != default) Position = new PixelPoint(at.X - 150, at.Y - 60); else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var header = new DockPanel { Margin = new Thickness(12, 8, 8, 4), LastChildFill = false, Background = Look.Clear };
        var title = UI.Text("Float", 11, FontWeight.Bold, Look.Muted);
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        _count.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(_count, Dock.Left);
        header.Children.Add(_count);
        foreach (var (glyph, help, action) in new (string, string, Action)[]
        {
            ("", "Close Float without deleting files", Close),
            ("", "Attach to the screen edge", ToggleDock),
            ("", "Share", () => { if (Targets().Count > 0) ShareWindow.Present(Targets()); }),
        })
        {
            var button = UI.IconButton(glyph, action, help, 24);
            button.Margin = new Thickness(4, 0, 0, 0);
            DockPanel.SetDock(button, Dock.Right);
            header.Children.Add(button);
        }
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var scroll = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        var body = new Panel();
        body.Children.Add(_empty);
        body.Children.Add(scroll);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(body);
        var grip = new Border { Width = 16, Height = 16, Background = Look.Clear, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = new Cursor(StandardCursorType.BottomRightCorner) };
        grip.PointerPressed += (_, e) => { BeginResizeDrag(WindowEdge.SouthEast, e); e.Handled = true; };
        var frame = new Panel();
        frame.Children.Add(layout);
        frame.Children.Add(grip);
        Content = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(16), Child = frame, ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFB)), BorderBrush = Look.Hairline, BorderThickness = new Thickness(1),
            BoxShadow = Look.Shadow(24, 6, 0.2),
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            Add(Wheel.WheelWindow.Files(e));
            e.Handled = true;
        });
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) Remove(Targets());
            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control) { _selected.UnionWith(_files); Draw(); }
        };
        Add(files);
    }

    public void Add(IEnumerable<string> files)
    {
        foreach (var file in files) if (!_files.Contains(file)) _files.Add(file);
        Draw();
    }

    private void Remove(IEnumerable<string> files)
    {
        foreach (var file in files.ToList()) { _files.Remove(file); _selected.Remove(file); }
        Draw();
    }

    /// <summary>The selection, or every file when nothing is selected.</summary>
    private List<string> Targets() => _selected.Count > 0 ? _files.Where(_selected.Contains).ToList() : [.. _files];

    private void Draw()
    {
        _grid.Children.Clear();
        _count.Text = _files.Count > 0 ? (_files.Count == 1 ? L10n.T("1 file") : L10n.T($"{_files.Count} files")) : "";
        _empty.IsVisible = _files.Count == 0;
        foreach (var file in _files) _grid.Children.Add(Tile(file));
    }

    private Control Tile(string file)
    {
        var selected = _selected.Contains(file);
        Control picture = Thumbnails.For(file, Draw) is { } thumbnail
            ? new Image { Width = 64, Height = 64, Source = thumbnail, Stretch = Stretch.Uniform }
            : new Border { Width = 64, Height = 64, Child = UI.Icon(Thumbnails.KindIcon(file), 34, Look.Muted).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center) };
        var name = new TextBlock
        {
            Text = Path.GetFileName(file.TrimEnd('/')), FontFamily = Look.Font, FontSize = 11, Foreground = Look.Ink, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 78, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), FlowDirection = FlowDirection.LeftToRight,
        };
        var stack = new StackPanel();
        stack.Children.Add(picture);
        stack.Children.Add(name);
        var tile = new Border
        {
            Child = stack, Padding = new Thickness(6), Margin = new Thickness(3), CornerRadius = new CornerRadius(9), Width = 90,
            Background = selected ? Look.AccentSoft : Look.Clear,
        };
        ToolTip.SetTip(tile, L10n.T($"{Path.GetFileName(file)}. Double-click to open; drag to carry the file."));
        tile.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { Core.Desktop.Open(file); return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (!_selected.Remove(file)) _selected.Add(file); }
            else if (!_selected.Contains(file)) { _selected.Clear(); _selected.Add(file); }
            _pressAt = point.Position;
            _press = e;
            Draw();
        };
        tile.PointerMoved += async (_, e) =>
        {
            if (_pressAt is not { } start || _press == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
            var press = _press;
            _pressAt = null;
            _press = null;
            var data = new DataTransfer();
            foreach (var path in Targets())
            {
                IStorageItem? item = Directory.Exists(path) ? await StorageProvider.TryGetFolderFromPathAsync(path) : await StorageProvider.TryGetFileFromPathAsync(path);
                if (item == null) continue;
                var entry = new DataTransferItem();
                entry.SetFile(item);
                data.Add(entry);
            }
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };
        tile.PointerReleased += (_, _) => { _pressAt = null; _press = null; };
        var menu = new ContextMenu { FlowDirection = Look.Direction };
        void Item(string title, Action action) { var item = new MenuItem { Header = L10n.T(title) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Open", () => Core.Desktop.Open(file));
        Item("Show in Files", () => FileActions.ShowInExplorer(file));
        Item("Copy Path", () => Core.Clipboard.CopyText(file));
        Item("Remove from Float", () => Remove([file]));
        tile.ContextMenu = menu;
        return tile;
    }

    /// <summary>Pins the Float as a narrow strip on the right edge of the screen; again to put it back.</summary>
    private void ToggleDock()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var scale = screen.Scaling;
        if (_docked)
        {
            Position = _undocked.Position;
            Width = _undocked.Width / scale;
            Height = _undocked.Height / scale;
            _docked = false;
            return;
        }
        _undocked = new PixelRect(Position, new PixelSize((int)(Width * scale), (int)(Height * scale)));
        var work = screen.WorkingArea;
        Width = 140;
        Height = Math.Min(560, work.Height / scale - 40);
        Position = new PixelPoint(work.Right - (int)((Width - 4) * scale), work.Y + (int)((work.Height - Height * scale) / 2));
        _docked = true;
    }
}

public static class Fluent
{
    /// <summary>Runs <paramref name="change"/> on <paramref name="item"/> and returns it, for building controls inline.</summary>
    public static T Also<T>(this T item, Action<T> change) { change(item); return item; }
}
