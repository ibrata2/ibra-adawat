using System.Globalization;
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

namespace Adawat.Linux.Windows;

/// <summary>Trim: drag the two ends of the timeline, or type the times; audio can also lose its silent ends.</summary>
public static class TrimWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build, bars: 220);

    private static ToolWindow Build(MediaSession media)
    {
        var video = media.Info.HasVideo;
        double start = 0, end = media.Duration;
        var silence = false;
        var window = new ToolWindow(video ? "Trim Video" : "Trim Audio", video ? 580 : 500);
        var stack = new StackPanel();
        stack.Children.Add(video ? MediaUI.Stage(media, 300) : MediaUI.AudioHost(media));
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 6) };
        var hint = UI.Text("Drag the ends to trim", 11, color: Look.Muted);
        var length = UI.Text("", 11, color: Look.Muted);
        DockPanel.SetDock(hint, Dock.Left);
        DockPanel.SetDock(length, Dock.Right);
        header.Children.Add(hint);
        header.Children.Add(length);
        stack.Children.Add(header);

        var timeline = new Timeline(media, video ? 64 : 96);
        TextBox? startField = null, endField = null;
        void Update()
        {
            length.Text = L10n.T($"{Format.Time(end - start)} selected");
            if (startField != null && !startField.IsFocused) startField.Text = Format.Time(start);
            if (endField != null && !endField.IsFocused) endField.Text = Format.Time(end);
            timeline.InvalidateVisual();
        }
        void SetStart(double value) { start = Math.Clamp(value, 0, end - 0.1); media.Seek(start); Update(); }
        void SetEnd(double value) { end = Math.Clamp(value, start + 0.1, media.Duration); media.Seek(end); Update(); }
        var dim = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));
        timeline.Overlay = (dc, w, h) =>
        {
            var x1 = timeline.XAt(start);
            var x2 = timeline.XAt(end);
            // Dim what will be cut.
            dc.FillRectangle(dim, new Rect(0, 0, Math.Max(0, x1), h));
            dc.FillRectangle(dim, new Rect(x2, 0, Math.Max(0, w - x2), h));
            dc.DrawRectangle(null, new Pen(Look.Accent, 3), new Rect(x1 + 1.5, 1.5, Math.Max(20, x2 - x1) - 3, h - 3), 7, 7);
            foreach (var x in new[] { x1, x2 - 12 })
            {
                dc.DrawRectangle(Look.Accent, null, new Rect(x, 0, 12, h), 4, 4);
                dc.DrawRectangle(Brushes.White, null, new Rect(x + 4.75, h / 2 - 9, 2.5, 18), 1.25, 1.25);
            }
        };
        var mode = 0;
        timeline.Pressed = (t, p) =>
        {
            var x1 = timeline.XAt(start);
            var x2 = timeline.XAt(end);
            mode = Math.Abs(p.X - x1 - 6) < 12 ? 1 : Math.Abs(p.X - x2 + 6) < 12 ? 2 : 0;
            return mode != 0;
        };
        timeline.Dragged = t => { if (mode == 1) SetStart(t); else if (mode == 2) SetEnd(t); else media.Seek(t); };
        timeline.Released = () => mode = 0;
        stack.Children.Add(timeline);

        var fields = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 4) };
        startField = MediaUI.TimeField(start, SetStart);
        endField = MediaUI.TimeField(end, SetEnd);
        StackPanel Field(string title, TextBox box)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(UI.Text(title, 12, color: Look.Muted));
            row.Children.Add(box);
            return row;
        }
        var startRow = Field("Start", startField);
        var endRow = Field("End", endField);
        DockPanel.SetDock(startRow, Dock.Left);
        DockPanel.SetDock(endRow, Dock.Right);
        fields.Children.Add(startRow);
        fields.Children.Add(endRow);
        stack.Children.Add(fields);

        void PlaySelection()
        {
            if (media.IsPlaying) { media.Pause(); return; }
            media.Play(media.Current < start || media.Current >= end - 0.05 ? start : null, end);
        }
        if (video) stack.Children.Add(MediaUI.Transport(media, PlaySelection));
        else
        {
            stack.Children.Add(UI.Check("Trim silent ends automatically", false, v => silence = v).Pad(0, 4));
            var play = UI.Button("Play Selected Range", PlaySelection, icon: "");
            play.HorizontalAlignment = HorizontalAlignment.Right;
            void Paint()
            {
                play.IsEnabled = media.CanPlay;
                var content = (StackPanel)play.Child!;
                ((TextBlock)content.Children[0]).Text = Icons.Glyph(media.IsPlaying ? "" : "");
                ((TextBlock)content.Children[^1]).Text = L10n.T(media.IsPlaying ? "Stop" : "Play Selected Range");
            }
            media.Changed += Paint;
            Paint();
            stack.Children.Add(play);
        }
        window.Body = stack;
        void Apply()
        {
            var (s, e, trim) = (start, end, silence);
            window.Close();
            Runner.Run("Trim", [media.File], async (f, c) => [await MediaOps.Trim(f[0], s, e, trim, c)]);
        }
        window.Trailing(UI.Button("Apply", Apply, primary: true));
        window.KeyDown += (_, e) =>
        {
            if (e.Source is TextBox) return;
            if (e.Key == Key.Space) { PlaySelection(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Apply(); e.Handled = true; }
        };
        Update();
        return window;
    }
}

