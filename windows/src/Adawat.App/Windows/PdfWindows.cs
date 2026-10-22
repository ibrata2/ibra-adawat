using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Adawat.Core;
using Adawat.Core.Media;
using Adawat.Core.Pdf;

namespace Adawat.App.Windows;

/// <summary>A numbered list the user reorders before files are combined, starting in Explorer's selection order.</summary>
public static class OrderWindow
{
    public static Window MergePdfs(IReadOnlyList<string> files) =>
        Make("Merge PDFs", "Document order", files, order => Runner.Run("Merge PDFs", order, PdfOps.Merge));

    public static Window JoinVideos(IReadOnlyList<string> files) =>
        Make("Join Videos", "Playback order", files, order => Runner.Run("Join Videos", order, MediaOps.Join));

    private static Window Make(string title, string caption, IReadOnlyList<string> files, Action<IReadOnlyList<string>> done)
    {
        var items = files.ToList();
        var thumbnails = files.Distinct().ToDictionary(f => f, f => ShellThumbnail.For(f, 64));
        var window = new ToolWindow(title, 400);
        var list = new StackPanel();
        void Build()
        {
            list.Children.Clear();
            for (var index = 0; index < items.Count; index++)
            {
                var i = index;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var number = UI.Text(L10n.T($"{index + 1}"), 12, color: Theme.Muted);
                row.Children.Add(number);
                var picture = new Image { Source = thumbnails[items[i]], Width = 34, Height = 26, Stretch = Stretch.Uniform };
                Grid.SetColumn(picture, 1);
                row.Children.Add(picture);
                var name = UI.Text(Path.GetFileName(items[i]), 12);
                name.Text = Path.GetFileName(items[i]);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(name, 2);
                row.Children.Add(name);
                var arrows = new StackPanel();
                Button Arrow(string glyph, bool enabled, int by)
                {
                    var button = UI.IconButton(glyph, () => { (items[i], items[i + by]) = (items[i + by], items[i]); Build(); }, by < 0 ? "Move up" : "Move down", 18);
                    button.Width = 32;
                    button.Height = 15;
                    button.Margin = new Thickness(0, 1, 0, 1);
                    button.IsEnabled = enabled;
                    ((TextBlock)button.Content).FontSize = 8;
                    return button;
                }
                arrows.Children.Add(Arrow("", i > 0, -1));
                arrows.Children.Add(Arrow("", i < items.Count - 1, 1));
                Grid.SetColumn(arrows, 3);
                row.Children.Add(arrows);
                list.Children.Add(new Border { Background = Theme.Card, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 8, 12, 8), Child = row, Margin = new Thickness(0, 0, 0, 6) });
            }
        }
        Build();
        var stack = new StackPanel();
        stack.Children.Add(UI.Text(caption, 12, color: Theme.Muted).Pad(0, 0));
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        window.Body = stack;
        var action = UI.Button(title, () => { window.Close(); done(items.ToList()); }, primary: true);
        action.IsDefault = true;
        window.Trailing(action);
        return window;
    }
}

/// <summary>Organize PDF: page cards to drag into a new order, turn, copy or delete, and an option to give every page the same width.</summary>
public static class OrganizeWindow
{
    private const string DragFormat = "Adawat.PageSlot";

    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var count = PdfOps.PageCount(file);
        var original = Enumerable.Range(0, count).Select(i => new PageSlot(i)).ToList();
        var slots = original.ToList();
        var thumbnails = new Dictionary<int, BitmapSource>();
        var equalWidths = false;
        var window = new ToolWindow("Organize PDF", 580);
        var grid = new WrapPanel { Margin = new Thickness(4) };
        var pageCount = UI.Text("", 11, color: Theme.Muted);
        var save = UI.Button("Save Organized PDF", () => { }, primary: true);
        var reset = UI.Button("Reset Order", () => { });
        bool Changed() => !slots.Select(s => s.Source).SequenceEqual(original.Select(s => s.Source)) || slots.Any(s => s.Rotation != 0);

