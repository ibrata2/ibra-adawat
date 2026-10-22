using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Adawat.Core;
using Adawat.Core.Images;
using SkiaSharp;

using CoreColor = Adawat.Core.Graphics.Color;
using Path = Avalonia.Controls.Shapes.Path;

namespace Adawat.Linux.Windows;

/// <summary>A round colour swatch that opens a palette with a hex field.</summary>
public static class ColorWell
{
    private static readonly string[] Palette =
    [
        "#000000", "#3A3A3C", "#8E8E93", "#C7C7CC", "#FFFFFF", "#5B47E6",
        "#1E3A8A", "#2563EB", "#0EA5E9", "#14B8A6", "#16A34A", "#84CC16",
        "#FACC15", "#F59E0B", "#EA580C", "#DC2626", "#DB2777", "#9333EA",
        "#7C2D12", "#365314", "#0F766E", "#312E81", "#701A75", "#881337",
    ];

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Border Make(Color value, Action<Color> changed)
    {
        var swatch = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(value), BorderBrush = Look.Hairline,
            BorderThickness = new Thickness(1.5), Cursor = new Cursor(StandardCursorType.Hand),
        };
        var flyout = new Flyout { Placement = PlacementMode.Bottom };
        var grid = new UniformGrid { Columns = 6 };
        var hex = new TextBox { MinHeight = 24, Margin = new Thickness(0, 8, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, Text = Hex(value), FlowDirection = FlowDirection.LeftToRight };
        void Set(Color color)
        {
            swatch.Background = new SolidColorBrush(color);
            hex.Text = Hex(color);
            changed(color);
        }
        foreach (var code in Palette)
        {
            var color = Color.Parse(code);
            var dot = new Border { Width = 22, Height = 22, Margin = new Thickness(3), CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(color), BorderBrush = Look.Hairline, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
            dot.PointerReleased += (_, _) => { Set(color); flyout.Hide(); };
            grid.Children.Add(dot);
        }
        hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var text = hex.Text ?? "";
            if (Color.TryParse(text.StartsWith('#') ? text : "#" + text, out var color)) { Set(color); flyout.Hide(); }
        };
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(hex);
        flyout.Content = panel;
        FlyoutBase.SetAttachedFlyout(swatch, flyout);
        swatch.PointerReleased += (_, _) => { if (swatch.IsEnabled) FlyoutBase.ShowAttachedFlyout(swatch); };
        return swatch;
    }
}

/// <summary>Create QR Code: text, a link or Wi-Fi details; module and corner shapes, colours or a gradient, a logo in the middle,
/// and a live check that a phone can still read it.</summary>
public static class QrWindow
{
    private sealed class Model
    {
        public bool Wifi;
        public string Text = "";
        public string Network = "", Password = "";
        public QrContent.Security Security = QrContent.Security.Wpa;
        public bool Hidden;
        public QrMatrix.Correction Correction = QrMatrix.Correction.Medium;
        public QrDesign.ModuleShape Module = QrDesign.ModuleShape.Square;
        public QrDesign.EyeShape Eye = QrDesign.EyeShape.Square;
        public Color Foreground = Colors.Black, EyeColor = Colors.Black, GradientEnd = Look.AccentColor, Background = Colors.White;
        public bool SeparateEye, Gradient, Transparent;
        public int Margin = 4;
        public SKBitmap? Logo;
        public double LogoScale = 0.22;
        public QrDesign.Plate Plate = QrDesign.Plate.Rounded;
        public int Pixels = 1024;
        public bool Svg;

        public string Payload => !Wifi ? Text : Network.Length == 0 ? "" : QrContent.Wifi(Network, Password, Security, Hidden);
        public QrMatrix? Matrix => QrMatrix.Encode(Payload, Correction);

        private static CoreColor C(Color c) => CoreColor.FromArgb(c.A, c.R, c.G, c.B);

        public QrDesign Design => new()
        {
            Module = Module, Eye = Eye, Foreground = C(Foreground), GradientEnd = Gradient ? C(GradientEnd) : null, EyeColor = SeparateEye ? C(EyeColor) : C(Foreground),
            Background = Transparent ? null : C(Background), Margin = Margin, Logo = Logo, LogoScale = LogoScale, LogoPlate = Plate,
        };

