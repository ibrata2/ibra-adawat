using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Core.Images;
using ImageMagick;

using Orientation = System.Windows.Controls.Orientation;
using TextAlignment = System.Windows.TextAlignment;
using ImageMetadata = Adawat.Core.Images.ImageMetadata;

namespace Adawat.App.Windows;

/// <summary>Crop: a frame over the first photo, a ratio, and exact pixel sizes; the same share of each photo is kept for a selection.</summary>
public static class CropWindow
{
    public static readonly (double? Value, string Title)[] Ratios = [(null, "Free"), (1, "1:1"), (16.0 / 9, "16:9"), (9.0 / 16, "9:16"), (4.0 / 3, "4:3"), (3.0 / 4, "3:4")];

    public static Window Make(IReadOnlyList<string> files)
    {
        var (preview, width, height) = Pictures.LoadPreview(files[0]);
        var window = new ToolWindow("Crop Image", 620);
        var editor = new CropEditor(380) { Source = Pictures.ToBitmap(preview), PixelWidth = width, PixelHeight = height };
        var stack = new StackPanel();
        stack.Children.Add(editor);
        var w = new TextBox { Width = 70, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var h = new TextBox { Width = 70, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var updating = false;
        void ShowSize()
        {
            updating = true;
            w.Text = Math.Round(editor.Crop.Width * width).ToString(CultureInfo.InvariantCulture);
            h.Text = Math.Round(editor.Crop.Height * height).ToString(CultureInfo.InvariantCulture);
            updating = false;
        }
        editor.Changed += ShowSize;
        void Typed()
        {
            if (updating || !int.TryParse(w.Text, out var pw) || !int.TryParse(h.Text, out var ph) || pw <= 0 || ph <= 0) return;
            var cw = Math.Min(1, (double)pw / width);
            var ch = Math.Min(1, (double)ph / height);
            editor.SetCrop(new Rect(Math.Min(editor.Crop.X, 1 - cw), Math.Min(editor.Crop.Y, 1 - ch), cw, ch));
        }
        w.LostFocus += (_, _) => Typed();
        h.LostFocus += (_, _) => Typed();
        var ratio = UI.Picker(Ratios.Select(r => (r.Value, r.Title)).ToList(), null, editor.SetRatio);
        stack.Children.Add(UI.Row("Aspect ratio", ratio).Pad(0, 10));
        var size = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        size.Children.Add(UI.Text("W", 12, color: Theme.Muted).Pad(4, 0));
        size.Children.Add(w);
        size.Children.Add(UI.Text("H", 12, color: Theme.Muted).Pad(8, 0));
        size.Children.Add(h);
        size.Children.Add(UI.Text("px", 12, color: Theme.Muted).Pad(6, 0));
        stack.Children.Add(UI.Row("Size", size));
        if (files.Count > 1) stack.Children.Add(UI.Caption($"Uses this crop, as the same share of each image, for all {files.Count} images."));
        ShowSize();
        window.Body = stack;
        window.Leading(UI.Button("Reset", () => { editor.SetRatio(null); editor.SetCrop(new Rect(0, 0, 1, 1)); }));
        window.Trailing(UI.Button("Apply", () =>
        {
            var crop = editor.Crop;
            (int, int)? exact = files.Count == 1 && int.TryParse(w.Text, out var pw) && int.TryParse(h.Text, out var ph) ? (pw, ph) : null;
            window.Close();
            Runner.Run("Crop", files, (f, c) => ImageOps.Crop(f, (crop.X, crop.Y, crop.Width, crop.Height), c, exact));
        }, primary: true));
        return window;
    }
}

/// <summary>Add Background: gradients, colours, a picture, or a blurred copy of the photo, with padding, corners, shadow and a ratio.</summary>
public static class FrameWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var options = new FrameOptions();
        var (preview, width, _) = Pictures.LoadPreview(files[0], 900);
        var scale = (double)preview.Width / width;
        var window = new ToolWindow("Add Background", 600);
        var stage = new PictureStage(340) { Background = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)) };
        var stack = new StackPanel();
        stack.Children.Add(stage);
        var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        pending.Tick += async (_, _) =>
        {
            pending.Stop();
            var current = options;
            try
            {
                var bitmap = await Task.Run(() => { using var framed = Frames.Render(preview, current, scale); return Pictures.ToBitmap(framed); });
                stage.Source = bitmap;
            }
            catch (AdawatException e) { Hud.Shared.ShowMessage(e.Message, Hud.Style.Failure); }
        };
        void Refresh() { pending.Stop(); pending.Start(); }

        var swatches = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Swatch(Brush fill, FrameBackground background, string? glyph = null)
        {
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = fill, Margin = new Thickness(3), BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
            if (glyph != null) dot.Child = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Ink };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                if (background is FrameBackground.Picture)
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.heic;*.webp;*.avif;*.tif;*.tiff;*.bmp;*.gif", Title = L10n.T("Choose a background image") };
                    if (dialog.ShowDialog() != true) return;
                    background = new FrameBackground.Picture(dialog.FileName);
                }
                options = options with { Background = background };
                Refresh();
            };
            swatches.Children.Add(dot);
        }
        for (var i = 0; i < FrameBackground.Gradients.Length; i++)
        {
            var stops = FrameBackground.Gradients[i];
            Swatch(new LinearGradientBrush(Color.FromRgb((byte)(stops[0].R * 255), (byte)(stops[0].G * 255), (byte)(stops[0].B * 255)),
                                           Color.FromRgb((byte)(stops[1].R * 255), (byte)(stops[1].G * 255), (byte)(stops[1].B * 255)), 45), new FrameBackground.Gradient(i));
        }
        foreach (var color in FrameBackground.Solids)
            Swatch(new SolidColorBrush(Color.FromRgb((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255))), new FrameBackground.Solid(color));
        Swatch(Brushes.White, new FrameBackground.Blurred(), "");
        Swatch(Brushes.White, new FrameBackground.Picture(""), "");
        stack.Children.Add(UI.Row("Background", swatches));
        stack.Children.Add(UI.Row("Padding", UI.Slider(0, 600, options.Padding, v => { options = options with { Padding = v }; Refresh(); }, v => $"{Math.Round(v)} px")));
        stack.Children.Add(UI.Row("Corners", UI.Slider(0, 200, options.Corners, v => { options = options with { Corners = v }; Refresh(); }, v => $"{Math.Round(v)} px")));
        stack.Children.Add(UI.Row("Shadow", UI.Slider(0, 120, options.Shadow, v => { options = options with { Shadow = v }; Refresh(); }, v => $"{Math.Round(v)} px")));
        stack.Children.Add(UI.Row("Ratio", UI.Segmented(Enum.GetValues<FrameRatio>().Select(r => (r, r.Title())).ToList(), FrameRatio.Auto, r => { options = options with { Ratio = r }; Refresh(); })));
        if (files.Count > 1) stack.Children.Add(UI.Caption($"The same background is added to all {files.Count} images."));
        window.Body = stack;
        window.Leading(UI.Button("Reset", () => { window.Close(); ToolWindows.Open("frame", files); }));
        window.Trailing(UI.Button("Save with Background", () =>
        {
            var chosen = options;
            window.Close();
            Runner.Run("Add Background", files, (f, c) => Frames.Apply(f, chosen, c));
        }, primary: true));
        Refresh();
        return window;
    }
}