/// <summary>Change Speed: a slider over the marked speeds, previewed at that speed; audio keeps its pitch.</summary>
public static class SpeedWindow
{
    private static readonly double[] Marks = [0.25, 0.5, 1, 1.5, 2, 4];

    /// <summary>Maps a 0…5 slider position onto the speeds marked under it, evenly spaced like the labels.</summary>
    public static double SpeedAt(double position)
    {
        var index = Math.Clamp(position, 0, Marks.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = Math.Min(lower + 1, Marks.Length - 1);
        var value = Marks[lower] + (Marks[upper] - Marks[lower]) * (index - lower);
        // Snap to a mark when close, so 1× is easy to hit again.
        foreach (var mark in Marks) if (Math.Abs(mark - value) < 0.04) value = mark;
        return Math.Round(value * 100) / 100;
    }

    public static double PositionOf(double speed)
    {
        for (var i = 0; i < Marks.Length - 1; i++)
            if (speed <= Marks[i + 1]) return i + (speed - Marks[i]) / (Marks[i + 1] - Marks[i]);
        return Marks.Length - 1;
    }

    private static string X(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private static ToolWindow Build(MediaSession media)
    {
        var video = media.Info.HasVideo;
        var speed = 1.0;
        var window = new ToolWindow(video ? "Change Video Speed" : "Change Speed", 560);
        var stack = new StackPanel();
        if (video) stack.Children.Add(MediaUI.Stage(media, 300));
        else { stack.Children.Add(MediaUI.AudioHost(media)); stack.Children.Add(new Timeline(media, 90)); }
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 2) };
        var label = UI.Text("Playback speed", 12);
        var value = UI.Text(X(1), 13, FontWeight.SemiBold);
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(label);
        header.Children.Add(value);
        stack.Children.Add(header);
        var slider = new Slider { Minimum = 0, Maximum = Marks.Length - 1, Value = PositionOf(1), FlowDirection = FlowDirection.LeftToRight };
        stack.Children.Add(slider);
        // Each label sits under its point on the track.
        var marks = new Canvas { FlowDirection = FlowDirection.LeftToRight, Height = 18 };
        var markLabels = new List<TextBlock>();
        for (var i = 0; i < Marks.Length; i++)
        {
            var mark = Marks[i];
            var text = UI.Text(X(mark), 10, color: Look.Muted);
            text.Cursor = new Cursor(StandardCursorType.Hand);
            text.PointerReleased += (_, _) => slider.Value = PositionOf(mark);
            markLabels.Add(text);
            marks.Children.Add(text);
        }
        void Place()
        {
            const double thumb = 20;
            for (var i = 0; i < markLabels.Count; i++)
            {
                var center = thumb / 2 + (marks.Bounds.Width - thumb) * i / (Marks.Length - 1);
                markLabels[i].Measure(Size.Infinity);
                Canvas.SetLeft(markLabels[i], Math.Clamp(center - markLabels[i].DesiredSize.Width / 2, 0, Math.Max(0, marks.Bounds.Width - markLabels[i].DesiredSize.Width)));
            }
        }
        marks.SizeChanged += (_, _) => Place();
        stack.Children.Add(marks);
        stack.Children.Add(MediaUI.Transport(media, () => media.Play(rate: speed), frames: false).Pad(0, 6));
        var caption = UI.Caption("");
        stack.Children.Add(caption);
        var apply = UI.Button("Apply", () =>
        {
            var chosen = speed;
            window.Close();
            Runner.Run("Change Speed", [media.File], async (f, c) => [await MediaTools.ChangeSpeed(f[0], chosen, c)]);
        }, primary: true);
        void Update()
        {
            speed = SpeedAt(slider.Value);
            value.Text = X(speed);
            for (var i = 0; i < Marks.Length; i++)
            {
                var on = Math.Abs(Marks[i] - speed) < 0.001;
                markLabels[i].FontWeight = on ? FontWeight.Bold : FontWeight.Normal;
                markLabels[i].Foreground = on ? Look.Ink : Look.Muted;
            }
            caption.Text = L10n.T($"Audio keeps its pitch. The new length is {Format.Time(media.Duration / speed)}.");
            apply.IsEnabled = Math.Abs(speed - 1) > 0.001;
            if (media.IsPlaying) media.SetRate(speed);
        }
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Update(); };
        window.Body = stack;
        window.Trailing(apply);
        Update();
        return window;
    }
}

