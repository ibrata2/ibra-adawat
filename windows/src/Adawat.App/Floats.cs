using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Adawat.Core;

namespace Adawat.App;

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
    private readonly TextBlock _count = UI.Text("", 11, FontWeights.SemiBold, Theme.Muted);
    private readonly TextBlock _empty = UI.Text("Drop files here", 12, color: Theme.Muted);
    private readonly HashSet<string> _selected = [];
    private Point? _pressAt;
    private string? _pressed;
    private bool _docked;
    private Rect _undocked;

    public FloatWindow(IReadOnlyList<string> files)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = 300;
        Height = 260;
        MinWidth = 200;
        MinHeight = 150;
        FlowDirection = Theme.Direction;
        FontFamily = Theme.Font;
        Native.GetCursorPos(out var point);
        var scale = 1.0;
        Left = point.X / scale - 150;
        Top = point.Y / scale - 60;

        var header = new DockPanel { Margin = new Thickness(12, 8, 8, 4), LastChildFill = false };
        var title = UI.Text("Float", 11, FontWeights.Bold, Theme.Muted);
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        _count.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(_count, Dock.Left);
        header.Children.Add(_count);
        foreach (var (glyph, help, action) in new (string, string, Action)[]
        {
            ("", "Close Float without deleting files", Close),
            ("", "Attach to the screen edge", ToggleDock),
            ("", "Share", () => { if (Targets().Count > 0) ShareUI.Share(Targets()); }),
        })
        {
            var button = UI.IconButton(glyph, action, help, 24);
            button.Margin = new Thickness(4, 0, 0, 0);
            DockPanel.SetDock(button, Dock.Right);
            header.Children.Add(button);
        }
        header.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) DragMove(); };

        var scroll = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        var body = new Grid();
        body.Children.Add(_empty);
        body.Children.Add(scroll);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(body);
        Content = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(16), Child = layout,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFB)), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.2 },
        };
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped) Add(dropped);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) Remove(Targets());
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { _selected.UnionWith(_files); Draw(); }
        };
        SourceInitialized += (_, _) => Native.RoundCorners(new WindowInteropHelper(this).Handle);
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
        _empty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var file in _files) _grid.Children.Add(Tile(file));
    }

    private FrameworkElement Tile(string file)
    {
        var selected = _selected.Contains(file);
        var image = new Image { Width = 64, Height = 64, Source = ShellThumbnail.For(file, 128), Stretch = Stretch.Uniform };
        var name = new TextBlock
        {
            Text = Path.GetFileName(file.TrimEnd('\\')), FontFamily = Theme.Font, FontSize = 11, Foreground = Theme.Ink, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 78, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), FlowDirection = FlowDirection.LeftToRight,
        };
        var stack = new StackPanel();
        stack.Children.Add(image);
        stack.Children.Add(name);
        var tile = new Border
        {
            Child = stack, Padding = new Thickness(6), Margin = new Thickness(3), CornerRadius = new CornerRadius(9), Width = 90,
            Background = selected ? Theme.AccentSoft : Brushes.Transparent, ToolTip = L10n.T($"{Path.GetFileName(file)}. Double-click to open; drag to carry the file."),
        };
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { if (!_selected.Remove(file)) _selected.Add(file); }
            else if (!_selected.Contains(file)) { _selected.Clear(); _selected.Add(file); }
            _pressAt = e.GetPosition(this);
            _pressed = file;
            Draw();
        };
        tile.MouseMove += (_, e) =>
        {
            if (_pressAt is not { } start || _pressed == null || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _pressAt = null;
            var data = new DataObject(DataFormats.FileDrop, Targets().ToArray());
            DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        };
        tile.MouseLeftButtonUp += (_, _) => _pressAt = null;
        var menu = new ContextMenu { FlowDirection = Theme.Direction };
        void Item(string title, Action action) { var item = new MenuItem { Header = L10n.T(title) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Open", () => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }));
        Item("Show in File Explorer", () => FileActions.ShowInExplorer(file));
        Item("Copy Path", () => Core.Clipboard.CopyText(file));
        Item("Remove from Float", () => Remove([file]));
        tile.ContextMenu = menu;
        return tile;
    }

    /// <summary>Pins the Float as a narrow strip on the right edge of the screen; again to put it back.</summary>
    private void ToggleDock()
    {
        if (_docked)
        {
            Left = _undocked.Left; Top = _undocked.Top; Width = _undocked.Width; Height = _undocked.Height;
            _docked = false;
            return;
        }
        _undocked = new Rect(Left, Top, Width, Height);
        Native.GetCursorPos(out var point);
        var work = Native.WorkArea(point.X, point.Y);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        Width = 140;
        Height = Math.Min(560, (work.Bottom - work.Top) / scale - 40);
        Left = work.Right / scale - Width + 4;
        Top = work.Top / scale + ((work.Bottom - work.Top) / scale - Height) / 2;
        _docked = true;
    }
}
