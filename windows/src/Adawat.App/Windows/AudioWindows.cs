using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Adawat.Core;
using Adawat.Core.Images;
using Adawat.Core.Media;

namespace Adawat.App.Windows;

/// <summary>Normalize Volume: the file's loudness now, and targets for loudness, range and true peak.</summary>
public static class NormalizeWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private static ToolWindow Build(MediaSession media)
    {
        double target = -16, range = 11, peak = -1.5;
        var window = new ToolWindow("Normalize Volume", 480);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.AudioHost(media));
        var header = new DockPanel { LastChildFill = false };
        var name = UI.Text("", 11, color: Theme.Muted);
        name.Text = Path.GetFileName(media.File);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 340;
        var length = UI.Text(Format.Time(media.Duration), 11, color: Theme.Muted);
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(length, Dock.Right);
        header.Children.Add(name);
        header.Children.Add(length);
        stack.Children.Add(header);
        var timeline = new Timeline(media, 150) { Margin = new Thickness(0, 8, 0, 4) };
        if (media.Info.HasVideo) timeline.Levels = [];
        stack.Children.Add(timeline);
        stack.Children.Add(MediaUI.Transport(media, frames: false));
        var measured = UI.Text("", 11, color: Theme.Muted);
        measured.Visibility = Visibility.Collapsed;
        stack.Children.Add(measured.Pad(0, 6));
        var sliders = new List<System.Windows.Controls.Slider>();
        StackPanel Setting(string title, double value, double min, double max, double step, string unit, Action<double> set)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var row = new DockPanel { LastChildFill = false };
            var label = UI.Text(title, 12);
            string Show(double v) => $"{v.ToString(step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture)} {unit}";
            var shown = UI.Text(Show(value), 12);
            DockPanel.SetDock(label, Dock.Left);
            DockPanel.SetDock(shown, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(shown);
            var slider = new System.Windows.Controls.Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
            slider.ValueChanged += (_, e) => { set(e.NewValue); shown.Text = Show(e.NewValue); };
            sliders.Add(slider);
            panel.Children.Add(row);
            panel.Children.Add(slider);
            return panel;
        }
        stack.Children.Add(Setting("Target loudness", target, -24, -5, 1, "LUFS", v => target = v));
        stack.Children.Add(Setting("Loudness range", range, 1, 20, 1, "LU", v => range = v));
        stack.Children.Add(Setting("True peak limit", peak, -9, 0, 0.5, "dBTP", v => peak = v));
        stack.Children.Add(UI.Caption("−16 LUFS is a balanced default for spoken audio and music. The true peak limit helps prevent clipping after encoding.").Pad(0, 10));
        window.Body = stack;
        window.Leading(UI.Button("Restore Defaults", () => { sliders[0].Value = -16; sliders[1].Value = 11; sliders[2].Value = -1.5; }));
        var apply = UI.Button("Normalize Audio", () =>
        {
            var (t, r, p) = (target, range, peak);
            window.Close();
            Runner.Run("Normalize Volume", [media.File], (f, c) => MediaOps.Normalize(f, t, r, p, c));
        }, primary: true);
        apply.IsDefault = true;
        window.Trailing(apply);
        window.Loaded += async (_, _) =>
        {
            if (await Task.Run(() => MediaOps.MeasureLoudness(media.File)) is not { } loudness) return;
            measured.Text = L10n.T($"Now: {loudness.I} LUFS · peak {loudness.Tp} dBTP · range {loudness.Lra} LU");
            measured.Visibility = Visibility.Visible;
        };
        return window;
    }
}

