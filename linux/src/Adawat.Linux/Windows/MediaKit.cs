using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Adawat.Core;
using Adawat.Core.Media;

namespace Adawat.Linux.Windows;

/// <summary>
/// Playback shared by the video and audio windows, built on FFmpeg since Linux has no player every desktop shares:
/// pictures are decoded to raw frames and drawn as their time comes, sound is decoded and handed to PipeWire or
/// PulseAudio. When paused, the picture is a still from FFmpeg at the current time.
/// </summary>
public sealed class MediaSession : IDisposable
{
    public string File { get; }
    public MediaInfo Info { get; }
    public double Duration => Info.Duration;
    public double FrameStep { get; }
    /// <summary>The picture: a still when paused, the live frames while playing.</summary>
    public Image Still { get; } = new() { Stretch = Stretch.Uniform };
    public double Current { get; private set; }
    public bool IsPlaying { get; private set; }
    public List<Bitmap> Thumbnails { get; } = [];
    public float[] Waveform { get; private set; } = [];

    /// <summary>The time or play state changed.</summary>
    public event Action? Changed;
    /// <summary>Thumbnails or the waveform arrived.</summary>
    public event Action? StripLoaded;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _stillTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Stopwatch _clock = new();
    private Playback? _playback;
    private double _startedAt, _rate = 1;
    private double? _stopAt;
    private bool _disposed;

    private MediaSession(string file, MediaInfo info)
    {
        File = file;
        Info = info;
        FrameStep = 1 / Math.Max(info.FrameRate > 0 ? info.FrameRate : 30, 1);
        _timer.Tick += (_, _) => Tick();
        _stillTimer.Tick += async (_, _) => { _stillTimer.Stop(); await ShowStill(Current); };
    }

    /// <summary>Probes the file; null (with a message) when its length cannot be read.</summary>
    public static MediaSession? Load(string file)
    {
        var info = Task.Run(() => MediaProbe.Info(file)).GetAwaiter().GetResult();
        if (info.Duration <= 0)
        {
            Hud.Shared.ShowMessage(L10n.T($"Couldn't read the length of {Path.GetFileName(file)}"), Hud.Style.Failure);
            return null;
        }
        var session = new MediaSession(file, info);
        if (info.HasVideo) _ = session.ShowStill(0);
        return session;
    }

    /// <summary>Stops playback when <paramref name="window"/> closes and starts loading the strip.</summary>
    public void Attach(Window window, int thumbnails = 10, int bars = 240)
    {
        window.Closed += (_, _) => Dispose();
        _ = LoadStrip(thumbnails, bars);
    }

    public bool CanPlay => ProcessRunner.Find("ffmpeg") != null && (!Info.HasAudio || Playback.AudioOut != null || Info.HasVideo);

    private void Tick()
    {
        if (!IsPlaying) return;
        Current = Math.Min(_startedAt + _clock.Elapsed.TotalSeconds * _rate, Duration);
        if (_stopAt is { } stop && Current >= stop || Current >= Duration - 0.01 || _playback?.Finished == true && !Info.HasVideo)
        {
            Pause();
            return;
        }
        Changed?.Invoke();
    }

    public void Seek(double seconds)
    {
        Current = Math.Clamp(seconds, 0, Duration);
        if (IsPlaying)
        {
            Restart();
        }
        else if (Info.HasVideo) { _stillTimer.Stop(); _stillTimer.Start(); }
        Changed?.Invoke();
    }

    public void Step(int frames)
    {
        Pause();
        Seek(Current + frames * FrameStep);
    }

    public void Pause()
    {
        _playback?.Dispose();
        _playback = null;
        _timer.Stop();
        _clock.Reset();
        var was = IsPlaying;
        IsPlaying = false;
        _stopAt = null;
        if (was && Info.HasVideo) _ = ShowStill(Current);
        Changed?.Invoke();
    }

    /// <summary>Plays from <paramref name="start"/> (or the current time) and stops at <paramref name="end"/>; a second press pauses.</summary>
    public void Play(double? start = null, double? end = null, double rate = 1)
    {
        if (!CanPlay) return;
        if (IsPlaying) { Pause(); return; }
        if (start is { } s) Current = Math.Clamp(s, 0, Duration);
        else if (Current >= Duration - 0.05) Current = 0;
        _stopAt = end;
        _rate = rate;
        IsPlaying = true;
        Restart();
        _timer.Start();
        Changed?.Invoke();
    }

    public void SetRate(double rate)
    {
        if (Math.Abs(rate - _rate) < 0.001) return;
        _rate = rate;
        if (IsPlaying) Restart();
    }

    private void Restart()
    {
        _playback?.Dispose();
        _startedAt = Current;
        var length = (_stopAt ?? Duration) - Current;
        _playback = new Playback(this, Current, Math.Max(0.05, length), _rate);
        _clock.Restart();
    }

