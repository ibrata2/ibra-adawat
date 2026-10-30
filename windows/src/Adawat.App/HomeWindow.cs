using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Adawat.Core;
using Microsoft.Win32;

namespace Adawat.App;

/// <summary>
/// IBMC's main window, laid out like a classic converter: a toolbar on top; on one side the sections (Video, Audio, Image,
/// PDF and documents, Tools), one open at a time, each a grid of tiles; on the other side the list of tasks with their
/// progress and results. A tile asks for files (or takes the files dropped on it, or the files waiting in the window) and
/// adds a task; tools with options open their window first.
/// </summary>
public sealed class HomeWindow : Window
{
    private static HomeWindow? _shared;

    private readonly Grid _sections = new();
    private readonly List<(Border Header, FrameworkElement Content, TextBlock Chevron)> _sectionParts = [];
    private readonly StackPanel _rows = new();
    private readonly Border _emptyTasks;
    private readonly Dictionary<Job, RowView> _views = [];
    private readonly HashSet<Job> _selected = [];
    private readonly Border _readyBar;
    private readonly TextBlock _readyText;
    private readonly TextBlock _outputText;
    private readonly TextBlock _countText;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private List<string> _ready = [];
    private int _open;
    private bool _quitting;

    public static bool IsShowing => _shared is { IsVisible: true } w && w.WindowState != WindowState.Minimized && w.IsActive;