/// <summary>Crop Video: the crop frame over a still that follows the scrubber, a ratio and exact pixel sizes.</summary>
public static class CropVideoWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build, thumbnails: 0);

    private static ToolWindow Build(MediaSession media)
    {
        var first = Task.Run(() => media.Frame(0, 1080)).GetAwaiter().GetResult()
                    ?? throw new AdawatException($"Couldn't read a frame from {Path.GetFileName(media.File)}");
        // The crop is typed in the video's upright pixels; the preview frame keeps the same shape.
        var scale = (double)Math.Max(media.Info.Width, media.Info.Height) / Math.Max(first.PixelSize.Width, first.PixelSize.Height);
        if (scale <= 0) scale = 1;
        var width = (int)Math.Round(first.PixelSize.Width * scale);
        var height = (int)Math.Round(first.PixelSize.Height * scale);
        var window = new ToolWindow("Crop Video", 560);
        var editor = new CropEditor(360) { Source = first, PixelWidth = width, PixelHeight = height };
        var stack = new StackPanel();
        stack.Children.Add(editor);
        var w = new TextBox { Width = 70, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var h = new TextBox { Width = 70, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
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
            editor.SetCrop(new Area(Math.Min(editor.Crop.X, 1 - cw), Math.Min(editor.Crop.Y, 1 - ch), cw, ch));
        }
        w.LostFocus += (_, _) => Typed();
        h.LostFocus += (_, _) => Typed();
        stack.Children.Add(UI.Row("Aspect ratio", UI.Picker(CropWindow.Ratios.Select(r => (r.Value, r.Title)).ToList(), null, editor.SetRatio)).Pad(0, 10));
        var size = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Spacing = 4 };
        size.Children.Add(UI.Text("W", 12, color: Look.Muted));
        size.Children.Add(w);
        size.Children.Add(UI.Text("H", 12, color: Look.Muted).Pad(4, 0));
        size.Children.Add(h);
        size.Children.Add(UI.Text("px", 12, color: Look.Muted).Pad(2, 0));
        stack.Children.Add(UI.Row("Size", size));

        var scrub = new Slider { Minimum = 0, Maximum = Math.Max(media.Duration, 0.01), FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 10, 0, 0) };
        var time = UI.Text("", 11, color: Look.Muted);
        var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        pending.Tick += async (_, _) => { pending.Stop(); if (await media.Frame(scrub.Value, 1080) is { } frame) editor.Source = frame; };
        void Moved() { time.Text = L10n.T($"{Format.Time(scrub.Value)} / {Format.Time(media.Duration)}"); pending.Stop(); pending.Start(); }
        scrub.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) Moved(); };
        stack.Children.Add(scrub);
        var bar = new DockPanel { LastChildFill = false };
        var steps = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Spacing = 8 };
        steps.Children.Add(UI.IconButton("", () => scrub.Value = Math.Max(0, scrub.Value - media.FrameStep), "Previous frame", 28));
        steps.Children.Add(UI.IconButton("", () => scrub.Value = Math.Min(media.Duration, scrub.Value + media.FrameStep), "Next frame", 28));
        DockPanel.SetDock(steps, Dock.Left);
        DockPanel.SetDock(time, Dock.Right);
        bar.Children.Add(steps);
        bar.Children.Add(time);
        stack.Children.Add(bar);
        time.Text = L10n.T($"{Format.Time(0)} / {Format.Time(media.Duration)}");
        ShowSize();
        window.Body = stack;
        window.Leading(UI.Button("Reset", () => { editor.SetRatio(null); editor.SetCrop(new Area(0, 0, 1, 1)); }));
        window.Trailing(UI.Button("Apply", () =>
        {
            var crop = editor.Crop;
            window.Close();
            Runner.Run("Crop Video", [media.File], (f, c) => MediaTools.Crop(f, crop.X, crop.Y, crop.Width, crop.Height, c));
        }, primary: true));
        return window;
    }
}