/// <summary>Audio Visualizer: a video of the sound as a moving waveform, or over a still picture, in a chosen shape.</summary>
public static class VisualizerWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build, bars: 90);

    private static ToolWindow Build(MediaSession media)
    {
        var still = false;
        string? image = null;
        BitmapSource? picture = null;
        var shape = VisualizerShape.Landscape;
        var color = Theme.AccentColor;
        var window = new ToolWindow("Audio Visualizer", 480);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.AudioHost(media));
        var preview = new PreviewFrame(media) { Height = 300 };
        stack.Children.Add(preview);
        var create = UI.Button("Create Visualization", () => { }, primary: true);
        void Update()
        {
            preview.Shape = shape;
            preview.Color = color;
            preview.Picture = still ? picture : null;
            preview.Still = still;
            preview.InvalidateVisual();
            create.IsEnabled = !still || image != null;
        }
        var imageButton = UI.Button("Choose Image…", () => { });
        var imageRow = UI.Row("Image", imageButton, 34);
        imageRow.Visibility = Visibility.Collapsed;
        var colorRow = UI.Row("Color", ColorWell.Make(color, c => { color = c; Update(); }), 34);
        stack.Children.Add(UI.Row("Style", UI.Picker([(false, "Waveform"), (true, "Still image")], false, v =>
        {
            still = v;
            imageRow.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            colorRow.Visibility = v ? Visibility.Collapsed : Visibility.Visible;
            Update();
        }), 36).Pad(0, 10));
        stack.Children.Add(imageRow);
        stack.Children.Add(colorRow);
        stack.Children.Add(UI.Row("Shape", UI.Picker(Enum.GetValues<VisualizerShape>().Select(s => (s, s.Title())).ToList(), shape, s => { shape = s; Update(); }), 36));
        imageButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = L10n.T("Choose an image for the visualization."), Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.heic;*.bmp;*.gif;*.tif;*.tiff" };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                var (loaded, _, _) = Pictures.LoadPreview(dialog.FileName, 800);
                using (loaded) picture = Pictures.ToBitmap(loaded);
                image = dialog.FileName;
                ((TextBlock)((StackPanel)imageButton.Content).Children[^1]).Text = Path.GetFileName(image);
            }
            catch (Exception) { Hud.Shared.ShowMessage(L10n.T($"Could not read {Path.GetFileName(dialog.FileName)}"), Hud.Style.Failure); }
            Update();
        };
        media.StripLoaded += preview.InvalidateVisual;
        window.Body = stack;
        create.Click += (_, _) =>
        {
            var (file, s, rgb) = (still ? image : null, shape, MediaUI.Rgb(color));
            window.Close();
            Runner.Run("Create Visualization", [media.File], async (f, c) => [await MediaTools.Visualize(f[0], file, s, rgb, c)]);
        };
        create.IsDefault = true;
        window.Trailing(create);
        Update();
        return window;
    }

    /// <summary>The result's frame: black in the chosen shape, with the waveform or the picture.</summary>
    private sealed class PreviewFrame(MediaSession media) : FrameworkElement
    {
        public VisualizerShape Shape { get; set; }
        public Color Color { get; set; }
        public BitmapSource? Picture { get; set; }
        public bool Still { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            var (pw, ph) = Shape.Size();
            var scale = Math.Min(ActualWidth / pw, ActualHeight / ph);
            var size = new Size(pw * scale, ph * scale);
            var frame = new Rect((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2, size.Width, size.Height);
            dc.PushClip(new RectangleGeometry(frame, 12, 12));
            dc.DrawRectangle(Brushes.Black, null, frame);
            if (Still && Picture != null)
            {
                var fit = Math.Min(frame.Width / Picture.PixelWidth, frame.Height / Picture.PixelHeight);
                var shown = new Size(Picture.PixelWidth * fit, Picture.PixelHeight * fit);
                dc.DrawImage(Picture, new Rect(frame.X + (frame.Width - shown.Width) / 2, frame.Y + (frame.Height - shown.Height) / 2, shown.Width, shown.Height));
            }
            else if (!Still && media.Waveform.Length > 0)
            {
                var band = Math.Min(120, frame.Height * 0.6);
                Timeline.DrawWave(dc, media.Waveform, new Rect(frame.X + 18, frame.Y + (frame.Height - band) / 2, frame.Width - 36, band), new SolidColorBrush(Color));
            }
            dc.Pop();
        }
    }
}