        public string? Problem
        {
            get
            {
                if (Payload.Length == 0) return Wifi ? "Enter the network name." : "Type the text or link to encode.";
                return Matrix == null ? "This is too long for a QR code. Shorten it or lower the error correction." : null;
            }
        }
    }

    public static Window Make(IReadOnlyList<string> files)
    {
        var source = files.Count > 0 ? files[0] : null;
        var model = new Model();
        if (source != null) model.Text = QrContent.Text(source) ?? "";

        var window = new ToolWindow("Create QR Code", 720);
        var left = new StackPanel { Width = 360 };
        var preview = new Image { Width = 258, Height = 258, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(preview, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        var placeholder = UI.Icon("", 64, Look.Muted);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.Opacity = 0.5;
        var previewBox = new Border { Width = 290, Height = 290, CornerRadius = new CornerRadius(14), Background = Look.Card, BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent };
        DragDrop.SetAllowDrop(previewBox, true);
        var previewGrid = new Panel();
        previewGrid.Children.Add(placeholder);
        previewGrid.Children.Add(preview);
        previewBox.Child = previewGrid;
        var status = UI.Text("", 12, FontWeight.Medium, wrap: true);
        status.TextAlignment = TextAlignment.Center;
        status.Width = 280;
        var modules = UI.Text("", 11, color: Look.Muted);
        modules.HorizontalAlignment = HorizontalAlignment.Center;
        var save = UI.Button("Save QR Code", () => { }, primary: true);
        var copy = UI.Button("Copy Image", () => { });
        var generation = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };

        void Refresh()
        {
            generation++;
            var current = generation;
            var problem = model.Problem;
            save.IsEnabled = copy.IsEnabled = problem == null;
            previewBox.Background = model.Transparent ? Brushes.White : Look.Card;
            if (problem != null || model.Matrix is not { } matrix)
            {
                preview.Source = null;
                placeholder.IsVisible = true;
                status.Text = L10n.T(problem ?? "");
                status.Foreground = Look.Muted;
                modules.Text = "";
                return;
            }
            var png = model.Design.PngBytes(matrix, 560);
            preview.Source = Pictures.FromBytes(png);
            placeholder.IsVisible = false;
            modules.Text = L10n.T($"{matrix.Size} × {matrix.Size} modules");
            status.Text = L10n.T("Checking…");
            status.Foreground = Look.Muted;
            var payload = model.Payload;
            _ = Task.Run(() =>
            {
                // Scanned over white, as a transparent code would be seen on a light page.
                using var picture = new ImageMagick.MagickImage(png);
                picture.BackgroundColor = ImageMagick.MagickColors.White;
                picture.Alpha(ImageMagick.AlphaAction.Remove);
                var found = Recognition.Codes(picture);
                Dispatcher.UIThread.Post(() =>
                {
                    if (current != generation) return;
                    var readable = found.Contains(payload);
                    status.Text = L10n.T(readable ? "Scans correctly" : "May not scan. Use a smaller logo, more contrast or higher correction.");
                    status.Foreground = readable ? new SolidColorBrush(Color.FromRgb(0x1F, 0x9D, 0x55)) : new SolidColorBrush(Color.FromRgb(0xC2, 0x6A, 0x00));
                });
            });
        }
        timer.Tick += (_, _) => { timer.Stop(); Refresh(); };
        void Changed() { timer.Stop(); timer.Start(); }

        // Content
        var textBox = new TextBox { Text = model.Text, Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(4) };
        textBox.TextChanged += (_, _) => { model.Text = textBox.Text ?? ""; Changed(); };
        var wifiPanel = new StackPanel { IsVisible = false };
        var network = new TextBox { MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, Width = 170 };
        var password = new TextBox { MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, Width = 170, Margin = new Thickness(8, 0, 0, 0) };
        network.TextChanged += (_, _) => { model.Network = network.Text ?? ""; Changed(); };
        password.TextChanged += (_, _) => { model.Password = password.Text ?? ""; Changed(); };
        static StackPanel Labeled(string title, Control box)
        {
            var panel = new StackPanel();
            panel.Children.Add(UI.Text(title, 11, color: Look.Muted));
            panel.Children.Add(box);
            return panel;
        }
        var fields = new StackPanel { Orientation = Orientation.Horizontal };
        fields.Children.Add(Labeled("Network name", network));
        var passwordField = Labeled("Password", password);
        fields.Children.Add(passwordField);
        wifiPanel.Children.Add(fields);
        var wifiOptions = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
        var security = UI.Picker(Enum.GetValues<QrContent.Security>().Select(s => (s, QrContent.Title(s))).ToList(), model.Security, s =>
        {
            model.Security = s;
            passwordField.IsVisible = s != QrContent.Security.None;
            Changed();
        });
        DockPanel.SetDock(security, Dock.Left);
        wifiOptions.Children.Add(security);
        var hidden = UI.Check("Hidden network", false, v => { model.Hidden = v; Changed(); });
        DockPanel.SetDock(hidden, Dock.Right);
        wifiOptions.Children.Add(hidden);
        wifiPanel.Children.Add(wifiOptions);
        var kind = UI.Segmented([(false, "Text or Link"), (true, "Wi-Fi")], false, wifi =>
        {
            model.Wifi = wifi;
            textBox.IsVisible = !wifi;
            wifiPanel.IsVisible = wifi;
            Changed();
        });
        kind.Margin = new Thickness(0, 0, 0, 8);
        left.Children.Add(kind);
        left.Children.Add(textBox);
        left.Children.Add(wifiPanel);
        left.Children.Add(UI.Divider());

        // Shapes, drawn with the same paths as the real code
        Border Swatch(Geometry geometry, double units, string title, bool selected, Action pick, List<Border> group)
        {
            var canvas = new Canvas { Width = units, Height = units };
            canvas.Children.Add(new Path { Data = geometry, Fill = Look.Ink });
            var picture = new Viewbox { Child = canvas, Width = 22, Height = 22 };
            var border = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 4, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = picture, BorderThickness = new Thickness(2) };
            ToolTip.SetTip(border, L10n.T(title));
            void Paint(Border b, bool on) { b.Background = new SolidColorBrush(Color.FromArgb((byte)(on ? 0x1A : 0x0A), 0, 0, 0)); b.BorderBrush = on ? Look.Accent : Brushes.Transparent; }
            Paint(border, selected);
            border.PointerReleased += (_, _) => { foreach (var b in group) Paint(b, b == border); pick(); Changed(); };
            group.Add(border);
            return border;
        }
        var sample = new QrMatrix(5, "1101110110011011101010111".Select(c => c == '1').ToArray());
        var moduleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var moduleGroup = new List<Border>();
        foreach (var shape in Enum.GetValues<QrDesign.ModuleShape>())
        {
            var path = new QrDesign { Module = shape, Margin = 0, Background = null }.ModulePath(sample);
            moduleRow.Children.Add(Swatch(Geometry.Parse("F1 " + path), 5, QrDesign.Title(shape), shape == model.Module, () => model.Module = shape, moduleGroup));
        }
        left.Children.Add(UI.Row("Shape", moduleRow, 40));
        var eyeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var eyeGroup = new List<Border>();
        foreach (var shape in Enum.GetValues<QrDesign.EyeShape>())
        {
            var builder = new PathBuilder();
            QrDesign.AddEye(shape, new Core.Graphics.Rect(0, 0, 7, 7), builder);
            eyeRow.Children.Add(Swatch(Geometry.Parse("F0 " + builder), 7, QrDesign.Title(shape), shape == model.Eye, () => model.Eye = shape, eyeGroup));
        }
        left.Children.Add(UI.Row("Corners", eyeRow, 40));
        left.Children.Add(UI.Divider());

        // Colours
        var codeColors = new StackPanel { Orientation = Orientation.Horizontal };
        var gradientWell = ColorWell.Make(model.GradientEnd, c => { model.GradientEnd = c; Changed(); });
        gradientWell.IsVisible = false;
        var arrow = UI.Icon(Look.Direction == FlowDirection.RightToLeft ? "" : "", 9, Look.Muted).Pad(6, 0);
        arrow.IsVisible = false;
        codeColors.Children.Add(ColorWell.Make(model.Foreground, c => { model.Foreground = c; Changed(); }));
        codeColors.Children.Add(arrow);
        codeColors.Children.Add(gradientWell);
        left.Children.Add(UI.Row("Code color", codeColors, 34));
        var colorOptions = new DockPanel { LastChildFill = false };
        var eyeWell = ColorWell.Make(model.EyeColor, c => { model.EyeColor = c; Changed(); });
        eyeWell.IsVisible = false;
        var gradient = UI.Check("Gradient", false, v => { model.Gradient = v; gradientWell.IsVisible = arrow.IsVisible = v; Changed(); });
        var ownEye = UI.Check("Own corner color", false, v => { model.SeparateEye = v; eyeWell.IsVisible = v; Changed(); });
        ownEye.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(gradient, Dock.Left);
        DockPanel.SetDock(ownEye, Dock.Left);
        DockPanel.SetDock(eyeWell, Dock.Right);
        colorOptions.Children.Add(gradient);
        colorOptions.Children.Add(ownEye);
        colorOptions.Children.Add(eyeWell);
        left.Children.Add(colorOptions);
        var backgroundRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var backgroundWell = ColorWell.Make(model.Background, c => { model.Background = c; Changed(); });
        backgroundRow.Children.Add(UI.Check("Transparent", false, v => { model.Transparent = v; backgroundWell.IsEnabled = !v; backgroundWell.Opacity = v ? 0.4 : 1; Changed(); }));
        backgroundRow.Children.Add(backgroundWell);
        left.Children.Add(UI.Row("Background", backgroundRow, 34));
        left.Children.Add(UI.Divider());

        // Logo
        ComboBox? correctionPicker = null;
        var logoRow = new StackPanel { Orientation = Orientation.Horizontal };
        var logoThumb = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), IsVisible = false };
        var logoDetails = new StackPanel { IsVisible = false };
        var logoHint = UI.Caption("Or drop an image on the preview.");
        var removeLogo = UI.Button("Remove", () => { });
        removeLogo.IsVisible = false;
        var chooseLogo = UI.Button("Choose Logo…", () => { });
        void SetLogo(SKBitmap? logo, Bitmap? thumb)
        {
            // A logo hides part of the code; the highest error correction lets phones read around it.
            if (logo != null && model.Logo == null) { model.Correction = QrMatrix.Correction.High; correctionPicker!.SelectedIndex = 3; }
            model.Logo = logo;
            logoThumb.Source = thumb;
            var has = logo != null;
            logoThumb.IsVisible = removeLogo.IsVisible = logoDetails.IsVisible = has;
            logoHint.IsVisible = !has;
            chooseLogo.Title = has ? "Change…" : "Choose Logo…";
            Changed();
        }
        void LoadLogo(string path)
        {
            try
            {
                using var image = ImageEngine.Load(path);
                if (Math.Max(image.Width, image.Height) > 1024) image.Resize(new ImageMagick.MagickGeometry(1024, 1024));
                var png = image.ToByteArray(ImageMagick.MagickFormat.Png);
                SetLogo(SKBitmap.Decode(png), Pictures.FromBytes(png));
            }
            catch (Exception) { Hud.Shared.ShowMessage(L10n.T($"Could not read {System.IO.Path.GetFileName(path)}"), Hud.Style.Failure); }
        }
        chooseLogo.Click += async () =>
        {
            if (await Dialogs.OpenImage(window, "Choose a logo for the middle of the code") is { } picked) LoadLogo(picked);
        };
        removeLogo.Click += () => SetLogo(null, null);
        removeLogo.Margin = new Thickness(0, 0, 6, 0);
        logoRow.Children.Add(logoThumb);
        logoRow.Children.Add(removeLogo);
        logoRow.Children.Add(chooseLogo);
        left.Children.Add(UI.Row("Logo", logoRow, 34));
        logoDetails.Children.Add(UI.Row("Logo size", UI.Slider(0.12, 0.32, model.LogoScale, v => { model.LogoScale = v; Changed(); }, v => L10n.T($"{Math.Round(v * 100)}%"), width: 140)));
        var plate = UI.Segmented(Enum.GetValues<QrDesign.Plate>().Select(p => (p, QrDesign.Title(p))).ToList(), model.Plate, p => { model.Plate = p; Changed(); });
        plate.Width = 230;
        logoDetails.Children.Add(UI.Row("Behind the logo", plate, 34));
        left.Children.Add(logoDetails);
        left.Children.Add(logoHint);
        left.Children.Add(UI.Divider());

        // Output
        left.Children.Add(UI.Row("Margin", UI.Slider(0, 8, model.Margin, v => { model.Margin = (int)Math.Round(v); Changed(); }, v => L10n.T($"{Math.Round(v)}"), width: 140, step: 1)));
        correctionPicker = UI.Picker(Enum.GetValues<QrMatrix.Correction>().Select(c => (c, QrMatrix.Title(c))).ToList(), model.Correction, c => { model.Correction = c; Changed(); });
        left.Children.Add(UI.Row("Error correction", correctionPicker));
        var formatRow = new StackPanel { Orientation = Orientation.Horizontal };
        var pixels = UI.Picker([(512, "512 px"), (1024, "1024 px"), (2048, "2048 px"), (4096, "4096 px")], model.Pixels, p => model.Pixels = p);
        pixels.MinWidth = 90;
        pixels.Margin = new Thickness(0, 0, 8, 0);
        formatRow.Children.Add(pixels);
        var format = UI.Segmented([(false, "PNG"), (true, "SVG")], false, svg => { model.Svg = svg; pixels.IsVisible = !svg; });
        format.Width = 110;
        formatRow.Children.Add(format);
        left.Children.Add(UI.Row("Format", formatRow, 34));

        // Preview column, which also takes a dropped logo
        previewBox.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        previewBox.AddHandler(DragDrop.DragEnterEvent, (_, _) => previewBox.BorderBrush = Look.Accent);
        previewBox.AddHandler(DragDrop.DragLeaveEvent, (_, _) => previewBox.BorderBrush = Brushes.Transparent);
        previewBox.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            previewBox.BorderBrush = Brushes.Transparent;
            if (Wheel.WheelWindow.Files(e) is { Count: > 0 } dropped) LoadLogo(dropped[0]);
        });
        var right = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        right.Children.Add(previewBox);
        right.Children.Add(status.Pad(0, 10));
        right.Children.Add(modules);

        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(left);
        columns.Children.Add(right);
        window.Body = columns;

        copy.Click += () =>
        {
            if (model.Matrix is not { } matrix) return;
            Core.Clipboard.CopyPng(model.Design.PngBytes(matrix, model.Pixels));
            Hud.Shared.ShowMessage(L10n.T("QR code copied"), Hud.Style.Success);
            Sounds.Done();
        };
        save.Click += () =>
        {
            if (model.Matrix is not { } matrix) return;
            var design = model.Design;
            var ext = model.Svg ? "svg" : "png";
            var output = source != null ? Output.PathFor(source, ext, "QR")
                : Output.Unique(System.IO.Path.Combine(Settings.Current.OutputLocation == OutputLocation.Desktop ? Core.Desktop.UserFolder("DESKTOP", "Desktop") : Output.Downloads, $"QR Code.{ext}"));
            byte[] bytes = model.Svg ? System.Text.Encoding.UTF8.GetBytes(design.Svg(matrix)) : design.PngBytes(matrix, model.Pixels);
            window.Close();
            Runner.Run("Create QR Code", source != null ? [source] : [], async (_, _) => { await File.WriteAllBytesAsync(output, bytes); return [output]; });
        };
        window.Leading(copy);
        window.Trailing(save);
        // The clipboard is read once the window exists; short text there is likely what to encode.
        window.Opened += async (_, _) =>
        {
            if (source != null || model.Text.Length > 0 || window.Clipboard is not { } board) return;
            if (await board.TryGetTextAsync() is { } clip && clip.Trim() is { Length: > 0 and <= 800 } text) textBox.Text = text;
        };
        Refresh();
        return window;
    }
}
