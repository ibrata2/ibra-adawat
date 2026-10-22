using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adawat.Core;
using Adawat.Core.Media;
using Adawat.Core.Pdf;

namespace Adawat.Linux.Windows;

/// <summary>A numbered list the user reorders before files are combined, starting in the file manager's selection order.</summary>
public static class OrderWindow
{
    public static Window MergePdfs(IReadOnlyList<string> files) =>
        Make("Merge PDFs", "Document order", files, order => Runner.Run("Merge PDFs", order, PdfOps.Merge));

    public static Window JoinVideos(IReadOnlyList<string> files) =>
        Make("Join Videos", "Playback order", files, order => Runner.Run("Join Videos", order, MediaOps.Join));

    private static Window Make(string title, string caption, IReadOnlyList<string> files, Action<IReadOnlyList<string>> done)
    {
        var items = files.ToList();
        var window = new ToolWindow(title, 400);
        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            for (var index = 0; index < items.Count; index++)
            {
                var i = index;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,46,*,Auto") };
                row.Children.Add(UI.Text(L10n.T($"{index + 1}"), 12, color: Look.Muted));
                Control picture = Thumbnails.For(items[i], Build) is { } thumb
                    ? new Image { Source = thumb, Width = 34, Height = 26, Stretch = Stretch.Uniform }
                    : UI.Icon(Thumbnails.KindIcon(items[i]), 16, Look.Muted);
                Grid.SetColumn(picture, 1);
                row.Children.Add(picture);
                var name = UI.Text("", 12);
                name.Text = Path.GetFileName(items[i]);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 2);
                row.Children.Add(name);
                var arrows = new StackPanel();
                SoftButton Arrow(string glyph, bool enabled, int by)
                {
                    var button = UI.IconButton(glyph, () => { (items[i], items[i + by]) = (items[i + by], items[i]); Build(); }, by < 0 ? "Move up" : "Move down", 18);
                    button.Width = 32;
                    button.Height = 16;
                    button.MinHeight = 16;
                    button.Margin = new Thickness(0, 1);
                    button.IsEnabled = enabled;
                    ((TextBlock)button.Child!).FontSize = 12;
                    return button;
                }
                arrows.Children.Add(Arrow("", i > 0, -1));
                arrows.Children.Add(Arrow("", i < items.Count - 1, 1));
                Grid.SetColumn(arrows, 3);
                row.Children.Add(arrows);
                list.Children.Add(new Border { Background = Look.Card, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 8), Child = row, Margin = new Thickness(0, 0, 0, 6) });
            }
        }
        Build();
        var stack = new StackPanel();
        stack.Children.Add(UI.Text(caption, 12, color: Look.Muted));
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        window.Body = stack;
        window.Trailing(UI.Button(title, () => { window.Close(); done(items.ToList()); }, primary: true));
        window.KeyDown += (_, e) => { if (e.Key == Key.Enter) { window.Close(); done(items.ToList()); } };
        return window;
    }
}

