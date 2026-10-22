using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Adawat.Core;
using Adawat.Core.Media;

namespace Adawat.App.Windows;

/// <summary>
/// Playback shared by the video and audio windows: a Windows media player when it can open the file, the current time,
/// frame stepping and range playback. When Windows cannot play the format, frames come from FFmpeg as the time changes.
/// </summary>
public sealed class MediaSession : IDisposable
{
    public string File { get; }
    public MediaInfo Info { get; }
    public double Duration => Info.Duration;
    public double FrameStep { get; }
    public MediaElement? Player { get; private set; }
    /// <summary>The picture shown when nothing plays: the first frame, then the frame at each seek when the player is missing.</summary>
    public Image Still { get; } = new() { Stretch = Stretch.Uniform };
    public double Current { get; private set; }
    public bool IsPlaying { get; private set; }
    public List<BitmapSource> Thumbnails { get; } = [];
    public float[] Waveform { get; private set; } = [];

    /// <summary>The time or play state changed.</summary>
    public event Action? Changed;
    /// <summary>The player opened the file and shows its frames itself.</summary>
    public event Action? Opened;
    public bool IsOpen => _ready;
    /// <summary>Thumbnails or the waveform arrived.</summary>
    public event Action? StripLoaded;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _stillTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private double? _stopAt;
    private bool _ready;
    private bool _disposed;

    private MediaSession(string file, MediaInfo info)
    {
        File = file;
        Info = info;
        FrameStep = 1 / Math.Max(info.FrameRate > 0 ? info.FrameRate : 30, 1);
        _timer.Tick += (_, _) => Tick();
        _stillTimer.Tick += async (_, _) => { _stillTimer.Stop(); await ShowStill(Current); };
        Player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Uniform, Volume = 1 };
        Player.MediaOpened += (_, _) =>
        {
            _ready = true;
            // Showing the first frame needs a play and pause; scrubbing then shows the frame at each seek.
            Player.Play();
            Player.Pause();
            Player.Position = TimeSpan.Zero;
            if (Info.HasVideo) Still.Visibility = Visibility.Collapsed;
            Opened?.Invoke();
            Changed?.Invoke();
        };
        Player.MediaFailed += (_, _) => Drop();
        Player.MediaEnded += (_, _) => { Pause(); Changed?.Invoke(); };
        Player.Source = new Uri(file);
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

    private void Drop()
    {
        if (Player == null) return;
        Player.Close();
        Player = null;
        _ready = false;
        Still.Visibility = Visibility.Visible;
        Changed?.Invoke();
    }

    public bool CanPlay => Player != null && _ready;

    private void Tick()
    {
        if (Player == null || !_ready) return;
        Current = Math.Min(Player.Position.TotalSeconds, Duration);
        if (_stopAt is { } stop && Current >= stop) Pause();
        Changed?.Invoke();
    }

    public void Seek(double seconds)
    {
        Current = Math.Clamp(seconds, 0, Duration);
        if (Player != null && _ready) Player.Position = TimeSpan.FromSeconds(Current);
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
        Player?.Pause();
        _timer.Stop();
        IsPlaying = false;
        _stopAt = null;
        Changed?.Invoke();
    }

    /// <summary>Plays from <paramref name="start"/> (or the current time) and stops at <paramref name="end"/>; a second press pauses.</summary>
    public void Play(double? start = null, double? end = null, double rate = 1)
    {
        if (Player == null || !_ready) return;
        if (IsPlaying) { Pause(); return; }
        if (start is { } s) Seek(s);
        else if (Current >= Duration - 0.05) Seek(0);
        _stopAt = end;
        Player.SpeedRatio = rate;
        Player.Play();
        Still.Visibility = Info.HasVideo ? Visibility.Collapsed : Visibility.Visible;
        IsPlaying = true;
        _timer.Start();
        Changed?.Invoke();
    }

    public void SetRate(double rate) { if (Player != null) Player.SpeedRatio = rate; }

    private async Task ShowStill(double time)
    {
        if (await Frame(time, 540) is { } frame) Still.Source = frame;
    }

    /// <summary>A still at <paramref name="seconds"/>, for previews and frame pickers.</summary>
    public async Task<BitmapSource?> Frame(double seconds, int height = 200)
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
        Player?.Stop();
        Player?.Close();
    }
}