/// <summary>Convert Audio Channels: each channel's waveform with a solo preview, then mono or stereo with per-side volume.</summary>
public static class ChannelsWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build, bars: 160);

    private static ToolWindow Build(MediaSession media)
    {
        var mono = false;
        double left = 1, right = 1;
        var stereo = media.Info.AudioChannels >= 2;
        var preview = new PreviewPlayer();
        var window = new ToolWindow("Convert Audio Channels", 480);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.AudioHost(media));
        stack.Children.Add(UI.Text("Output channel preview", 11, color: Theme.Muted).Pad(0, 0));
        var waves = new List<Timeline>();
        var soloButtons = new List<Button>();
        for (var channel = 0; channel < (stereo ? 2 : 1); channel++)
        {
            var index = channel;
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            var badge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Theme.AccentSoft, HorizontalAlignment = HorizontalAlignment.Left,
                                     Child = new TextBlock { Text = channel == 0 ? (stereo ? "L" : "M") : "R", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            row.Children.Add(badge);
            var wave = new Timeline(media, 44) { Levels = [], WaveBrush = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), ShowPlayhead = false };
            wave.Pressed = (_, _) => true;
            wave.Dragged = _ => { };
            Grid.SetColumn(wave, 1);
            row.Children.Add(wave);
            waves.Add(wave);
            var solo = UI.IconButton("", () => preview.Toggle($"solo{index}", () =>
                MediaTools.PreviewAudio(media.File, MediaTools.ChannelFilter(media.Info, true, 1, 1, index), false)), "Play this channel on its own", 28);
            solo.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(solo, 2);
            row.Children.Add(solo);
            soloButtons.Add(solo);
            stack.Children.Add(row);
        }
        stack.Children.Add(UI.Divider());
        stack.Children.Add(UI.Row("Output", UI.Segmented([(true, "Mono"), (false, "Stereo")], false, v => mono = v), 36));
        string Percent(double v) => L10n.T($"{Math.Round(v * 100)}%");
        stack.Children.Add(UI.Row("Left volume", UI.Slider(0, 1.5, 1, v => left = v, Percent, width: 200)));
        stack.Children.Add(UI.Row("Right volume", UI.Slider(0, 1.5, 1, v => right = v, Percent, width: 200)));
        var playOutput = UI.Button("Play Output", () => preview.Toggle("output", () =>
            MediaTools.PreviewAudio(media.File, MediaTools.ChannelFilter(media.Info, mono, left, right), false)), icon: "");
        stack.Children.Add(UI.Row("Preview", playOutput, 36));
        preview.Changed += () =>
        {
            for (var i = 0; i < soloButtons.Count; i++) ((TextBlock)soloButtons[i].Content).Text = preview.Playing == $"solo{i}" ? "" : "";
            var content = (StackPanel)playOutput.Content;
            ((TextBlock)content.Children[0]).Text = preview.Playing == "output" ? "" : "";
            ((TextBlock)content.Children[^1]).Text = L10n.T(preview.Playing == "output" ? "Stop" : "Play Output");
        };
        window.Closed += (_, _) => preview.Stop();
        window.Loaded += async (_, _) =>
        {
            waves[0].Levels = await Task.Run(() => MediaOps.Waveform(media.File, 160, stereo ? 0 : null));
            waves[0].InvalidateVisual();
            if (!stereo) return;
            waves[1].Levels = await Task.Run(() => MediaOps.Waveform(media.File, 160, 1));
            waves[1].InvalidateVisual();
        };
        window.Body = stack;
        var apply = UI.Button("Apply", () =>
        {
            var (m, l, r) = (mono, left, right);
            window.Close();
            Runner.Run("Convert Audio Channels", [media.File], async (f, c) => [await MediaTools.ConvertChannels(f[0], m, l, r, c)]);
        }, primary: true);
        apply.IsDefault = true;
        window.Trailing(apply);
        return window;
    }
}

