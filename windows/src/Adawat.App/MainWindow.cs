using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Adawat.Core;
using Microsoft.Win32;

namespace Adawat.App;

/// <summary>
/// The app's own window: drop files on it (or add them with the buttons), then pick a tool, a conversion or an action
/// on the right. With files selected in the list, the choice applies to those; otherwise to every file in the list.
/// Closing it hides it; the app stays next to the clock for the right-click menu.
/// </summary>
public sealed class MainWindow : Window
{
    private static MainWindow? _shared;

    private readonly ObservableCollection<string> _files = [];
    private readonly ListBox _list = new();
    private readonly Border _dropZone;
    private readonly Grid _filesPanel = new();
    private readonly StackPanel _operations = new();
    private readonly List<Border> _tabs = [];
    private readonly TextBlock _count;
    private readonly TextBlock _scopeNote;
    private WheelMode _mode = WheelMode.Tools;
    private bool _quitting;

    /// <summary>Shows the window (creating it once) and adds <paramref name="files"/> to it.</summary>
    public static void Present(IEnumerable<string>? files = null)
    {
        _shared ??= new MainWindow();
        if (files != null) _shared.Add(files);
        if (!_shared.IsVisible) _shared.Show();
        if (_shared.WindowState == WindowState.Minimized) _shared.WindowState = WindowState.Normal;
        _shared.Activate();
        _shared.Topmost = true;
        _shared.Topmost = false;
    }

    public static void Quit()
    {
        if (_shared != null) { _shared._quitting = true; _shared.Close(); }
    }

    private MainWindow()
    {
        Title = L10n.T("IBRA Adawat");
        Width = 920;
        Height = 600;
        MinWidth = 720;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Surface;
        Foreground = Theme.Ink;
        FontFamily = Theme.Font;
        FlowDirection = Theme.Direction;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Adawat.ico"));
        AllowDrop = true;
        SourceInitialized += (_, _) => Native.MatchTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle, Theme.IsDark);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        // MARK: Files side