/// <summary>Thumbnails or a waveform across the whole file, with a playhead. Clicking seeks unless a window takes the mouse itself.</summary>
public class Timeline : FrameworkElement
{
    protected readonly MediaSession Media;
    public Action<DrawingContext, double, double>? Overlay { get; set; }
    /// <summary>Mouse down, move and up at a time; when set, clicking no longer seeks by itself.</summary>
    public Func<double, Point, bool>? Pressed { get; set; }
    public Action<double>? Dragged { get; set; }
    public Action? Released { get; set; }
    public float[]? Levels { get; set; }
    public Brush WaveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xD9, Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B));
    public bool ShowPlayhead { get; set; } = true;
    private bool _dragging;

    public Timeline(MediaSession media, double height)
    {
        Media = media;
        Height = height;
        // Time runs left to right in every language.
        FlowDirection = FlowDirection.LeftToRight;
        Cursor = Cursors.Hand;
        media.Changed += InvalidateVisual;
        media.StripLoaded += InvalidateVisual;
    }

    public double TimeAt(double x) => Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * Media.Duration;
    public double XAt(double time) => time / Math.Max(Media.Duration, 0.001) * ActualWidth;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        dc.PushClip(clip);
        var levels = Levels ?? Media.Waveform;
        if (Media.Thumbnails.Count > 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), null, new Rect(0, 0, w, h));
            var slot = w / 10;
            for (var i = 0; i < Media.Thumbnails.Count; i++)
            {
                var image = Media.Thumbnails[i];
                // Fill each slot, cropping the picture's sides.
                var scale = Math.Max(slot / image.PixelWidth, h / image.PixelHeight);
                var size = new Size(image.PixelWidth * scale, image.PixelHeight * scale);
                dc.PushClip(new RectangleGeometry(new Rect(i * slot, 0, slot, h)));
                dc.DrawImage(image, new Rect(i * slot + (slot - size.Width) / 2, (h - size.Height) / 2, size.Width, size.Height));
                dc.Pop();
            }
        }
        else if (levels.Length > 0)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x14, Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B)), null, new Rect(0, 0, w, h));
            DrawWave(dc, levels, new Rect(0, 0, w, h), WaveBrush);
        }
        else dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), null, new Rect(0, 0, w, h));
        dc.Pop();
        Overlay?.Invoke(dc, w, h);
        if (ShowPlayhead)
        {
            var x = XAt(Media.Current);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD9, 0x28, 0x2B, 0x29)), null, new Rect(x - 0.75, 0, 1.5, h));
        }
    }

    public static void DrawWave(DrawingContext dc, float[] levels, Rect rect, Brush brush)
    {
        var step = rect.Width / levels.Length;
        for (var i = 0; i < levels.Length; i++)
        {
            var bar = Math.Max(1.5, levels[i] * rect.Height * 0.85);
            dc.DrawRectangle(brush, null, new Rect(rect.X + i * step, rect.Y + (rect.Height - bar) / 2, Math.Max(1, step * 0.55), bar));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var t = TimeAt(p.X);
        if (Pressed?.Invoke(t, p) != true) Media.Seek(t);
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var t = TimeAt(e.GetPosition(this).X);
        if (Dragged != null) Dragged(t); else Media.Seek(t);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        Released?.Invoke();
        InvalidateVisual();
    }
}

/// <summary>Builders for the pieces every media window shares.</summary>
public static class MediaUI
{
    /// <summary>The black, rounded video area.</summary>
    public static Border Stage(MediaSession media, double height)
    {
        var grid = new Grid { Background = Brushes.Black };
        if (media.Player != null) grid.Children.Add(media.Player);
        grid.Children.Add(media.Still);
        return new Border { Height = height, CornerRadius = new CornerRadius(12), Background = Brushes.Black, Child = grid, ClipToBounds = true };
    }

    /// <summary>Audio has no picture, but the player still has to be in the window to open the file.</summary>
    public static Grid AudioHost(MediaSession media)
    {
        var host = new Grid { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false };
        if (media.Player != null) host.Children.Add(media.Player);
        return host;
    }

    /// <summary>◁| ▶ |▷ and "current / total".</summary>
    public static DockPanel Transport(MediaSession media, Action? play = null, bool frames = true)
    {
        var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        Button Plain(string glyph, Action click, string help)
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
        var time = UI.Text("", 11, color: Theme.Muted);
        DockPanel.SetDock(time, Dock.Right);
        bar.Children.Add(time);
        void Update()
        {
            time.Text = L10n.T($"{Format.Time(media.Current)} / {Format.Time(media.Duration)}");
            ((TextBlock)playButton.Content).Text = media.IsPlaying ? "" : "";
            playButton.IsEnabled = media.CanPlay;
        }
        media.Changed += Update;
        Update();
        return bar;
    }

    /// <summary>A text field holding a time such as 0:12.50; typing a time and leaving the field sets it.</summary>
    public static TextBox TimeField(double value, Action<double> set)
    {
        var box = new TextBox { Width = 78, Height = 24, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Text = Format.Time(value), FlowDirection = FlowDirection.LeftToRight };
        void Commit() { if (Format.ParseTime(box.Text) is { } seconds) set(seconds); }
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
    private readonly MediaPlayer _player = new();
    private CancellationTokenSource? _pending;
    public string? Playing { get; private set; }
    public event Action? Changed;

    public PreviewPlayer()
    {
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, e) => { Stop(); Hud.Shared.ShowMessage(L10n.T("Could not play the preview. ") + e.ErrorException?.Message, Hud.Style.Failure); };
    }

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
            _player.Open(new Uri(path));
            _player.Play();
        }
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
        _player.Stop();
        _player.Close();
        Playing = null;
        Changed?.Invoke();
    }
}