/// <summary>Split Video: split points on the timeline, clips to drop, and the rest exported apart or joined back.</summary>
public static class SplitWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private static ToolWindow Build(MediaSession media)
    {
        var points = new List<double>();
        var removed = new HashSet<int>();
        int? selected = null;
        var equalClips = 2;
        var joinKept = true;
        List<double> Bounds() => [0, .. points.OrderBy(p => p), media.Duration];
        Action refresh = () => { };
        var window = new ToolWindow("Split Video", 580);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.Stage(media, 280));
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 6) };
        var count = UI.Text("", 11, color: Look.Muted);
        bool CanSplit(double t) => t > 0.05 && t < media.Duration - 0.05 && points.All(p => Math.Abs(p - t) >= 0.05);
        var add = UI.Button("Add Split", () => { }, icon: "");
        DockPanel.SetDock(count, Dock.Left);
        DockPanel.SetDock(add, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(add);
        stack.Children.Add(header);
        var timeline = new Timeline(media, 56);
        var fade = new SolidColorBrush(Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF));
        timeline.Overlay = (dc, w, h) =>
        {
            var bounds = Bounds();
            for (var i = 0; i < bounds.Count - 1; i++)
            {
                var x1 = timeline.XAt(bounds[i]);
                var x2 = timeline.XAt(bounds[i + 1]);
                if (removed.Contains(i)) dc.FillRectangle(fade, new Rect(x1, 0, Math.Max(2, x2 - x1), h));
                if (selected == i) dc.DrawRectangle(null, new Pen(Look.Accent, 3), new Rect(x1 + 1.5, 1.5, Math.Max(2, x2 - x1 - 3), h - 3), 6, 6);
            }
            foreach (var point in points)
            {
                var x = timeline.XAt(point);
                dc.DrawRectangle(Brushes.White, new Pen(Look.Accent, 1), new Rect(x - 1.5, 0, 3, h), 1.5, 1.5);
            }
        };
        timeline.Pressed = (t, _) =>
        {
            var bounds = Bounds();
            for (var i = 0; i < bounds.Count - 1; i++)
                if (t >= bounds[i] && t <= bounds[i + 1]) { selected = i; media.Seek(bounds[i]); break; }
            refresh();
            return true;
        };
        timeline.Dragged = _ => { };
        stack.Children.Add(timeline);
        stack.Children.Add(MediaUI.Transport(media));
        stack.Children.Add(UI.Divider());
        var equalRow = new StackPanel { Orientation = Orientation.Horizontal };
        var equalText = UI.Text("2", 13, FontWeight.SemiBold);
        equalText.MinWidth = 26;
        equalText.TextAlignment = TextAlignment.Center;
        equalRow.Children.Add(UI.IconButton("", () => { equalClips = Math.Max(2, equalClips - 1); equalText.Text = equalClips.ToString(); }, "Fewer", 24));
        equalRow.Children.Add(equalText);
        equalRow.Children.Add(UI.IconButton("", () => { equalClips = Math.Min(50, equalClips + 1); equalText.Text = equalClips.ToString(); }, "More", 24));
        equalRow.Children.Add(new Border { Width = 10 });
        equalRow.Children.Add(UI.Button("Set", () =>
        {
            points = Enumerable.Range(1, equalClips - 1).Select(i => media.Duration * i / equalClips).ToList();
            removed.Clear();
            selected = null;
            refresh();
        }));
        stack.Children.Add(UI.Row("Equal clips", equalRow, 34));
        var join = UI.Segmented([(true, "Join the rest into one video"), (false, "Export separate clips")], true, v => joinKept = v);
        join.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(join);
        var hint = UI.Caption("");
        stack.Children.Add(hint.Pad(0, 6));
        var reset = UI.Button("Reset", () => { points.Clear(); removed.Clear(); selected = null; refresh(); });
        var apply = UI.Button("Split Video", () =>
        {
            var (p, r, j) = (points.ToList(), removed.ToHashSet(), joinKept && removed.Count > 0);
            window.Close();
            Runner.Run("Split Video", [media.File], (f, c) => MediaTools.Split(f[0], p, r, j, c));
        }, primary: true);
        void Refresh()
        {
            count.Text = L10n.T(points.Count == 1 ? "1 split point" : $"{points.Count} split points");
            add.IsEnabled = CanSplit(media.Current);
            join.IsVisible = removed.Count > 0;
            hint.Text = L10n.T(selected is not { } clip ? "Click a clip in the timeline, then press Delete to remove it. Space plays the video."
                : removed.Contains(clip) ? "Press Delete again to keep this clip." : "Press Delete to remove this clip.");
            reset.IsEnabled = points.Count > 0;
            apply.IsEnabled = points.Count > 0 && removed.Count < points.Count + 1;
            timeline.InvalidateVisual();
        }
        refresh = Refresh;
        add.Click += () =>
        {
            if (!CanSplit(media.Current)) return;
            points.Add(media.Current);
            removed.Clear();
            selected = null;
            Refresh();
        };
        media.Changed += () => add.IsEnabled = CanSplit(media.Current);
        window.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Delete or Key.Back && selected is { } clip)
            {
                if (!removed.Remove(clip)) removed.Add(clip);
                Refresh();
                e.Handled = true;
            }
            else if (e.Key == Key.Space) { media.Play(); e.Handled = true; }
        };
        window.Body = stack;
        window.Leading(reset);
        window.Trailing(apply);
        Refresh();
        return window;
    }
}