        var left = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var top = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        _count = UI.Text("Files", 15, FontWeights.SemiBold);
        DockPanel.SetDock(_count, Dock.Left);
        top.Children.Add(_count);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(UI.Button("Add Files…", AddFilesDialog, icon: "").Pad(4, 0));
        buttons.Children.Add(UI.Button("Add Folder…", AddFolderDialog, icon: "").Pad(4, 0));
        buttons.Children.Add(UI.IconButton("", Clear, "Clear the list", 28).Pad(4, 0));
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);

        // Empty: a big drop zone. With files: the list.
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        hint.Children.Add(UI.Icon("", 44, Theme.Accent).Pad(0, 6));
        hint.Children.Add(new TextBlock { Text = L10n.T("Drop files here"), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center });
        hint.Children.Add(new TextBlock { Text = L10n.T("or add them with the buttons above, or right-click any file in Explorer and choose IBRA Adawat"),
                                          FontSize = 12, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340, Margin = new Thickness(0, 6, 0, 14) });
        var pick = UI.Button("Choose Files…", AddFilesDialog, primary: true, icon: "");
        pick.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Children.Add(pick);
        _dropZone = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Theme.Hairline, Background = Theme.Card,
            Child = hint, Cursor = Cursors.Hand,
        };
        _dropZone.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not ButtonBase) AddFilesDialog(); };

        _list.ItemsSource = _files;
        _list.SelectionMode = SelectionMode.Extended;
        _list.Background = Theme.Card;
        _list.BorderBrush = Theme.Hairline;
        _list.Foreground = Theme.Ink;
        _list.ItemTemplate = FileTemplate();
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) => Refresh();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
        _list.MouseDoubleClick += (_, _) =>
        {
            if (_list.SelectedItem is string file) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); } catch (Exception) { }
        };
        var listMenu = new ContextMenu { FlowDirection = Theme.Direction };
        MenuItem Item(string title, Action action) { var item = new MenuItem { Header = L10n.T(title) }; item.Click += (_, _) => action(); return item; }
        listMenu.Items.Add(Item("Remove from the list", RemoveSelected));
        listMenu.Items.Add(Item("Show in folder", () =>
        {
            if (_list.SelectedItem is string file) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
        }));
        _list.ContextMenu = listMenu;

        _filesPanel.Children.Add(_list);
        _filesPanel.Children.Add(_dropZone);
        left.Children.Add(_filesPanel);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var divider = new Border { Background = Theme.Hairline };
        Grid.SetColumn(divider, 1);
        root.Children.Add(divider);

        // MARK: Tools side

        var right = new DockPanel { Margin = new Thickness(12, 16, 12, 12), LastChildFill = true };
        var tabs = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (mode, title, icon) in new[] { (WheelMode.Tools, "Tools", ""), (WheelMode.Convert, "Convert", ""), (WheelMode.Actions, "Actions", "") })
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(UI.Icon(icon, 13));
            content.Children.Add(UI.Text(title, 13, FontWeights.SemiBold).Pad(6, 0));
            var tab = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(2), Cursor = Cursors.Hand, Child = content, Tag = mode };
            tab.MouseLeftButtonUp += (_, _) => { _mode = mode; Refresh(); };
            _tabs.Add(tab);
            tabs.Children.Add(tab);
        }
        var tabsHost = new Border { Background = Theme.ButtonFill, CornerRadius = new CornerRadius(10), Child = tabs, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(tabsHost, Dock.Top);
        right.Children.Add(tabsHost);
        _scopeNote = UI.Caption("");
        _scopeNote.Margin = new Thickness(4, 2, 4, 6);
        DockPanel.SetDock(_scopeNote, Dock.Top);
        right.Children.Add(_scopeNote);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(UI.Button("Download from Link…", Windows.IbraWindows.DownloadLink, icon: "").Pad(3, 0));
        footer.Children.Add(UI.Button("Settings", SettingsWindow.Present, icon: "").Pad(3, 0));
        DockPanel.SetDock(footer, Dock.Bottom);
        right.Children.Add(footer);

        right.Children.Add(new ScrollViewer { Content = _operations, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        Content = root;

        // Drag and drop from Explorer, the desktop or any app.
        DragEnter += (_, e) => { e.Effects = Accepts(e) ? DragDropEffects.Copy : DragDropEffects.None; Highlight(Accepts(e)); e.Handled = true; };
        DragOver += (_, e) => { e.Effects = Accepts(e) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        DragLeave += (_, _) => Highlight(false);
        Drop += (_, e) =>
        {
            Highlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped) Add(dropped);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) AddFilesDialog();
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && System.Windows.Clipboard.ContainsFileDropList())
                Add(System.Windows.Clipboard.GetFileDropList().Cast<string>());
        };
        // Closing hides; the right-click menu keeps working while the app runs next to the clock.
        Closing += (_, e) => { if (!_quitting) { e.Cancel = true; Hide(); } };
        Refresh();
    }

    private static bool Accepts(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Highlight(bool on)
    {
        _dropZone.BorderBrush = on ? Theme.Accent : Theme.Hairline;
        _list.BorderBrush = on ? Theme.Accent : Theme.Hairline;
    }

    public void Add(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (!(File.Exists(file) || Directory.Exists(file))) continue;
            if (!_files.Contains(file, StringComparer.OrdinalIgnoreCase)) _files.Add(file);
        }
        Refresh();
    }

    private void AddFilesDialog()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = L10n.T("Choose files") };
        if (dialog.ShowDialog(this) == true) Add(dialog.FileNames);
    }

    private void AddFolderDialog()
    {
        var dialog = new OpenFolderDialog { Multiselect = true, Title = L10n.T("Choose folders") };
        if (dialog.ShowDialog(this) == true) Add(dialog.FolderNames);
    }

    private void Clear()
    {
        _files.Clear();
        Refresh();
    }

    private void RemoveSelected()
    {
        foreach (var file in _list.SelectedItems.Cast<string>().ToList()) _files.Remove(file);
        Refresh();
    }

    /// <summary>The files a choice applies to: the selected ones, or all of them.</summary>
    private IReadOnlyList<string> Targets => _list.SelectedItems.Count > 0 ? _list.SelectedItems.Cast<string>().ToList() : _files.ToList();

    private void Refresh()
    {
        var empty = _files.Count == 0;
        _dropZone.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _count.Text = empty ? L10n.T("Files") : L10n.T(_files.Count == 1 ? "1 file" : $"{_files.Count} files");
        foreach (var tab in _tabs) tab.Background = (WheelMode)tab.Tag == _mode ? Theme.Selected : Brushes.Transparent;

        _operations.Children.Clear();
        var targets = Targets;
        if (targets.Count == 0)
        {
            _scopeNote.Text = "";
            _operations.Children.Add(UI.Caption("Add files to see what you can do with them.").Pad(6, 12));
            return;
        }
        _scopeNote.Text = _list.SelectedItems.Count > 0 && _list.SelectedItems.Count < _files.Count
            ? L10n.T($"For the {targets.Count} selected") : L10n.T(targets.Count == 1 ? "For this file" : $"For all {targets.Count} files");
        var operations = Catalog.For(_mode, targets);
        if (operations.Count == 0)
        {
            _operations.Children.Add(UI.Caption("Nothing here for these files.").Pad(6, 12));
            return;
        }
        foreach (var operation in operations)
            _operations.Children.Add(OperationRow.Make(operation, () =>
            {
                var files = Targets.ToArray();
                Runner.Start(operation, files);
            }));
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate(typeof(string));
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        grid.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 5, 4, 5));
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(TextBlock.FontFamilyProperty, Theme.Icons);
        icon.SetValue(TextBlock.FontSizeProperty, 16.0);
        icon.SetValue(TextBlock.ForegroundProperty, Theme.Accent);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 10, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new KindIcon() });
        grid.AppendChild(icon);
        var text = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new FileName() });
        name.SetValue(TextBlock.FontSizeProperty, 13.0);
        name.SetValue(TextBlock.ForegroundProperty, Theme.Ink);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new FileDetail() });
        detail.SetValue(TextBlock.FontSizeProperty, 11.0);
        detail.SetValue(TextBlock.ForegroundProperty, Theme.Muted);
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.AppendChild(detail);
        grid.AppendChild(text);
        template.VisualTree = grid;
        return template;
    }

    private sealed class FileName : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) =>
            value is string path ? (Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path) : "";
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => System.Windows.Data.Binding.DoNothing;
    }

    private sealed class FileDetail : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
        {
            if (value is not string path) return "";
            try
            {
                var folder = Path.GetDirectoryName(path) ?? "";
                return File.Exists(path) ? $"{Format.Bytes(new FileInfo(path).Length)} · {folder}" : folder;
            }
            catch (Exception) { return path; }
        }
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => System.Windows.Data.Binding.DoNothing;
    }

    private sealed class KindIcon : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is string path ? FileKinds.Of(path) switch
        {
            FileKind.Image => "",
            FileKind.Video => "",
            FileKind.Audio => "",
            FileKind.Pdf => "",
            FileKind.Folder => "",
            FileKind.Archive => "",
            _ => "",
        } : "";
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>One choice as a clickable row: its icon (or format badge), its name, and "…" when it opens options first.</summary>
public static class OperationRow
{
    public static Border Make(AdawatOperation operation, Action click)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement mark = operation.Badge is { } badge
            ? new Border
            {
                Background = Theme.AccentSoft, CornerRadius = new CornerRadius(5), Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock { Text = badge.ToUpperInvariant(), FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Theme.Ink, FlowDirection = FlowDirection.LeftToRight },
            }
            : UI.Icon(operation.Icon, 15, Theme.Accent);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
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
        row.MouseLeftButtonUp += (_, _) => click();
        return row;
    }
}