/// <summary>Collage: grid, row, column or featured layouts on a chosen canvas, with spacing, corners, background and order.</summary>
public static class CollageWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var order = files.ToList();
        var options = new CollageOptions { Columns = files.Count <= 4 ? 2 : 3 };
        var thumbs = order.ToDictionary(f => f, f => ImageEngine.Load(f, 900));
        var window = new ToolWindow("Create Collage", 640);
        var stage = new PictureStage(340) { Background = new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)) };
        var stack = new StackPanel();
        stack.Children.Add(stage);
        var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        pending.Tick += async (_, _) =>
        {
            pending.Stop();
            var current = options;
            var images = order.Select(f => thumbs[f]).ToList();
            stage.Source = await Task.Run(() => { using var collage = Collage.Render(images, current, 900.0 / current.Width); return Pictures.ToBitmap(collage); });
        };
        void Refresh() { pending.Stop(); pending.Start(); }
        stack.Children.Add(UI.Row("Layout", UI.Segmented([(CollageStyle.Grid, "Grid"), (CollageStyle.Row, "Row"), (CollageStyle.Column, "Column"), (CollageStyle.Featured, "Featured")], options.Style,
                                                         s => { options = options with { Style = s }; Refresh(); })).Pad(0, 6));
        stack.Children.Add(UI.Row("Columns", UI.Slider(1, Math.Max(2, files.Count), options.Columns, v => { options = options with { Columns = (int)Math.Round(v) }; Refresh(); }, v => Math.Round(v).ToString(), step: 1)));
        stack.Children.Add(UI.Row("Canvas", UI.Picker(Enum.GetValues<CollageCanvas>().Select(c => (c, c.Title())).ToList(), options.Canvas, c => { options = options with { Canvas = c }; Refresh(); })));
        stack.Children.Add(UI.Row("Width", UI.Picker([(1080, "1080 px"), (2048, "2048 px"), (3000, "3000 px"), (4096, "4096 px")], options.Width, v => { options = options with { Width = v }; Refresh(); })));
        stack.Children.Add(UI.Row("Image fit", UI.Picker([(true, "Fill cells (crop edges)"), (false, "Fit whole images")], true, v => { options = options with { Fill = v }; Refresh(); })));
        stack.Children.Add(UI.Row("Spacing", UI.Slider(0, 80, options.Spacing, v => { options = options with { Spacing = v }; Refresh(); }, v => $"{Math.Round(v)} px")));
        stack.Children.Add(UI.Row("Corners", UI.Slider(0, 120, options.Corners, v => { options = options with { Corners = v }; Refresh(); }, v => $"{Math.Round(v)} px")));
        var orderList = new StackPanel { Orientation = Orientation.Horizontal };
        void DrawOrder()
        {
            orderList.Children.Clear();
            for (var i = 0; i < order.Count; i++)
            {
                var index = i;
                var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
                cell.Children.Add(new Image { Source = Pictures.ToBitmap(Thumb(thumbs[order[i]])), Width = 44, Height = 44, Stretch = Stretch.UniformToFill });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                buttons.Children.Add(UI.IconButton("", () => { if (index > 0) { (order[index - 1], order[index]) = (order[index], order[index - 1]); DrawOrder(); Refresh(); } }, "Move", 20));
                buttons.Children.Add(UI.IconButton("", () => { if (index < order.Count - 1) { (order[index + 1], order[index]) = (order[index], order[index + 1]); DrawOrder(); Refresh(); } }, "Move", 20));
                cell.Children.Add(buttons);
                orderList.Children.Add(cell);
            }
        }
        DrawOrder();
        stack.Children.Add(UI.Row("Image order", new ScrollViewer { Content = orderList, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxWidth = 400 }).Pad(0, 6));
        window.Body = stack;
        window.Leading(UI.Button("Reset Layout", () => { options = new CollageOptions { Columns = files.Count <= 4 ? 2 : 3 }; order = files.ToList(); DrawOrder(); Refresh(); }));
        window.Trailing(UI.Button("Create Collage", () =>
        {
            var chosen = options;
            var sorted = order.ToList();
            window.Close();
            Runner.Run("Create Collage", sorted, (f, c) => Collage.Make(f, chosen, c));
        }, primary: true));
        window.Closed += (_, _) => { foreach (var t in thumbs.Values) t.Dispose(); };
        Refresh();
        return window;
    }

    private static MagickImage Thumb(MagickImage image)
    {
        var copy = (MagickImage)image.Clone();
        copy.Thumbnail(88, 88);
        return copy;
    }
}