    private async Task ShowStill(double time)
    {
        if (await Frame(time, 540) is { } frame && !IsPlaying) Still.Source = frame;
    }

    /// <summary>A still at <paramref name="seconds"/>, for previews and frame pickers.</summary>
    public async Task<Bitmap?> Frame(double seconds, int height = 200)
    {
        var bytes = await Task.Run(() => MediaTools.Thumbnail(File, Math.Min(seconds, Math.Max(0, Duration - 0.04)), height));
        return Pictures.FromBytes(bytes);
    }

    private async Task LoadStrip(int count, int bars)
    {
        if (Info.HasVideo)
        {
            for (var index = 0; index < count && !_disposed; index++)
            {
                if (await Frame(Duration * (index + 0.5) / count, 90) is { } image) Thumbnails.Add(image);
                StripLoaded?.Invoke();
            }
        }
        else if (Info.HasAudio)
        {
            Waveform = await Task.Run(() => MediaOps.Waveform(File, bars));
            StripLoaded?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _playback?.Dispose();
    }

    /// <summary>One run of playback from a time: an FFmpeg decoding frames into a bitmap, and one decoding sound into the
    /// desktop's audio server. The window's clock decides when each frame shows.</summary>
    private sealed class Playback : IDisposable
    {
        private readonly List<Process> _processes = [];
        private readonly CancellationTokenSource _stop = new();
        public bool Finished { get; private set; }

        public static string? AudioOut => ProcessRunner.Find("pw-play") ?? ProcessRunner.Find("paplay");

        public Playback(MediaSession media, double start, double length, double rate)
        {
            var ffmpeg = ProcessRunner.Find("ffmpeg")!;
            var from = FFmpeg.Seconds(start);
            var span = FFmpeg.Seconds(length);
            if (media.Info.HasVideo) StartVideo(media, ffmpeg, from, span, rate);
            if (media.Info.HasAudio && AudioOut is { } output) StartAudio(ffmpeg, output, media.File, from, span, rate);
        }

        private void StartVideo(MediaSession media, string ffmpeg, string from, string span, double rate)
        {
            // Frames at the window's size, at most 30 a second; sizes are even for the scaler.
            var height = Math.Min(540, media.Info.Height > 0 ? media.Info.Height : 540) / 2 * 2;
            var width = (int)Math.Round(height * (double)Math.Max(1, media.Info.Width) / Math.Max(1, media.Info.Height)) / 2 * 2;
            if (width <= 0 || height <= 0) return;
            var fps = Math.Min(30, media.Info.FrameRate > 0 ? media.Info.FrameRate : 30);
            var process = Start(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-ss", from, "-i", media.File, "-t", span, "-an", "-sn",
                                         "-vf", $"fps={fps.ToString(CultureInfo.InvariantCulture)},scale={width}:{height}", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"], stdout: true);
            if (process == null) return;
            var token = _stop.Token;
            var clock = Stopwatch.StartNew();
            var bitmaps = new[] { new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque),
                                  new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque) };
            Task.Run(async () =>
            {
                var frame = new byte[width * height * 4];
                var stream = process.StandardOutput.BaseStream;
                for (var index = 0; !token.IsCancellationRequested; index++)
                {
                    var read = 0;
                    while (read < frame.Length)
                    {
                        var n = await stream.ReadAsync(frame.AsMemory(read), token).ConfigureAwait(false);
                        if (n == 0) { Finished = true; return; }
                        read += n;
                    }
                    // Each frame waits for its moment on the clock; late frames are dropped.
                    var due = index / fps / rate;
                    var wait = due - clock.Elapsed.TotalSeconds;
                    if (wait < -0.1) continue;
                    if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), token).ConfigureAwait(false);
                    var bitmap = bitmaps[index % 2];
                    using (var locked = bitmap.Lock()) System.Runtime.InteropServices.Marshal.Copy(frame, 0, locked.Address, Math.Min(frame.Length, locked.RowBytes * height));
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        media.Still.Source = bitmap;
                        media.Still.InvalidateVisual();
                    });
                }
            }, token).ContinueWith(_ => { });
        }

        private void StartAudio(string ffmpeg, string output, string file, string from, string span, double rate)
        {
            var tempo = AtempoChain(rate);
            var decoder = Start(ffmpeg, ["-hide_banner", "-nostdin", "-loglevel", "error", "-ss", from, "-i", file, "-t", span, "-vn", "-sn",
                                         .. tempo.Length > 0 ? new[] { "-af", tempo } : [], "-ac", "2", "-ar", "48000", "-f", "s16le", "pipe:1"], stdout: true);
            string[] playArgs = Path.GetFileName(output) == "pw-play"
                ? ["--format", "s16", "--rate", "48000", "--channels", "2", "-"]
                : ["--raw", "--format=s16le", "--rate=48000", "--channels=2"];
            var player = Start(output, playArgs, stdin: true);
            if (decoder == null || player == null) return;
            var token = _stop.Token;
            Task.Run(async () =>
            {
                try { await decoder.StandardOutput.BaseStream.CopyToAsync(player.StandardInput.BaseStream, token).ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
                try { player.StandardInput.Close(); } catch (Exception) { }
                Finished = true;
            }, token).ContinueWith(_ => { });
        }

        /// <summary>atempo takes 0.5…2 per step, so other speeds chain several.</summary>
        private static string AtempoChain(double rate)
        {
            if (Math.Abs(rate - 1) < 0.001) return "";
            var steps = new List<string>();
            while (rate > 2) { steps.Add("atempo=2"); rate /= 2; }
            while (rate < 0.5) { steps.Add("atempo=0.5"); rate /= 0.5; }
            steps.Add($"atempo={rate.ToString("0.###", CultureInfo.InvariantCulture)}");
            return string.Join(",", steps);
        }

        private Process? Start(string tool, IEnumerable<string> arguments, bool stdout = false, bool stdin = false)
        {
            try
            {
                var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = stdout, RedirectStandardInput = stdin, RedirectStandardError = true };
                foreach (var argument in arguments) info.ArgumentList.Add(argument);
                var process = Process.Start(info)!;
                _ = process.StandardError.ReadToEndAsync();
                _processes.Add(process);
                return process;
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { return null; }
        }

        public void Dispose()
        {
            _stop.Cancel();
            foreach (var process in _processes)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }
}

/// <summary>Thumbnails or a waveform across the whole file, with a playhead. Clicking seeks unless a window takes the mouse itself.</summary>
public class Timeline : Control
{
    protected readonly MediaSession Media;
    public Action<DrawingContext, double, double>? Overlay { get; set; }
    /// <summary>Mouse down, move and up at a time; when set, clicking no longer seeks by itself.</summary>
    public Func<double, Point, bool>? Pressed { get; set; }
    public Action<double>? Dragged { get; set; }
    public Action? Released { get; set; }
    public float[]? Levels { get; set; }
    public IBrush WaveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xD9, Look.AccentColor.R, Look.AccentColor.G, Look.AccentColor.B));
    public bool ShowPlayhead { get; set; } = true;
    private bool _dragging;

    public Timeline(MediaSession media, double height)
    {
        Media = media;
        Height = height;
        // Time runs left to right in every language.
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = new Cursor(StandardCursorType.Hand);
        media.Changed += InvalidateVisual;
        media.StripLoaded += InvalidateVisual;
    }

    public double TimeAt(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Media.Duration;
    public double XAt(double time) => time / Math.Max(Media.Duration, 0.001) * Bounds.Width;

    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var levels = Levels ?? Media.Waveform;
        using (dc.PushClip(new RoundedRect(new Rect(0, 0, w, h), 8)))
        {
            if (Media.Thumbnails.Count > 0)
            {
                dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), new Rect(0, 0, w, h));
                var slot = w / 10;
                for (var i = 0; i < Media.Thumbnails.Count; i++)
                {
                    var image = Media.Thumbnails[i];
                    // Fill each slot, cropping the picture's sides.
                    var scale = Math.Max(slot / image.PixelSize.Width, h / image.PixelSize.Height);
                    var size = new Size(image.PixelSize.Width * scale, image.PixelSize.Height * scale);
                    using (dc.PushClip(new Rect(i * slot, 0, slot, h)))
                        dc.DrawImage(image, new Rect(i * slot + (slot - size.Width) / 2, (h - size.Height) / 2, size.Width, size.Height));
                }
            }
            else if (levels.Length > 0)
            {
                dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0x14, Look.AccentColor.R, Look.AccentColor.G, Look.AccentColor.B)), new Rect(0, 0, w, h));
                DrawWave(dc, levels, new Rect(0, 0, w, h), WaveBrush);
            }
            else dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), new Rect(0, 0, w, h));
        }
        Overlay?.Invoke(dc, w, h);
        if (ShowPlayhead)
        {
            var x = XAt(Media.Current);
            dc.FillRectangle(new SolidColorBrush(Color.FromArgb(0xD9, 0x28, 0x2B, 0x29)), new Rect(x - 0.75, 0, 1.5, h));
        }
    }

    public static void DrawWave(DrawingContext dc, float[] levels, Rect rect, IBrush brush)
    {
        var step = rect.Width / levels.Length;
        for (var i = 0; i < levels.Length; i++)
        {
            var bar = Math.Max(1.5, levels[i] * rect.Height * 0.85);
            dc.FillRectangle(brush, new Rect(rect.X + i * step, rect.Y + (rect.Height - bar) / 2, Math.Max(1, step * 0.55), bar));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var t = TimeAt(p.X);
        if (Pressed?.Invoke(t, p) != true) Media.Seek(t);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;
        var t = TimeAt(e.GetPosition(this).X);
        if (Dragged != null) Dragged(t); else Media.Seek(t);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        Released?.Invoke();
        InvalidateVisual();
    }
}