/// <summary>Save Frames: pick moments while playing or stepping, then save each as a full-size picture.</summary>
public static class FramesWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private static ToolWindow Build(MediaSession media)
    {
        var frames = new List<(double Time, Bitmap? Image)>();
        var window = new ToolWindow("Video Snapshots", 580);
        var stack = new StackPanel();
        stack.Children.Add(MediaUI.Stage(media, 300));
        stack.Children.Add(new Timeline(media, 44).Pad(0, 10));
        stack.Children.Add(MediaUI.Transport(media));
        stack.Children.Add(UI.Divider());
        var header = new DockPanel { LastChildFill = false };
        var count = UI.Text("", 11, color: Look.Muted);
        var add = UI.Button("Add Frame", () => { });
        DockPanel.SetDock(count, Dock.Left);
        DockPanel.SetDock(add, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(add);
        stack.Children.Add(header);
        var strip = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        var scroller = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(scroller);
        var save = UI.Button("Save 1 Frame", () => { }, primary: true);
        bool Taken() => frames.Any(f => Math.Abs(f.Time - media.Current) < media.FrameStep / 2);
        void Build()
        {
            strip.Children.Clear();
            count.Text = L10n.T($"{frames.Count} selected");
            scroller.IsVisible = frames.Count > 0;
            for (var i = 0; i < frames.Count; i++)
            {
                var index = i;
                var frame = frames[i];
                var tile = new Panel { Width = 96, Height = 60, Margin = new Thickness(0, 0, 8, 4), Cursor = new Cursor(StandardCursorType.Hand) };
                tile.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(7), ClipToBounds = true,
                    Background = frame.Image != null ? new ImageBrush(frame.Image) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromArgb(0xB3, 0, 0, 0)),
                });
                tile.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Padding = new Thickness(5, 1), Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock { Text = Format.Time(frame.Time), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                });
                var cross = UI.Icon("", 7, Brushes.White);
                cross.HorizontalAlignment = HorizontalAlignment.Center;
                var remove = new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0)), Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = cross,
                };
                ToolTip.SetTip(remove, L10n.T("Remove selected frame"));
                remove.PointerPressed += (_, e) => e.Handled = true;
                remove.PointerReleased += (_, e) => { frames.RemoveAt(index); Build(); e.Handled = true; };
                tile.Children.Add(remove);
                tile.PointerReleased += (_, _) => media.Seek(frame.Time);
                strip.Children.Add(tile);
            }
            save.Title = frames.Count == 1 ? "Save 1 Frame" : $"Save {frames.Count} Frames";
            save.IsEnabled = frames.Count > 0;
            add.IsEnabled = !Taken();
        }
        add.Click += async () =>
        {
            if (Taken()) return;
            var time = media.Current;
            frames.Add((time, null));
            frames.Sort((a, b) => a.Time.CompareTo(b.Time));
            Build();
            var image = await media.Frame(time, 120);
            var at = frames.FindIndex(f => f.Time == time);
            if (at >= 0) { frames[at] = (time, image); Build(); }
        };
        media.Changed += () => add.IsEnabled = !Taken();
        save.Click += () =>
        {
            var times = frames.Select(f => f.Time).ToList();
            window.Close();
            Runner.Run("Save Frames", [media.File], (f, c) => MediaTools.SaveFrames(f[0], times, c));
        };
        window.Body = stack;
        window.Trailing(save);
        Build();
        return window;
    }
}