/// <summary>Bleep Audio: sections drawn on a layer over the waveform, previewed with the bleeps, then saved.</summary>
public static class BleepWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private sealed class Section { public double Start, End; }

    private static ToolWindow Build(MediaSession media)
    {
        var sections = new List<Section>();
        Section? selected = null;
        var preview = new PreviewPlayer();
        var window = new ToolWindow("Bleep Audio", 540);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.AudioHost(media));
        var header = new DockPanel { LastChildFill = false };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(UI.Text("Bleeps", 12, FontWeights.SemiBold));
        var count = UI.Text("0", 12, color: Theme.Muted).Pad(6, 0);
        title.Children.Add(count);
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        var entire = UI.Button("Bleep Entire Audio", () => { });
        var addHere = UI.Button("Add bleep at playhead", () => { });
        addHere.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(entire, Dock.Right);
        DockPanel.SetDock(addHere, Dock.Right);
        header.Children.Add(entire);
        header.Children.Add(addHere);
        stack.Children.Add(header);

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labels = new StackPanel();
        labels.Children.Add(new Border { Height = 36, Child = UI.Text("Bleep", 11, color: Theme.Muted) });
        labels.Children.Add(new Border { Height = 76, Child = UI.Text("Audio", 11, color: Theme.Muted) });
        grid.Children.Add(labels);
        var lanes = new StackPanel();
        Grid.SetColumn(lanes, 1);
        grid.Children.Add(lanes);
        var layer = new Timeline(media, 30) { Levels = [], ShowPlayhead = true, Margin = new Thickness(0, 3, 0, 3) };
        lanes.Children.Add(layer);
        var wave = new Timeline(media, 70);
        lanes.Children.Add(wave.Pad(0, 3));
        var ends = new DockPanel { LastChildFill = false, FlowDirection = FlowDirection.LeftToRight };
        var zero = UI.Text("0:00.00", 10, color: Theme.Muted);
        var total = UI.Text(Format.Time(media.Duration), 10, color: Theme.Muted);
        DockPanel.SetDock(zero, Dock.Left);
        DockPanel.SetDock(total, Dock.Right);
        ends.Children.Add(zero);
        ends.Children.Add(total);
        lanes.Children.Add(ends);
        stack.Children.Add(grid);
        stack.Children.Add(UI.Caption("Drag across the Bleep layer to add a section, then drag a section or either edge to adjust it. Delete removes the selected one.").Pad(0, 6));
        stack.Children.Add(UI.Divider());
        var original = UI.Button("Original", () => preview.Toggle("original", () => Task.FromResult(media.File)));
        var bleeped = UI.Button("With Bleeps", () =>
        {
            var ranges = sections.Select(s => (s.Start, s.End)).ToList();
            preview.Toggle("bleeped", () => MediaTools.PreviewAudio(media.File, MediaTools.BleepFilter(ranges, media.Duration), true));
        }, icon: "");
        var previews = new StackPanel { Orientation = Orientation.Horizontal };
        previews.Children.Add(original);
        previews.Children.Add(new Border { Width = 6 });
        previews.Children.Add(bleeped);
        stack.Children.Add(UI.Row("Preview", previews, 36));
        var reset = UI.Button("Reset", () => { });
        var save = UI.Button("Save Bleeped Copy", () => { }, primary: true);

        void Refresh()
        {
            count.Text = L10n.T($"{sections.Count}");
            reset.IsEnabled = save.IsEnabled = bleeped.IsEnabled = sections.Count > 0;
            layer.InvalidateVisual();
        }
        var trackBrush = new SolidColorBrush(Color.FromArgb(0x0D, 0, 0, 0));
        layer.Overlay = (dc, w, h) =>
        {
            dc.DrawRoundedRectangle(trackBrush, null, new Rect(0, 0, w, h), 7, 7);
            foreach (var s in sections)
            {
                var x1 = layer.XAt(s.Start);
                var x2 = layer.XAt(s.End);
                var rect = new Rect(x1, 0, Math.Max(8, x2 - x1), h);
                var fill = Color.FromArgb((byte)(s == selected ? 0xD9 : 0x99), Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B);
                dc.DrawRoundedRectangle(new SolidColorBrush(fill), null, rect, 6, 6);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(rect.X + 4, h / 2 - 7, 2, 14), 1, 1);
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(rect.Right - 6, h / 2 - 7, 2, 14), 1, 1);
                var text = new FormattedText(L10n.T("Bleep"), CultureInfo.CurrentUICulture, Theme.Direction, new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 10, Brushes.White, 1.0);
                if (text.Width < rect.Width - 16) dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2 + (Theme.Direction == FlowDirection.RightToLeft ? text.Width : 0), (h - text.Height) / 2));
            }
        };
        // Dragging empty space adds a section; dragging a section moves it; dragging near an edge resizes it.
        var mode = 0;
        double startTime = 0;
        (double Start, double End) origin = default;
        layer.Pressed = (t, p) =>
        {
            startTime = t;
            var edge = 6 * media.Duration / Math.Max(1, layer.ActualWidth);
            selected = sections.FirstOrDefault(s => Math.Abs(s.Start - t) < edge);
            mode = 1;
            if (selected == null) { selected = sections.FirstOrDefault(s => Math.Abs(s.End - t) < edge); mode = 2; }
            if (selected == null) { selected = sections.FirstOrDefault(s => s.Start <= t && t <= s.End); mode = 0; }
            if (selected == null) { selected = new Section { Start = t, End = t }; sections.Add(selected); mode = 3; }
            origin = (selected.Start, selected.End);
            Refresh();
            return true;
        };
        layer.Dragged = t =>
        {
            if (selected == null) return;
            var delta = t - startTime;
            switch (mode)
            {
                case 0:
                    var length = origin.End - origin.Start;
                    selected.Start = Math.Clamp(origin.Start + delta, 0, media.Duration - length);
                    selected.End = selected.Start + length;
                    break;
                case 1: selected.Start = Math.Min(t, origin.End - 0.05); break;
                case 2: selected.End = Math.Max(t, origin.Start + 0.05); break;
                default: selected.Start = Math.Min(startTime, t); selected.End = Math.Max(startTime, t); break;
            }
            Refresh();
        };
        layer.Released = () =>
        {
            if (selected != null && selected.End - selected.Start < 0.05) { sections.Remove(selected); selected = null; }
            Refresh();
        };
        addHere.Click += (_, _) =>
        {
            selected = new Section { Start = Math.Max(0, media.Current - 0.25), End = Math.Min(media.Duration, media.Current + 0.25) };
            sections.Add(selected);
            Refresh();
        };
        entire.Click += (_, _) =>
        {
            sections.Clear();
            selected = new Section { Start = 0, End = media.Duration };
            sections.Add(selected);
            Refresh();
        };
        reset.Click += (_, _) => { sections.Clear(); selected = null; Refresh(); };
        window.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Delete or Key.Back) || selected == null) return;
            sections.Remove(selected);
            selected = null;
            Refresh();
            e.Handled = true;
        };
        preview.Changed += () =>
        {
            ((TextBlock)((StackPanel)original.Content).Children[^1]).Text = L10n.T(preview.Playing == "original" ? "Stop" : "Original");
            var content = (StackPanel)bleeped.Content;
            ((TextBlock)content.Children[0]).Text = preview.Playing == "bleeped" ? "" : "";
            ((TextBlock)content.Children[^1]).Text = L10n.T(preview.Playing == "bleeped" ? "Stop" : "With Bleeps");
        };
        window.Closed += (_, _) => preview.Stop();
        save.Click += (_, _) =>
        {
            var ranges = sections.Select(s => (s.Start, s.End)).OrderBy(r => r.Start).ToList();
            window.Close();
            Runner.Run("Bleep Audio", [media.File], async (f, c) => [await MediaTools.Bleep(f[0], ranges, c)]);
        };
        window.Body = stack;
        window.Leading(reset);
        save.IsDefault = true;
        window.Trailing(save);
        Refresh();
        return window;
    }
}