/// <summary>Builders for the pieces every media window shares.</summary>
public static class MediaUI
{
    public static readonly FontFamily Mono = new("DejaVu Sans Mono, Noto Sans Mono, monospace");

    /// <summary>The black, rounded video area.</summary>
    public static Border Stage(MediaSession media, double height)
    {
        var grid = new Panel { Background = Brushes.Black };
        grid.Children.Add(media.Still);
        return new Border { Height = height, CornerRadius = new CornerRadius(12), Background = Brushes.Black, Child = grid, ClipToBounds = true };
    }

    /// <summary>Audio has no picture; FFmpeg plays it without anything in the window.</summary>
    public static Panel AudioHost(MediaSession media) => new() { Width = 1, Height = 1, IsHitTestVisible = false };

    /// <summary>◁| ▶ |▷ and "current / total".</summary>
    public static DockPanel Transport(MediaSession media, Action? play = null, bool frames = true)
    {
        var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        SoftButton Plain(string glyph, Action click, string help)
        {
            var button = UI.IconButton(glyph, click, help, 28);
            button.Margin = new Thickness(0, 0, 8, 0);
            return button;
        }
        if (frames) buttons.Children.Add(Plain("", () => media.Step(-1), "Previous frame"));
        var playButton = Plain("", play ?? (() => media.Play()), "Play");
        buttons.Children.Add(playButton);
        if (frames) buttons.Children.Add(Plain("", () => media.Step(1), "Next frame"));
        DockPanel.SetDock(buttons, Dock.Left);
        bar.Children.Add(buttons);
        var time = UI.Text("", 11, color: Look.Muted);
        DockPanel.SetDock(time, Dock.Right);
        bar.Children.Add(time);
        void Update()
        {
            time.Text = L10n.T($"{Format.Time(media.Current)} / {Format.Time(media.Duration)}");
            ((TextBlock)playButton.Child!).Text = Icons.Glyph(media.IsPlaying ? "" : "");
            playButton.IsEnabled = media.CanPlay;
        }
        media.Changed += Update;
        Update();
        return bar;
    }