/// <summary>Redact Video: rectangles drawn on the video, each covering its own stretch of time, filled or blurred.</summary>
public static class RedactVideoWindow
{
    public static Window? Make(IReadOnlyList<string> files) => MediaUI.Open(files, Build);

    private sealed class Range { public double Start, End; }

    private static ToolWindow Build(MediaSession media)
    {
        var first = Task.Run(() => media.Frame(0, 720)).GetAwaiter().GetResult()
                    ?? throw new AdawatException($"Couldn't read a frame from {Path.GetFileName(media.File)}");
        var blur = false;
        var color = Colors.Black;
        var window = new ToolWindow("Redact Video", 580);
        var editor = new BoxEditor(290) { Source = first };
        Range RangeOf(BoxEditor.Box box) => (Range)(box.Tag ??= new Range { Start = 0, End = media.Duration });
        editor.Visible = box => { var r = RangeOf(box); return r.Start <= media.Current + 0.001 && media.Current <= r.End + 0.001; };
        // While playing, the live frames show under the boxes; when paused, a still at the playhead.
        var live = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
        editor.SetUnderlay(live, hidePicture: false);
        var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        pending.Tick += async (_, _) => { pending.Stop(); if (await media.Frame(media.Current, 720) is { } frame) editor.Source = frame; };
        Action rows_ = () => { };
        void Paint() => editor.BoxFill = blur ? new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(color);
        Paint();
        var stack = new StackPanel();
        stack.Children.Add(editor);
        var colorWell = ColorWell.Make(color, c => { color = c; Paint(); editor.Draw(); });
        var colorRow = UI.Row("Color", colorWell, 34);
        stack.Children.Add(UI.Row("Style", UI.Segmented([(false, "Solid"), (true, "Blur")], false, v =>
        {
            blur = v;
            colorRow.IsVisible = !v;
            Paint();
            editor.Draw();
        }), 38).Pad(0, 8));
        stack.Children.Add(colorRow);
        stack.Children.Add(UI.Divider());
        var header = new DockPanel { LastChildFill = false };
        var title = UI.Text("Redactions", 12, FontWeight.SemiBold);
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        var trash = UI.IconButton("", editor.DeleteSelected, "Delete Redaction", 28);
        var whole = UI.Button("Show for Entire Video", () =>
        {
            if (editor.Selected is not { } box) return;
            var r = RangeOf(box);
            r.Start = 0;
            r.End = media.Duration;
            rows_();
        });
        whole.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(trash, Dock.Right);
        DockPanel.SetDock(whole, Dock.Right);
        header.Children.Add(trash);
        header.Children.Add(whole);
        stack.Children.Add(header);
        var empty = UI.Caption("Draw rectangles on the video to cover areas. Each one gets its own time range here.");
        stack.Children.Add(empty.Pad(0, 6));
        var rows = new StackPanel { Margin = new Thickness(0, 6) };
        stack.Children.Add(rows);
        stack.Children.Add(new Timeline(media, 36));
        stack.Children.Add(MediaUI.Transport(media));
        var save = UI.Button("Save Redacted Copy", () =>
        {
            var redactions = editor.Boxes.Select(b => { var r = RangeOf(b); return new VideoRedaction(b.Rect.X, b.Rect.Y, b.Rect.Width, b.Rect.Height, r.Start, r.End); }).ToList();
            var (useBlur, rgb) = (blur, MediaUI.Rgb(color));
            window.Close();
            Runner.Run("Redact Video", [media.File], async (f, c) => [await MediaTools.Redact(f[0], redactions, useBlur, rgb, c)]);
        }, primary: true);

        void Rows()
        {
            rows.Children.Clear();
            for (var i = 0; i < editor.Boxes.Count; i++)
            {
                var box = editor.Boxes[i];
                var row = new Grid { Height = 24, Margin = new Thickness(0, 0, 0, 6), ColumnDefinitions = new ColumnDefinitions("18,*") };
                row.Children.Add(UI.Text(L10n.T($"{i + 1}"), 11, color: Look.Muted));
                var bar = new RangeBar(media, RangeOf(box), L10n.T($"Box {i + 1}"), box == editor.Selected, () => { editor.Select(box); media.Seek(RangeOf(box).Start); });
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                rows.Children.Add(row);
            }
            empty.IsVisible = editor.Boxes.Count == 0;
            whole.IsEnabled = trash.IsEnabled = editor.Selected != null;
            save.IsEnabled = editor.Boxes.Count > 0;
            editor.Draw();
        }
        rows_ = Rows;
        editor.Changed += Rows;
        editor.SelectionChanged += () => { whole.IsEnabled = trash.IsEnabled = editor.Selected != null; };
        media.Changed += () =>
        {
            editor.Draw();
            live.Source = media.IsPlaying ? media.Still.Source : null;
            live.InvalidateVisual();
            editor.ShowPicture(!media.IsPlaying);
            if (!media.IsPlaying) { pending.Stop(); pending.Start(); }
        };
        window.Body = stack;
        window.Trailing(save);
        Rows();
        return window;
    }