        void Rotate(PageSlot slot) { var i = slots.IndexOf(slot); if (i >= 0) slots[i] = slot with { Rotation = (slot.Rotation + 90) % 360 }; Build(); }
        void Duplicate(PageSlot slot) { var i = slots.IndexOf(slot); if (i >= 0) slots.Insert(i + 1, new PageSlot(slot.Source, slot.Rotation)); Build(); }
        void Remove(PageSlot slot) { if (slots.Count > 1) { slots.Remove(slot); Build(); } }

        void Build()
        {
            grid.Children.Clear();
            pageCount.Text = L10n.T(slots.Count == 1 ? "1 page" : $"{slots.Count} pages");
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var card = new Grid { Width = 122, Height = 150, Margin = new Thickness(7), Background = Brushes.Transparent, AllowDrop = true, Cursor = Cursors.Hand };
                card.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8),
                                               Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.18 } });
                var picture = new Image { Margin = new Thickness(10), Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5),
                                          RenderTransform = new RotateTransform(slot.Rotation) };
                if (thumbnails.TryGetValue(slot.Source, out var thumb)) picture.Source = thumb;
                card.Children.Add(picture);
                var top = new DockPanel { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Top, LastChildFill = false };
                var number = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.Accent,
                                          Child = new TextBlock { Text = L10n.T($"{index + 1}"), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                DockPanel.SetDock(number, Dock.Left);
                top.Children.Add(number);
                var turn = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), ToolTip = L10n.T("Rotate clockwise"),
                                        Child = UI.Icon("", 10, Brushes.White) };
                ((TextBlock)turn.Child).HorizontalAlignment = HorizontalAlignment.Center;
                turn.MouseLeftButtonUp += (_, e) => { Rotate(slot); e.Handled = true; };
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
                Point? down = null;
                card.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(card);
                card.PreviewMouseMove += (_, e) =>
                {
                    if (down is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(card) - start).Length < 6) return;
                    down = null;
                    card.Opacity = 0.4;
                    DragDrop.DoDragDrop(card, new DataObject(DragFormat, slot.Id.ToString()), DragDropEffects.Move);
                    card.Opacity = 1;
                    Build();
                };
                card.DragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;
                };
                card.DragEnter += (_, e) =>
                {
                    if (e.Data.GetData(DragFormat) is not string id) return;
                    var moving = slots.FirstOrDefault(s => s.Id.ToString() == id);
                    if (moving == null || moving == slot) return;
                    var from = slots.IndexOf(moving);
                    var to = slots.IndexOf(slot);
                    slots.RemoveAt(from);
                    slots.Insert(to, moving);
                    // Move the card itself so the drag source stays alive while the order changes under the pointer.
                    var source = grid.Children[from];
                    grid.Children.RemoveAt(from);
                    grid.Children.Insert(to, source);
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
        reset.Click += (_, _) => { slots = original.ToList(); Build(); };
        DockPanel.SetDock(reset, Dock.Right);
        header.Children.Add(reset);
        stack.Children.Add(header);
        stack.Children.Add(new ScrollViewer { Content = grid, Height = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(UI.Divider());
        stack.Children.Add(UI.Check("Make all pages the same width", false, v => { equalWidths = v; save.IsEnabled = Changed() || equalWidths; }));
        stack.Children.Add(UI.Caption("Pages are scaled proportionally to match the narrowest page; their heights may still differ. Drag pages to reorder them; right-click for more.").Pad(0, 6));
        window.Body = stack;
        save.Click += (_, _) =>
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
                window.Dispatcher.BeginInvoke(() =>
                {
                    if (Pictures.FromBytes(png) is not { } bitmap) return;
                    thumbnails[index] = bitmap;
                    for (var i = 0; i < slots.Count && i < grid.Children.Count; i++)
                        if (slots[i].Source == index && grid.Children[i] is Grid card && card.Children[1] is Image picture) picture.Source = bitmap;
                });
            }
        });
        return window;
    }
}