    /// <summary>A text field holding a time such as 0:12.50; typing a time and leaving the field sets it.</summary>
    public static TextBox TimeField(double value, Action<double> set)
    {
        var box = new TextBox { Width = 84, MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = Mono, FontSize = 12, Text = Format.Time(value), FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(6, 2) };
        void Commit() { if (Format.ParseTime(box.Text ?? "") is { } seconds) set(seconds); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return box;
    }

    public static Core.Images.Rgb Rgb(Color color) => new(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Opens a media window after probing the file; null when it cannot be read.</summary>
    public static Window? Open(IReadOnlyList<string> files, Func<MediaSession, ToolWindow> make, int thumbnails = 10, int bars = 240)
    {
        if (files.Count == 0 || MediaSession.Load(files[0]) is not { } media) return null;
        var window = make(media);
        media.Attach(window, thumbnails, bars);
        return window;
    }
}

/// <summary>Plays short renderings made just for listening (one channel, the bleeped result).</summary>
public sealed class PreviewPlayer
{
    private CancellationTokenSource? _pending;
    private Process? _process;
    public string? Playing { get; private set; }
    public event Action? Changed;

    public async void Toggle(string key, Func<Task<string>> render)
    {
        if (Playing == key) { Stop(); return; }
        Stop();
        Playing = key;
        Changed?.Invoke();
        var cts = _pending = new CancellationTokenSource();
        try
        {
            var path = await Task.Run(render);
            if (cts.IsCancellationRequested || Playing != key) return;
            var player = ProcessRunner.Find("pw-play") ?? ProcessRunner.Find("paplay") ?? throw new AdawatException("No audio player was found (pw-play or paplay).");
            var info = new ProcessStartInfo(player) { UseShellExecute = false, RedirectStandardError = true };
            info.ArgumentList.Add(path);
            _process = Process.Start(info);
            if (_process == null) return;
            await _process.WaitForExitAsync(cts.Token);
            if (Playing == key) Stop();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Playing = null;
            Changed?.Invoke();
            Hud.Shared.ShowMessage(L10n.T("Could not play the preview. ") + e.Message, Hud.Style.Failure);
        }
    }

    public void Stop()
    {
        _pending?.Cancel();
        try { if (_process is { HasExited: false }) _process.Kill(); } catch (InvalidOperationException) { }
        _process = null;
        Playing = null;
        Changed?.Invoke();
    }
}