/// <summary>Organize PDF: page cards to drag into a new order, turn, copy or delete, and an option to give every page the same width.</summary>
public static class OrganizeWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var count = PdfOps.PageCount(file);
        var original = Enumerable.Range(0, count).Select(i => new PageSlot(i)).ToList();
        var slots = original.ToList();
        var thumbnails = new Dictionary<int, Bitmap>();
        var equalWidths = false;
        var window = new ToolWindow("Organize PDF", 580);
        var grid = new WrapPanel { Margin = new Thickness(4) };
        var pageCount = UI.Text("", 11, color: Look.Muted);
        var save = UI.Button("Save Organized PDF", () => { }, primary: true);
        var reset = UI.Button("Reset Order", () => { });
        bool Changed() => !slots.Select(s => s.Source).SequenceEqual(original.Select(s => s.Source)) || slots.Any(s => s.Rotation != 0);

        void Rotate(PageSlot slot) { var i = slots.IndexOf(slot); if (i >= 0) slots[i] = slot with { Rotation = (slot.Rotation + 90) % 360 }; Build(); }
        void Duplicate(PageSlot slot) { var i = slots.IndexOf(slot); if (i >= 0) slots.Insert(i + 1, new PageSlot(slot.Source, slot.Rotation)); Build(); }
        void Remove(PageSlot slot) { if (slots.Count > 1) { slots.Remove(slot); Build(); } }

        // Cards are dragged with the pointer held: the order changes live as the card passes over its neighbours.
        PageSlot? moving = null;
        Point? down = null;

        void Build()
        {
            grid.Children.Clear();
            pageCount.Text = L10n.T(slots.Count == 1 ? "1 page" : $"{slots.Count} pages");
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var card = new Panel { Width = 122, Height = 150, Margin = new Thickness(7), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Tag = slot };
                card.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), BoxShadow = Look.Shadow(6, 1, 0.18) });
                var picture = new Image { Margin = new Thickness(10), Stretch = Stretch.Uniform, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(slot.Rotation) };
                if (thumbnails.TryGetValue(slot.Source, out var thumb)) picture.Source = thumb;
                card.Children.Add(picture);
                var top = new DockPanel { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Top, LastChildFill = false };
                var number = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Look.Accent,
                    Child = new TextBlock { Text = L10n.T($"{index + 1}"), FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                DockPanel.SetDock(number, Dock.Left);
                top.Children.Add(number);
                var turnIcon = UI.Icon("", 10, Brushes.White);
                turnIcon.HorizontalAlignment = HorizontalAlignment.Center;
                var turn = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Child = turnIcon };
                ToolTip.SetTip(turn, L10n.T("Rotate clockwise"));
                turn.PointerPressed += (_, e) => e.Handled = true;
                turn.PointerReleased += (_, e) => { Rotate(slot); e.Handled = true; };
                DockPanel.SetDock(turn, Dock.Right);
                top.Children.Add(turn);
                card.Children.Add(top);
                var menu = new ContextMenu();
                MenuItem Item(string title, Action act, bool enabled = true) { var item = new MenuItem { Header = L10n.T(title), IsEnabled = enabled }; item.Click += (_, _) => act(); return item; }
                menu.Items.Add(Item("Rotate Clockwise", () => Rotate(slot)));
                menu.Items.Add(Item("Duplicate Page", () => Duplicate(slot)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Delete Page", () => Remove(slot), slots.Count > 1));
                card.ContextMenu = menu;
                card.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                    down = e.GetPosition(grid);
                    moving = slot;
                    e.Pointer.Capture(card);
                };
                card.PointerMoved += (_, e) =>
                {
                    if (moving == null || down is not { } start) return;
                    var at = e.GetPosition(grid);
                    if (Math.Abs(at.X - start.X) + Math.Abs(at.Y - start.Y) < 6) return;
                    var source = grid.Children.OfType<Panel>().FirstOrDefault(c => ReferenceEquals(c.Tag, moving));
                    if (source != null) source.Opacity = 0.4;
                    var target = grid.Children.OfType<Panel>().FirstOrDefault(c => c.Bounds.Inflate(7).Contains(at));
                    if (target?.Tag is not PageSlot over || over == moving || source == null) return;
                    var from = slots.IndexOf(moving);
                    var to = slots.IndexOf(over);
                    slots.RemoveAt(from);
                    slots.Insert(to, moving);
                    // Move the card itself so the pointer capture survives while the order changes under it.
                    grid.Children.RemoveAt(from);
                    grid.Children.Insert(to, source);
                };
                card.PointerReleased += (_, e) =>
                {
                    var wasMoving = moving != null && down is { } start && Math.Abs(e.GetPosition(grid).X - start.X) + Math.Abs(e.GetPosition(grid).Y - start.Y) >= 6;
                    moving = null;
                    down = null;
                    e.Pointer.Capture(null);
                    if (wasMoving) Build();
                };
                grid.Children.Add(card);
            }
            reset.IsEnabled = Changed();
            save.IsEnabled = Changed() || equalWidths;
        }

        var stack = new StackPanel();
        var header = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(pageCount, Dock.Left);
        header.Children.Add(pageCount);
        reset.Click += () => { slots = original.ToList(); Build(); };
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        stack.Children.Add(header);
        stack.Children.Add(new ScrollViewer { Content = grid, Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(UI.Divider());
        stack.Children.Add(UI.Check("Make all pages the same width", false, v => { equalWidths = v; save.IsEnabled = Changed() || equalWidths; }));
        stack.Children.Add(UI.Caption("Pages are scaled proportionally to match the narrowest page; their heights may still differ. Drag pages to reorder them; right-click for more.").Pad(0, 6));
        window.Body = stack;
        save.Click += () =>
        {
            var order = slots.ToList();
            var equal = equalWidths;
            window.Close();
            Runner.Run("Organize PDF", [file], async (f, c) => [await PdfOps.Organize(f[0], order, equal, c)]);
        };
        window.Trailing(save);
        Build();

        // Small page pictures arrive in the background, a few at a time.
        _ = Task.Run(() =>
        {
            for (var page = 0; page < Math.Min(count, 300); page++)
            {
                byte[] png;
                try { using var image = PdfOps.RenderPage(file, page, 36); png = image.ToByteArray(ImageMagick.MagickFormat.Png); }
                catch { continue; }
                var index = page;
                Dispatcher.UIThread.Post(() =>
                {
                    if (Pictures.FromBytes(png) is not { } bitmap) return;
                    thumbnails[index] = bitmap;
                    foreach (var card in grid.Children.OfType<Panel>())
                        if (card.Tag is PageSlot slot && slot.Source == index && card.Children[1] is Image picture) picture.Source = bitmap;
                });
            }
        });
        return window;
    }
}