/// <summary>Redact Photo: boxes drawn by hand, or found for faces and text, covered solid or blurred.</summary>
public static class RedactWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var (preview, _, _) = Pictures.LoadPreview(file);
        var window = new ToolWindow("Redact Photo", 640);
        var editor = new BoxEditor(400) { Source = Pictures.ToBitmap(preview) };
        var blur = false;
        var color = new Rgb(0, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(editor);
        var caption = UI.Caption("Draw rectangles over the areas to hide. Drag a box to move it, or its corners to resize.");
        stack.Children.Add(caption.Pad(0, 8));
        stack.Children.Add(UI.Row("Style", UI.Segmented([(false, "Solid"), (true, "Blur")], false, v =>
        {
            blur = v;
            editor.Fill = v ? new SolidColorBrush(Color.FromArgb(0x99, 0x80, 0x80, 0x80)) : new SolidColorBrush(Color.FromArgb(0xEE, 0, 0, 0));
            caption.Text = L10n.T(v ? "Blur softens details. Use Solid for sensitive information." : "Draw rectangles over the areas to hide. Drag a box to move it, or its corners to resize.");
            editor.Draw();
        })));
        async void Detect(bool faces)
        {
            Hud.Shared.ShowMessage(faces ? "Detecting faces…" : "Detecting text…", Hud.Style.Working);
            try
            {
                using var image = await ImageEngine.LoadAsync(file, 2400);
                var boxes = faces ? await Recognition.Faces(image) : await Recognition.TextBoxes(image);
                foreach (var (x, y, w, h) in boxes) editor.Add(new Rect(x / image.Width, y / image.Height, w / image.Width, h / image.Height));
                Hud.Shared.ShowMessage(boxes.Count == 0 ? (faces ? "No faces found" : "No text found") : faces ? $"Covered {boxes.Count} face{(boxes.Count == 1 ? "" : "s")}" : $"Covered {boxes.Count} text area{(boxes.Count == 1 ? "" : "s")}", Hud.Style.Success);
            }
            catch (Exception e) { Hud.Shared.ShowMessage(e.Message, Hud.Style.Failure); }
        }
        var detect = new StackPanel { Orientation = Orientation.Horizontal };
        detect.Children.Add(UI.Button("Faces", () => Detect(true), icon: ""));
        detect.Children.Add(UI.Button("Text", () => Detect(false), icon: "").Pad(6, 0));
        stack.Children.Add(UI.Row("Detect", detect));
        window.Body = stack;
        window.Leading(UI.Button("Delete Redaction", editor.DeleteSelected));
        window.Trailing(UI.Button("Save Redacted Copy", () =>
        {
            var boxes = editor.Boxes.Select(b => new ImageRedaction.Box(b.Rect.X, b.Rect.Y, b.Rect.Width, b.Rect.Height)).ToList();
            if (boxes.Count == 0) { Hud.Shared.ShowMessage("Draw at least one redaction.", Hud.Style.Failure); return; }
            window.Close();
            Runner.Run("Redact Photo", [file], async (f, c) => [await ImageRedaction.Save(f[0], boxes, blur, color, c)]);
        }, primary: true));
        return window;
    }
}