    /// <summary>One redaction's time range: the bar seeks and selects; its two white edges drag.</summary>
    private sealed class RangeBar : Control
    {
        private readonly MediaSession _media;
        private readonly Range _range;
        private readonly string _label;
        private readonly bool _selected;
        private readonly Action _pick;
        private int _edge;

        public RangeBar(MediaSession media, Range range, string label, bool selected, Action pick)
        {
            (_media, _range, _label, _selected, _pick) = (media, range, label, selected, pick);
            FlowDirection = FlowDirection.LeftToRight;
            Cursor = new Cursor(StandardCursorType.Hand);
            media.Changed += InvalidateVisual;
        }

        private double X(double t) => t / Math.Max(0.001, _media.Duration) * Bounds.Width;

        public override void Render(DrawingContext dc)
        {
            var (w, h) = (Bounds.Width, Bounds.Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x0D, 0, 0, 0)), null, new Rect(0, 0, w, h), 6, 6);
            var x1 = X(_range.Start);
            var x2 = X(_range.End);
            var bar = new Rect(x1, 0, Math.Max(12, x2 - x1), h);
            var fill = Color.FromArgb((byte)(_selected ? 0xBF : 0x73), Look.AccentColor.R, Look.AccentColor.G, Look.AccentColor.B);
            dc.DrawRectangle(new SolidColorBrush(fill), null, bar, 6, 6);
            var text = new FormattedText(_label, CultureInfo.CurrentUICulture, Look.Direction, new Typeface(Look.Font, FontStyle.Normal, FontWeight.SemiBold), 10, Brushes.White);
            if (text.Width < bar.Width - 16) dc.DrawText(text, new Point(bar.X + (bar.Width - text.Width) / 2, (h - text.Height) / 2));
            dc.DrawRectangle(Brushes.White, null, new Rect(x1, h / 2 - 8, 6, 16), 3, 3);
            dc.DrawRectangle(Brushes.White, null, new Rect(Math.Max(x1 + 6, x2 - 6), h / 2 - 8, 6, 16), 3, 3);
            var playhead = X(_media.Current);
            dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0xB3, 0x28, 0x2B, 0x29)), new Rect(playhead - 0.5, 0, 1, h));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var x = e.GetPosition(this).X;
            _edge = Math.Abs(x - X(_range.Start) - 3) < 9 ? 1 : Math.Abs(x - X(_range.End) + 3) < 9 ? 2 : 0;
            if (_edge == 0) _pick();
            else e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_edge == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var t = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1) * _media.Duration;
            if (_edge == 1) _range.Start = Math.Clamp(t, 0, _range.End - 0.1);
            else _range.End = Math.Clamp(t, _range.Start + 0.1, _media.Duration);
            _media.Seek(_edge == 1 ? _range.Start : _range.End);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _edge = 0;
            e.Pointer.Capture(null);
        }
    }
}