    /// <summary>Shows the window (creating it once); <paramref name="files"/> wait in it for a tile to be chosen.</summary>
    public static void Present(IEnumerable<string>? files = null)
    {
        _shared ??= new HomeWindow();
        if (files?.ToList() is { Count: > 0 } list) _shared.SetReady(list);
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

    private static string T(string english, string arabic) => L10n.IsArabic ? arabic : english;

    private HomeWindow()
    {
        Title = "IBMC — Ibrahim Multimedia Converter";
        Width = 1180;
        Height = 760;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this);
        FontFamily = Theme.Font;
        // Minimise, maximise and close stay at the top right in Arabic too: only the content is mirrored.
        FlowDirection = FlowDirection.LeftToRight;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Adawat.ico"));
        AllowDrop = true;

        var root = new DockPanel { LastChildFill = true };

        // MARK: Toolbar

        var toolbar = new DockPanel { LastChildFill = false, Margin = new Thickness(10, 8, 10, 8) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) };
        brand.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon-256.png")), Width = 34, Height = 34 });
        var brandText = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "IBMC", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Theme.Ink, FlowDirection = FlowDirection.LeftToRight });
        brandText.Children.Add(new TextBlock { Text = "Ibrahim Multimedia Converter", FontSize = 10, Foreground = Theme.Muted, FlowDirection = FlowDirection.LeftToRight });
        brand.Children.Add(brandText);
        DockPanel.SetDock(brand, Dock.Left);
        toolbar.Children.Add(brand);

        var outputButton = ToolButton("", T("Output Folder", "مجلد الإخراج"), () => { });
        outputButton.Click += (_, _) => { var menu = OutputMenu(); menu.PlacementTarget = outputButton; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Separator());
        left.Children.Add(outputButton);
        left.Children.Add(ToolButton("", T("Settings", "الإعدادات"), SettingsWindow.Present));
        left.Children.Add(Separator());
        left.Children.Add(ToolButton("", T("Remove", "إزالة"), RemoveSelected));
        left.Children.Add(ToolButton("", T("Clear Finished", "مسح المنتهية"), Jobs.ClearFinished));
        left.Children.Add(Separator());
        _stopButton = ToolButton("", T("Stop", "إيقاف"), Jobs.Stop);
        left.Children.Add(_stopButton);
        _startButton = UI.Button(T("Start", "بدء"), Jobs.Start, primary: true, icon: "");
        _startButton.Margin = new Thickness(4, 0, 4, 0);
        left.Children.Add(_startButton);
        DockPanel.SetDock(left, Dock.Left);
        toolbar.Children.Add(left);
        var linkButton = ToolButton("", T("Download from Link", "تحميل من رابط"), Windows.IbraWindows.DownloadLink);
        DockPanel.SetDock(linkButton, Dock.Right);
        toolbar.Children.Add(linkButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var line = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(line);

        // MARK: Status bar

        var status = new DockPanel { LastChildFill = false, Margin = new Thickness(12, 5, 12, 6) };
        _outputText = UI.Text("", 11.5, color: Theme.Muted);
        _outputText.TextTrimming = TextTrimming.CharacterEllipsis;
        _outputText.MaxWidth = 520;
        DockPanel.SetDock(_outputText, Dock.Left);
        status.Children.Add(_outputText);
        _countText = UI.Text("", 11.5, color: Theme.Muted);
        DockPanel.SetDock(_countText, Dock.Right);
        status.Children.Add(_countText);
        var auto = UI.Check(T("Start tasks right away", "ابدأ المهام فوراً"), Settings.Current.AutoStart, v => { Settings.Current.AutoStart = v; Settings.Current.Save(); Refresh(); });
        auto.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(auto, Dock.Right);
        status.Children.Add(auto);
        var statusLine = new Border { Height = 1, Background = Theme.Hairline };
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(statusLine, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(statusLine);

        // MARK: Body

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560), MinWidth = 420 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });

        // Sections side: the "ready files" bar, then the accordion.
        var sectionsSide = new DockPanel { Margin = new Thickness(10, 10, 6, 10), LastChildFill = true };
        _readyText = UI.Text("", 12.5, FontWeights.SemiBold);
        _readyText.TextTrimming = TextTrimming.CharacterEllipsis;
        var readyRow = new DockPanel { LastChildFill = true };
        var readyClose = UI.IconButton("", () => SetReady([]), T("Clear", "مسح"), 24);
        DockPanel.SetDock(readyClose, Dock.Right);
        readyRow.Children.Add(readyClose);
        var readyIcon = UI.Icon("", 14, Theme.Accent);
        readyIcon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(readyIcon, Dock.Left);
        readyRow.Children.Add(readyIcon);
        readyRow.Children.Add(_readyText);
        _readyBar = new Border { Child = readyRow, Background = Theme.AccentSoft, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(_readyBar, Dock.Top);
        sectionsSide.Children.Add(_readyBar);
        BuildSections();
        sectionsSide.Children.Add(new Border { Child = _sections, Background = Theme.Card, CornerRadius = new CornerRadius(12), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), ClipToBounds = true });
        Grid.SetColumn(sectionsSide, 0);
        body.Children.Add(sectionsSide);

        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        // Tasks side: column headers and the rows.
        var tasksSide = new Border { Margin = new Thickness(6, 10, 10, 10), Background = Theme.Card, CornerRadius = new CornerRadius(12), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), ClipToBounds = true };
        var tasks = new DockPanel { LastChildFill = true };
        var header = RowGrid();
        header.Margin = new Thickness(10, 9, 10, 9);
        AddCell(header, 1, UI.Text(T("File", "الملف"), 12, FontWeights.SemiBold, Theme.Muted));
        AddCell(header, 2, UI.Text(T("Task", "العملية"), 12, FontWeights.SemiBold, Theme.Muted));
        AddCell(header, 3, UI.Text(T("Size", "الحجم"), 12, FontWeights.SemiBold, Theme.Muted));
        AddCell(header, 4, UI.Text(T("Output / Status", "الإخراج / الحالة"), 12, FontWeights.SemiBold, Theme.Muted));
        var headerHost = new Border { Child = header, Background = Theme.ButtonFill, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(headerHost, Dock.Top);
        tasks.Children.Add(headerHost);
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        hint.Children.Add(UI.Icon("", 44, Theme.Accent).Pad(0, 6));
        hint.Children.Add(new TextBlock { Text = T("No tasks yet", "ماكو مهام بعد"), FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center });
        hint.Children.Add(new TextBlock
        {
            Text = T("Pick a tool on the side, or drop files here, or right-click any file in Explorer and choose IBMC.",
                     "اختار أداة من الأقسام، أو اسحب الملفات هنا، أو كليك يمين على أي ملف واختار IBMC."),
            FontSize = 12, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 340, Margin = new Thickness(0, 6, 0, 0),
        });
        _emptyTasks = new Border { Child = hint };
        var listGrid = new Grid();
        listGrid.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        listGrid.Children.Add(_emptyTasks);
        tasks.Children.Add(listGrid);
        tasksSide.Child = tasks;
        Grid.SetColumn(tasksSide, 2);
        body.Children.Add(tasksSide);
        root.Children.Add(body);
        root.FlowDirection = Theme.Direction;
        Content = root;

        // Files dropped anywhere outside a tile wait for a tile; their section opens.
        DragEnter += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) SetReady(files); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && System.Windows.Clipboard.ContainsFileDropList())
                SetReady(System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList());
            if (e.Key == Key.Delete) RemoveSelected();
        };
        Jobs.All.CollectionChanged += OnJobsChanged;
        Jobs.QueueChanged += () => Dispatcher.BeginInvoke(Refresh);
        foreach (var job in Jobs.All) AddRow(job);
        // Closing hides; IBMC stays next to the clock for the right-click menu.
        Closing += (_, e) => { if (!_quitting) { e.Cancel = true; Hide(); } };
        Open(Math.Clamp(Settings.Current.HomeSection, 0, HomeCatalog.Sections.Count - 1));
        Refresh();
    }

    // MARK: Toolbar helpers

    private static Button ToolButton(string glyph, string title, Action click)
    {
        var button = UI.Button(title, click, icon: glyph);
        button.Margin = new Thickness(3, 0, 3, 0);
        return button;
    }

    private static Border Separator() => new() { Width = 1, Height = 22, Background = Theme.Hairline, Margin = new Thickness(6, 0, 6, 0) };

    private ContextMenu OutputMenu()
    {
        var settings = Settings.Current;
        var menu = new ContextMenu { FlowDirection = Theme.Direction };
        MenuItem Choice(string title, OutputLocation location)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = settings.OutputLocation == location };
            item.Click += (_, _) => { settings.OutputLocation = location; settings.Save(); Refresh(); };
            return item;
        }
        menu.Items.Add(Choice(T("Next to the original", "جنب الملف الأصلي"), OutputLocation.NextToOriginal));
        menu.Items.Add(Choice(T("Downloads folder", "مجلد التنزيلات"), OutputLocation.Downloads));
        menu.Items.Add(Choice(T("Desktop", "سطح المكتب"), OutputLocation.Desktop));
        var custom = new MenuItem
        {
            Header = settings.OutputLocation == OutputLocation.Custom && Directory.Exists(settings.CustomOutputFolder)
                ? $"{T("Folder", "فولدر")}: {Path.GetFileName(settings.CustomOutputFolder.TrimEnd('\\'))}" : T("Choose a folder…", "اختيار فولدر…"),
            IsCheckable = true, IsChecked = settings.OutputLocation == OutputLocation.Custom,
        };
        custom.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = T("Where should results be saved?", "وين تنحفظ النتائج؟") };
            if (dialog.ShowDialog(this) == true)
            {
                settings.CustomOutputFolder = dialog.FolderName;
                settings.OutputLocation = OutputLocation.Custom;
                settings.Save();
            }
            Refresh();
        };
        menu.Items.Add(custom);
        menu.Items.Add(new System.Windows.Controls.Separator());
        var open = new MenuItem { Header = T("Open the output folder", "فتح مجلد الإخراج"), IsEnabled = settings.OutputLocation != OutputLocation.NextToOriginal };
        open.Click += (_, _) => OpenFolder(OutputFolder());
        menu.Items.Add(open);
        return menu;
    }

    private static string OutputFolder() => Settings.Current.OutputLocation switch
    {
        OutputLocation.Downloads => Output.Downloads,
        OutputLocation.Desktop => Output.DesktopFolder,
        OutputLocation.Custom when Directory.Exists(Settings.Current.CustomOutputFolder) => Settings.Current.CustomOutputFolder,
        _ => "",
    };

    private static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // MARK: Sections

    private void BuildSections()
    {
        for (var index = 0; index < HomeCatalog.Sections.Count; index++)
        {
            var section = HomeCatalog.Sections[index];
            var color = new SolidColorBrush(section.Color);
            color.Freeze();
            var i = index;

            var head = new DockPanel { LastChildFill = true };
            var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = color, Margin = new Thickness(0, 0, 10, 0),
                                     Child = new TextBlock { Text = section.Glyph, FontFamily = Theme.Icons, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(badge, Dock.Left);
            head.Children.Add(badge);
            var chevron = UI.Icon("", 11, Theme.Muted);
            DockPanel.SetDock(chevron, Dock.Right);
            head.Children.Add(chevron);
            var count = UI.Text(L10n.IsArabic ? $"{section.Tiles.Count} أداة" : $"{section.Tiles.Count} tools", 11, color: Theme.Muted);
            count.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(UI.Text(section.Title, 14, FontWeights.SemiBold));
            var header = new Border { Child = head, Padding = new Thickness(10, 7, 12, 7), Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0) };
            header.MouseEnter += (_, _) => { if (_open != i) header.Background = Theme.ButtonHover; };
            header.MouseLeave += (_, _) => { if (_open != i) header.Background = Brushes.Transparent; };
            header.MouseLeftButtonUp += (_, _) => Open(i);

            var tiles = new WrapPanel { Margin = new Thickness(6, 2, 6, 8) };
            foreach (var tile in section.Tiles) tiles.Children.Add(Tile(section, tile, color));
            var content = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };

            _sections.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _sections.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            Grid.SetRow(header, index * 2);
            Grid.SetRow(content, index * 2 + 1);
            _sections.Children.Add(header);
            _sections.Children.Add(content);
            _sectionParts.Add((header, content, chevron));
        }
    }

    private void Open(int index)
    {
        _open = index;
        for (var i = 0; i < _sectionParts.Count; i++)
        {
            var (header, content, chevron) = _sectionParts[i];
            var open = i == index;
            content.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _sections.RowDefinitions[i * 2 + 1].Height = open ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            header.Background = open ? Theme.Selected : Brushes.Transparent;
            chevron.Text = open ? "" : "";
        }
        if (Settings.Current.HomeSection != index) { Settings.Current.HomeSection = index; Settings.Current.Save(); }
    }

    private Border Tile(HomeSection section, HomeTile tile, Brush color)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (tile.Badge is { } badge)
        {
            // A file sheet with the format name on a coloured band, like a file icon.
            var sheet = new Grid { Width = 44, Height = 52, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
            sheet.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Theme.IsDark ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) : Brushes.White, BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1) });
            sheet.Children.Add(new Border
            {
                Background = color, CornerRadius = new CornerRadius(4), Margin = new Thickness(-6, 0, 6, 0), Height = 20, VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(5, 0, 5, 0), MinWidth = 34,
                Child = new TextBlock { Text = badge.Length > 5 ? badge[..4] : badge.ToUpperInvariant(), FontSize = badge.Length > 4 ? 9.5 : 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
            });
            sheet.Children.Add(new TextBlock { Text = section.Glyph, FontFamily = Theme.Icons, FontSize = 18, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(sheet);
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = tile.Glyph, FontFamily = Theme.Icons, FontSize = 30, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6) });
        }
        stack.Children.Add(new TextBlock
        {
            Text = tile.Title, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Theme.Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxWidth = 98, MaxHeight = 34, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var border = new Border
        {
            Width = 112, Height = 112, Margin = new Thickness(4), CornerRadius = new CornerRadius(10), Child = stack, Cursor = Cursors.Hand,
            Background = Theme.ButtonFill, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1.5), ToolTip = tile.Title, AllowDrop = true,
        };
        border.MouseEnter += (_, _) => { border.Background = Theme.ButtonHover; border.BorderBrush = color; };
        border.MouseLeave += (_, _) => { border.Background = Theme.ButtonFill; border.BorderBrush = Brushes.Transparent; };
        border.MouseLeftButtonDown += (_, _) => border.Background = Theme.ButtonPressed;
        border.MouseLeftButtonUp += (_, _) => { border.Background = Theme.ButtonHover; Choose(section, tile, null); };
        border.DragEnter += (_, e) => { border.BorderBrush = color; e.Handled = true; };
        border.DragLeave += (_, _) => border.BorderBrush = Brushes.Transparent;
        border.Drop += (_, e) =>
        {
            border.BorderBrush = Brushes.Transparent;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Choose(section, tile, files);
            e.Handled = true;
        };
        return border;
    }

    // MARK: Choosing a tile

    private void Choose(HomeSection section, HomeTile tile, IReadOnlyList<string>? dropped)
    {
        if (tile.Direct is { } direct) { direct(); return; }
        var kinds = tile.Kinds ?? section.Kinds;
        bool Fits(string f) => kinds.Length == 0 || kinds.Contains(FileKinds.Of(f));
        var files = dropped?.ToList() ?? (_ready.Where(Fits).ToList() is { Count: > 0 } ready ? ready : null);
        if (files == null)
        {
            var dialog = new OpenFileDialog { Multiselect = true, Title = tile.Title, Filter = $"{section.DialogFilter}|{T("All files", "كل الملفات")}|*.*" };
            if (tile.Kinds?.Contains(FileKind.Subtitle) == true) dialog.Filter = $"{T("Video and subtitles", "فيديو وترجمة")}|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.srt;*.vtt";
            if (tile.Id == "ibra.slideshow") dialog.Filter = $"{T("Pictures and a song", "صور وأغنية")}|*.jpg;*.jpeg;*.png;*.webp;*.heic;*.mp3;*.m4a;*.wav;*.aac";
            if (dialog.ShowDialog(this) != true) return;
            files = dialog.FileNames.ToList();
        }
        var fitting = files.Where(Fits).ToList();
        if (fitting.Count == 0) { Hud.Shared.ShowMessage(T("These files don't fit this tool", "هاي الملفات ما تناسب هذه الأداة"), Hud.Style.Failure); return; }
        if (fitting.Count < tile.MinFiles)
        {
            Hud.Shared.ShowMessage(L10n.IsArabic ? $"هذه الأداة تحتاج {tile.MinFiles} ملفات على الأقل" : $"This tool needs at least {tile.MinFiles} files", Hud.Style.Failure);
            return;
        }
        if (dropped == null && _ready.Count > 0) SetReady([]);

        if (tile.Together || fitting.Count == 1)
        {
            var op = Resolve(tile, fitting);
            if (op == null) { NotForThese(); return; }
            Runner.Queue(op, fitting);
            return;
        }
        // Several files: a tool that takes them all at once in its window gets them all; otherwise one task per file.
        var all = Resolve(tile, fitting);
        if (all is { Window: not null }) { Runner.Queue(all, fitting); return; }
        var added = 0;
        foreach (var file in fitting)
        {
            var op = Resolve(tile, [file]);
            if (op == null) continue;
            Runner.Queue(op, [file]);
            added++;
            if (op.Window != null) break; // one options window at a time
        }
        if (added == 0) NotForThese();
    }

    private static AdawatOperation? Resolve(HomeTile tile, IReadOnlyList<string> files)
    {
        if (tile.Id == "archive.zip")
            return new AdawatOperation("archive.zip", "To ZIP", Catalog.Icons.Archive, (f, c) => ArchiveOps.Pack(f, "zip", c)) { Badge = "ZIP" };
        return Catalog.Find(tile.Id, files);
    }

    private void NotForThese() => Hud.Shared.ShowMessage(T("This tool doesn't apply to these files", "هذه الأداة ما تنطبق على هاي الملفات"), Hud.Style.Failure);

    private void SetReady(IReadOnlyList<string> files)
    {
        _ready = files.Where(f => File.Exists(f) || Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _readyBar.Visibility = _ready.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_ready.Count == 0) return;
        var what = _ready.Count == 1 ? Path.GetFileName(_ready[0].TrimEnd('\\')) : (L10n.IsArabic ? $"{_ready.Count} ملفات" : $"{_ready.Count} files");
        _readyText.Text = L10n.IsArabic ? $"{what} — اختار أداة" : $"{what} — pick a tool";
        Open(HomeCatalog.SectionFor(_ready));
    }

    // MARK: Tasks

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var job in _views.Keys.Where(j => !Jobs.All.Contains(j)).ToList())
            {
                _rows.Children.Remove(_views[job].Root);
                _views.Remove(job);
                _selected.Remove(job);
            }
            for (var i = 0; i < Jobs.All.Count; i++)
            {
                var job = Jobs.All[i];
                if (!_views.ContainsKey(job)) AddRow(job);
                var root = _views[job].Root;
                var at = _rows.Children.IndexOf(root);
                if (at != i) { _rows.Children.Remove(root); _rows.Children.Insert(Math.Min(i, _rows.Children.Count), root); }
            }
            Refresh();
        });
    }

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        return grid;
    }

    private static void AddCell(Grid grid, int column, UIElement element)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private sealed class RowView
    {
        public required Border Root;
        public required TextBlock StateIcon;
        public required TextBlock Status;
        public required ProgressBar Bar;
    }

    private void AddRow(Job job)
    {
        if (_views.ContainsKey(job)) return;
        var grid = RowGrid();
        var icon = UI.Icon("", 14, Theme.Muted);
        AddCell(grid, 0, icon);
        var name = UI.Text(job.SourceText, 12.5);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = string.Join("\n", job.Files.Take(20));
        name.Margin = new Thickness(0, 0, 8, 0);
        AddCell(grid, 1, name);
        var task = UI.Text(job.Operation.Title, 12.5, color: Theme.Muted);
        task.TextTrimming = TextTrimming.CharacterEllipsis;
        task.Margin = new Thickness(0, 0, 8, 0);
        AddCell(grid, 2, task);
        var size = UI.Text(job.Size > 0 ? Format.Bytes(job.Size) : "—", 12, color: Theme.Muted);
        size.FlowDirection = FlowDirection.LeftToRight;
        size.HorizontalAlignment = Theme.Direction == FlowDirection.RightToLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        AddCell(grid, 3, size);
        var statusStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var status = UI.Text("", 12);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        statusStack.Children.Add(status);
        var bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 4, 0, 0), Foreground = Theme.Accent, Background = Theme.ButtonFill, BorderThickness = new Thickness(0) };
        statusStack.Children.Add(bar);
        AddCell(grid, 4, statusStack);
        var root = new Border { Child = grid, Padding = new Thickness(10, 8, 10, 8), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var view = new RowView { Root = root, StateIcon = icon, Status = status, Bar = bar };
        _views[job] = view;
        root.MouseEnter += (_, _) => { if (!_selected.Contains(job)) root.Background = Theme.ButtonHover; };
        root.MouseLeave += (_, _) => { if (!_selected.Contains(job)) root.Background = Brushes.Transparent; };
        root.MouseLeftButtonUp += (_, _) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) _selected.Clear();
            if (!_selected.Add(job)) _selected.Remove(job);
            PaintSelection();
        };
        root.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) OpenResult(job); };
        root.ContextMenu = RowMenu(job);
        job.Changed += () => Dispatcher.BeginInvoke(() => Paint(job));
        _rows.Children.Add(root);
        Paint(job);
    }

    private ContextMenu RowMenu(Job job)
    {
        var menu = new ContextMenu { FlowDirection = Theme.Direction };
        MenuItem Item(string title, string glyph, Action action)
        {
            var item = new MenuItem { Header = title, Icon = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 13 } };
            item.Click += (_, _) => action();
            return item;
        }
        var openResult = Item(T("Open the result", "فتح الناتج"), "", () => OpenResult(job));
        var showResult = Item(T("Show in folder", "إظهار بالمجلد"), "", () =>
        {
            var target = job.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p)) ?? job.Files.FirstOrDefault();
            if (target != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
        });
        var retry = Item(T("Run again", "إعادة التشغيل"), "", () => Jobs.Retry(job));
        menu.Items.Add(openResult);
        menu.Items.Add(showResult);
        menu.Items.Add(retry);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(Item(T("Remove from the list", "إزالة من القائمة"), "", () => Jobs.Remove(job)));
        menu.Opened += (_, _) =>
        {
            openResult.IsEnabled = job.Outputs.Count > 0;
            retry.IsEnabled = job.State is JobState.Done or JobState.Failed or JobState.Stopped;
        };
        return menu;
    }

    private static void OpenResult(Job job)
    {
        var target = job.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (target == null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception) { }
    }

    private void Paint(Job job)
    {
        if (!_views.TryGetValue(job, out var view)) return;
        view.Status.Text = job.Status;
        view.Bar.Value = job.Progress;
        view.Bar.Visibility = job.State == JobState.Running ? Visibility.Visible : Visibility.Collapsed;
        view.Bar.IsIndeterminate = job.State == JobState.Running && job.Progress <= 0;
        (view.StateIcon.Text, view.StateIcon.Foreground, view.Status.Foreground) = job.State switch
        {
            JobState.Waiting => ("", Theme.Muted, Theme.Muted),
            JobState.Running => ("", Theme.Accent, Theme.Ink),
            JobState.Done => ("", Ok, Ok),
            JobState.Failed => ("", Bad, Bad),
            _ => ("", Theme.Muted, Theme.Muted),
        };
        view.Status.ToolTip = job.Outputs.Count > 0 ? string.Join("\n", job.Outputs.Take(20)) : job.Status;
    }

    private static readonly Brush Ok = Frozen(Color.FromRgb(0x22, 0xA8, 0x5A));
    private static readonly Brush Bad = Frozen(Color.FromRgb(0xE0, 0x3A, 0x3A));
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void PaintSelection()
    {
        foreach (var (job, view) in _views) view.Root.Background = _selected.Contains(job) ? Theme.Selected : Brushes.Transparent;
    }

    private void RemoveSelected()
    {
        foreach (var job in _selected.ToList()) Jobs.Remove(job);
        _selected.Clear();
    }

    private void Refresh()
    {
        _emptyTasks.Visibility = Jobs.All.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var done = Jobs.All.Count(j => j.State == JobState.Done);
        var waiting = Jobs.All.Count(j => j.State == JobState.Waiting);
        var running = Jobs.All.Count(j => j.State == JobState.Running);
        _countText.Text = L10n.IsArabic
            ? $"{Jobs.All.Count} مهام · {running} شغالة · {waiting} بالانتظار · {done} منجزة"
            : $"{Jobs.All.Count} tasks · {running} running · {waiting} waiting · {done} done";
        _startButton.IsEnabled = Jobs.AnyWaiting;
        _stopButton.IsEnabled = Jobs.AnyRunning;
        var folder = OutputFolder();
        _outputText.Text = (L10n.IsArabic ? "مجلد الإخراج: " : "Output: ") + (folder.Length > 0 ? folder : T("next to each original", "جنب كل ملف أصلي"));
    }
}