/// <summary>Edit Photo: light, colour, detail and effects sliders over a live preview; hold the preview to see the original.</summary>
public static class EditPhotoWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var (preview, width, _) = Pictures.LoadPreview(file, 1200);
        var scale = (double)preview.Width / width;
        var adjustments = new PhotoAdjustments();
        var original = Pictures.ToBitmap(preview);
        var window = new ToolWindow("Edit Photo", 680);
        var stage = new PictureStage(330) { Source = original };
        var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(10),
                                 HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = UI.Text("Original", 11, FontWeights.SemiBold, Brushes.White) };
        stage.Children.Add(badge);
        BitmapSourceHolder edited = new() { Bitmap = original };
        var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        var busy = false;
        pending.Tick += async (_, _) =>
        {
            if (busy) return;
            pending.Stop();
            busy = true;
            var copy = adjustments.Copy();
            edited.Bitmap = await Task.Run(() => { using var image = (MagickImage)preview.Clone(); copy.Apply(image, scale); return Pictures.ToBitmap(image); });
            stage.Source = edited.Bitmap;
            ((TextBlock)badge.Child).Text = L10n.T(copy.IsNeutral ? "Original" : "Edited");
            busy = false;
        };
        stage.MouseLeftButtonDown += (_, _) => { stage.Source = original; ((TextBlock)badge.Child).Text = L10n.T("Original"); };
        stage.MouseLeftButtonUp += (_, _) => { stage.Source = edited.Bitmap; ((TextBlock)badge.Child).Text = L10n.T(adjustments.IsNeutral ? "Original" : "Edited"); };
        stage.ToolTip = L10n.T("Hold to compare with the original");
        var sliders = new StackPanel();
        var resets = new List<Action>();
        foreach (var (title, items) in PhotoAdjustments.Sections)
        {
            sliders.Children.Add(UI.Text(title == "Light" && L10n.IsArabic ? "الإضاءة" : title, 11.5, FontWeights.SemiBold, Theme.Muted).Pad(0, 6));
            foreach (var item in items)
            {
                var control = UI.Slider(item.Min, item.Max, 0, v => { adjustments[item.Key] = item.Key == Adjust.Exposure ? Math.Round(v, 2) : Math.Round(v); pending.Stop(); pending.Start(); },
                                        v => item.Key == Adjust.Exposure ? v.ToString("0.00", CultureInfo.InvariantCulture) + item.Unit : Math.Round(v) + item.Unit, reset: 0, width: 280);
                resets.Add(() => ((Slider)control.Children[0]).Value = 0);
                sliders.Children.Add(UI.Row(item.Title, control, 28));
            }
        }
        var stack = new StackPanel();
        stack.Children.Add(stage);
        stack.Children.Add(new ScrollViewer { Content = sliders, Height = 250, Margin = new Thickness(0, 10, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Body = stack;
        window.Leading(UI.Button("Reset", () => { foreach (var reset in resets) reset(); }));
        window.Trailing(UI.Button("Save Edited Copy", () =>
        {
            var chosen = adjustments.Copy();
            window.Close();
            Runner.Run("Edit Photo", [file], async (f, c) => [await PhotoAdjustments.Save(f[0], chosen, c)]);
        }, primary: true));
        return window;
    }

    private sealed class BitmapSourceHolder { public System.Windows.Media.Imaging.BitmapSource? Bitmap; }
}

/// <summary>Metadata: every field grouped, with search; editable fields can be changed, and any can be removed.</summary>
public static class MetadataWindow
{
    public static Window Make(IReadOnlyList<string> files)
    {
        var file = files[0];
        var fields = ImageMetadata.Read(file);
        var window = new ToolWindow("Metadata", 560);
        var list = new StackPanel();
        var search = new TextBox { Height = 26, MinWidth = 200, VerticalContentAlignment = VerticalAlignment.Center };
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
        var name = UI.Text(Path.GetFileName(file), 12.5, color: Theme.Muted);
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(search, Dock.Right);
        header.Children.Add(name);
        header.Children.Add(search);
        void Draw()
        {
            list.Children.Clear();
            var query = search.Text.Trim();
            var visible = fields.Where(f => !f.Removed && (query.Length == 0 || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || f.Value.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            if (visible.Count == 0) list.Children.Add(UI.Caption("No matching metadata."));
            foreach (var group in visible.GroupBy(f => f.Group))
            {
                list.Children.Add(UI.Text(group.Key, 11.5, FontWeights.SemiBold, Theme.Muted).Pad(0, 6));
                foreach (var field in group)
                {
                    var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var label = UI.Text(L10n.T(field.Name) + ":", 12.5);
                    label.TextAlignment = TextAlignment.Right;
                    label.Margin = new Thickness(0, 0, 10, 0);
                    row.Children.Add(label);
                    FrameworkElement value;
                    if (field.Editable)
                    {
                        var box = new TextBox { Text = field.Value, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
                        box.TextChanged += (_, _) => field.Value = box.Text;
                        value = box;
                    }
                    else value = UI.Text(field.Value, 12.5);
                    Grid.SetColumn(value, 1);
                    row.Children.Add(value);
                    if (field.Group != "File")
                    {
                        var remove = UI.IconButton("", () => { field.Removed = true; Draw(); }, "Remove this field", 22);
                        Grid.SetColumn(remove, 2);
                        remove.Margin = new Thickness(8, 0, 0, 0);
                        row.Children.Add(remove);
                    }
                    list.Children.Add(row);
                }
            }
        }
        search.TextChanged += (_, _) => Draw();
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        stack.Children.Add(UI.Caption("For photos, Remove All also clears hidden camera and processing data. The original stays unchanged.").Pad(0, 10));
        Draw();
        window.Body = stack;
        window.Leading(UI.Button("Remove All", () =>
        {
            window.Close();
            Runner.Run("Remove Metadata", [file], async (f, c) => [await ImageMetadata.Save(f[0], fields, true, c)]);
        }));
        window.Trailing(UI.Button("Save", () =>
        {
            if (!fields.Any(f => f.Changed)) { window.Close(); return; }
            window.Close();
            Runner.Run("Edit Metadata", [file], async (f, c) => [await ImageMetadata.Save(f[0], fields, false, c)]);
        }, primary: true));
        return window;
    }
}
