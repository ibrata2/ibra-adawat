using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Adawat.Core;

namespace Adawat.App;

/// <summary>
/// The menu that opens from Explorer's right-click "IBRA أدوات": the selected files, three tabs (Tools, Convert, Actions)
/// and every choice that fits them as a list. A click runs the choice, or opens its options window first.
/// </summary>
public sealed class FileMenuWindow : Window
{
    private static FileMenuWindow? _current;

    private readonly IReadOnlyList<string> _files;
    private readonly StackPanel _list = new();
    private readonly List<Border> _tabs = [];
    private bool _closing;

    public static void Open(IReadOnlyList<string> files)
    {
        _current?.CloseMenu();
        _current = new FileMenuWindow(files);
        _current.ShowAtCursor();
    }

    private FileMenuWindow(IReadOnlyList<string> files)
    {
        _files = files;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 320;
        FontFamily = Theme.Font;
        FlowDirection = Theme.Direction;
        Title = "IBRA أدوات";

        var root = new DockPanel { LastChildFill = true };

        // Header: app name and what is selected.
        var header = new StackPanel { Margin = new Thickness(14, 12, 14, 8) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Accent, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(UI.Text("IBRA أدوات", 13, FontWeights.Bold));
        header.Children.Add(brand);
        var what = UI.Text(files.Count == 1 ? Path.GetFileName(files[0]) : L10n.T($"{files.Count} items"), 11.5, color: Theme.Muted);
        what.TextTrimming = TextTrimming.CharacterEllipsis;
        what.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(what);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Tabs.
        var tabs = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(10, 0, 10, 6) };
        foreach (var (mode, title, icon) in new[] { (WheelMode.Tools, "Tools", ""), (WheelMode.Convert, "Convert", ""), (WheelMode.Actions, "Actions", "") })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(UI.Icon(icon, 12));
            content.Children.Add(UI.Text(title, 12, FontWeights.SemiBold).Pad(5, 0));
            var tab = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 7, 6, 7), Margin = new Thickness(2), Cursor = Cursors.Hand, Child = content, Tag = mode, Background = Brushes.Transparent };
            tab.MouseLeftButtonUp += (_, _) => ShowMode(mode);
            _tabs.Add(tab);
            tabs.Children.Add(tab);
        }
        DockPanel.SetDock(tabs, Dock.Top);
        root.Children.Add(tabs);
        var line = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(line);

        var open = new Border { Padding = new Thickness(14, 9, 14, 11), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var openRow = new StackPanel { Orientation = Orientation.Horizontal };
        openRow.Children.Add(UI.Icon("\uE8A7", 12, Theme.Accent));
        openRow.Children.Add(UI.Text("Open in the IBRA Adawat window", 12, color: Theme.Muted).Pad(8, 0));
        open.Child = openRow;
        open.MouseEnter += (_, _) => open.Background = Theme.ButtonHover;
        open.MouseLeave += (_, _) => open.Background = Brushes.Transparent;
        open.MouseLeftButtonUp += (_, _) => { var f = _files.ToArray(); CloseMenu(); HomeWindow.Present(f); };
        var openLine = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(open, Dock.Bottom);
        DockPanel.SetDock(openLine, Dock.Bottom);
        root.Children.Add(open);
        root.Children.Add(openLine);

        root.Children.Add(new ScrollViewer
        {
            Content = _list, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6),
        });

        Content = new Border
        {
            Background = Theme.Surface, CornerRadius = new CornerRadius(14), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1),
            Child = root, Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45 },
        };

        // Tools first; when a file has no tools (a document, say), start on Convert.
        var start = Catalog.For(WheelMode.Tools, files).Count > 0 ? WheelMode.Tools : WheelMode.Convert;
        ShowMode(start);

        Deactivated += (_, _) => CloseMenu();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseMenu(); };
        Closed += (_, _) => { if (_current == this) _current = null; };
    }

    private void ShowMode(WheelMode mode)
    {
        foreach (var tab in _tabs) tab.Background = (WheelMode)tab.Tag == mode ? Theme.Selected : Brushes.Transparent;
        _list.Children.Clear();
        var operations = Catalog.For(mode, _files);
        if (operations.Count == 0)
        {
            _list.Children.Add(UI.Caption("Nothing here for these files.").Pad(10, 12));
            return;
        }
        foreach (var operation in operations) _list.Children.Add(Row(operation));
    }

    private FrameworkElement Row(AdawatOperation operation)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement mark = operation.Badge is { } badge
            ? new Border
            {
                Background = Theme.AccentSoft, CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = badge.ToUpperInvariant(), FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Theme.Ink, FlowDirection = FlowDirection.LeftToRight },
            }
            : UI.Icon(operation.Icon, 15, Theme.Accent);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(mark);
        var title = UI.Text(operation.Title, 13);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        if (operation.Window != null)
        {
            var more = UI.Text("…", 13, color: Theme.Muted);
            Grid.SetColumn(more, 2);
            grid.Children.Add(more);
        }
        var row = new Border { Child = grid, Padding = new Thickness(4, 9, 10, 9), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        row.MouseEnter += (_, _) => row.Background = Theme.ButtonHover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            var files = _files.ToArray();
            CloseMenu();
            Dispatcher.BeginInvoke(() => Runner.Start(operation, files), System.Windows.Threading.DispatcherPriority.Background);
        };
        return row;
    }

    private void ShowAtCursor()
    {
        Opacity = 0;
        Left = -10000;
        Top = -10000;
        base.Show();
        UpdateLayout();
        Native.GetCursorPos(out var cursor);
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        if (scale <= 0) scale = 1;
        var area = Native.WorkArea(cursor.X, cursor.Y);
        double w = ActualWidth * scale, h = ActualHeight * scale;
        var x = Math.Clamp(cursor.X - 10, area.Left, Math.Max(area.Left, area.Right - w));
        var y = Math.Clamp(cursor.Y - 10, area.Top, Math.Max(area.Top, area.Bottom - h));
        Left = x / scale;
        Top = y / scale;
        Opacity = 1;
        Activate();
        Topmost = true;
        Focus();
    }

    private void CloseMenu()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
