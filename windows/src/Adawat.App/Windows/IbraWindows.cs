using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Adawat.Core;
using Adawat.Core.Ibra;
using Microsoft.Win32;

namespace Adawat.App.Windows;

/// <summary>Option windows for the IBRA tools. Each one saves its choices so the next drop starts from them.</summary>
public static class IbraWindows
{
    private static TextBlock FileLine(IReadOnlyList<string> files)
    {
        var name = UI.Text(files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files", 12, color: Theme.Muted);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Margin = new Thickness(0, 0, 0, 8);
        return name;
    }

    private static ToolWindow Frame(string title, StackPanel body, string action, Action run, double width = 440)
    {
        var window = new ToolWindow(title, width) { Body = body };
        window.Trailing(UI.Button("Cancel", window.Close), UI.Button(action, () => { run(); window.Close(); }, primary: true));
        return window;
    }

    // MARK: Speech to text

    public static Window? Transcribe(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var spoken = s.TranscribeSpoken;
        var target = s.TranscribeTarget;
        var model = s.TranscribeModel;
        var burn = s.TranscribeBurn;
        var text = true;
        var subtitles = true;
        var stack = new StackPanel();
        stack.Children.Add(FileLine(files));
        stack.Children.Add(UI.Row("Spoken language", UI.Picker(Transcription.Languages.Select(l => (l.Code, l.Name)).ToList(), spoken, v => spoken = v), 36));
        stack.Children.Add(UI.Row("Write it in", UI.Picker(Transcription.Targets.Select(l => (l.Code, l.Name)).ToList(), target, v => target = v), 36));
        stack.Children.Add(UI.Caption("Arabic is written in Modern Standard Arabic. Translating to English works offline; other languages need the internet."));
        stack.Children.Add(UI.Divider());
        stack.Children.Add(UI.Row("Accuracy", UI.Picker(Transcription.Models.ToList(), model, v => model = v), 36));
        var note = UI.Caption(Transcription.HasModel(model) ? "" : "The speech model downloads once the first time.");
        stack.Children.Add(note);
        stack.Children.Add(UI.Divider());
        stack.Children.Add(UI.Check("Text file (TXT)", text, v => text = v).Pad(0, 3));
        stack.Children.Add(UI.Check("Subtitles with timing (SRT)", subtitles, v => subtitles = v).Pad(0, 3));
        if (files.Any(f => FileKinds.Of(f) == FileKind.Video))
            stack.Children.Add(UI.Check("Draw the subtitles on the video (for Reels)", burn, v => burn = v).Pad(0, 3));
        stack.Children.Add(UI.Caption("Experimental: check the text before you publish it.").Pad(0, 6));
        return Frame("Speech to Text", stack, "Start", () =>
        {
            s.TranscribeSpoken = spoken;
            s.TranscribeTarget = target;
            s.TranscribeModel = model;
            s.TranscribeBurn = burn;
            s.Save();
            var options = new TranscribeOptions { Spoken = spoken, Target = target, Model = model, WriteText = text, WriteSubtitles = subtitles, Burn = burn };
            if (!options.WriteText && !options.WriteSubtitles && !options.Burn) options = options with { WriteText = true };
            Runner.Run("Speech to Text", files, (f, c) => Transcription.Run(f, options, c));
        });
    }

    // MARK: Watermark

    private static readonly IReadOnlyList<(string, string)> Positions =
    [
        ("bottom-right", "Bottom right"), ("bottom-left", "Bottom left"), ("top-right", "Top right"), ("top-left", "Top left"), ("center", "Center"),
    ];

    public static Window? Watermark(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var logo = s.WatermarkLogo;
        var position = s.WatermarkPosition;
        var scale = s.WatermarkScale;
        var opacity = s.WatermarkOpacity;
        var stack = new StackPanel();
        stack.Children.Add(FileLine(files));
        var logoName = UI.Text(File.Exists(logo) ? Path.GetFileName(logo) : "No logo", 12, color: Theme.Muted);
        logoName.TextTrimming = TextTrimming.CharacterEllipsis;
        logoName.MaxWidth = 170;
        var logoRow = new StackPanel { Orientation = Orientation.Horizontal };
        logoRow.Children.Add(logoName);
        logoRow.Children.Add(UI.Button("Choose…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "PNG / JPG|*.png;*.jpg;*.jpeg;*.webp", Title = L10n.T("Choose your logo (PNG with transparency is best)") };
            if (dialog.ShowDialog() == true) { logo = dialog.FileName; logoName.Text = Path.GetFileName(logo); }
        }).Pad(8, 0));
        logoRow.Children.Add(UI.IconButton("", () => { logo = ""; logoName.Text = L10n.T("No logo"); }, "Remove the logo", 24));
        stack.Children.Add(UI.Row("Logo", logoRow, 36));
        var text = new TextBox { Text = s.WatermarkText, Width = 220, FontSize = 13, Padding = new Thickness(4, 3, 4, 3), FlowDirection = FlowDirection.LeftToRight };
        stack.Children.Add(UI.Row("Text (number, @account)", text, 36));
        stack.Children.Add(UI.Row("Place", UI.Picker(Positions, position, v => position = v), 36));
        stack.Children.Add(UI.Row("Size", UI.Slider(0.06, 0.5, scale, v => scale = v, v => L10n.T($"{Math.Round(v * 100)}%"), width: 180)));
        stack.Children.Add(UI.Row("Opacity", UI.Slider(0.2, 1, opacity, v => opacity = v, v => L10n.T($"{Math.Round(v * 100)}%"), width: 180)));
        stack.Children.Add(UI.Caption("The logo and text are saved, so next time one drop is enough.").Pad(0, 6));
        return Frame("Watermark", stack, "Add Watermark", () =>
        {
            s.WatermarkLogo = logo;
            s.WatermarkText = text.Text.Trim();
            s.WatermarkPosition = position;
            s.WatermarkScale = scale;
            s.WatermarkOpacity = opacity;
            s.Save();
            var options = WatermarkOptions.Saved;
            Runner.Run("Watermark", files, (f, c) => SocialOps.Watermark(f, options, c));
        });
    }

    // MARK: Social size

    public static Window? Social(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var size = SocialOps.Sizes.FirstOrDefault(x => x.Id == s.SocialSize) ?? SocialOps.Sizes[0];
        var stack = new StackPanel();
        stack.Children.Add(FileLine(files));
        stack.Children.Add(UI.Row("Size", UI.Picker(SocialOps.Sizes.Select(x => (x, x.Title)).ToList(), size, v => size = v), 36));
        stack.Children.Add(UI.Caption("Nothing is cut off: the empty space is filled with a blurred copy of the same picture."));
        return Frame("Social Size", stack, "Resize", () =>
        {
            s.SocialSize = size.Id;
            s.Save();
            var chosen = size;
            Runner.Run("Social Size", files, (f, c) => SocialOps.Resize(f, chosen, c));
        });
    }

    // MARK: Before and after

    public static Window? BeforeAfter(IReadOnlyList<string> files)
    {
        if (files.Count != 2) return null;
        var sideBySide = true;
        var ordered = files.OrderBy(f => File.GetLastWriteTimeUtc(f)).ToList();
        var stack = new StackPanel();
        var before = new TextBox { Text = "قبل", Width = 140, FontSize = 13, Padding = new Thickness(4, 3, 4, 3) };
        var after = new TextBox { Text = "بعد", Width = 140, FontSize = 13, Padding = new Thickness(4, 3, 4, 3) };
        var swap = false;
        var order = UI.Text($"{Path.GetFileName(ordered[0])}  →  {Path.GetFileName(ordered[1])}", 12, color: Theme.Muted);
        order.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(order);
        stack.Children.Add(UI.Row("Order", UI.Button("Swap", () =>
        {
            swap = !swap;
            var a = swap ? ordered[1] : ordered[0];
            var b = swap ? ordered[0] : ordered[1];
            order.Text = $"{Path.GetFileName(a)}  →  {Path.GetFileName(b)}";
        }, icon: ""), 36));
        stack.Children.Add(UI.Row("Layout", UI.Segmented([(true, "Side by side"), (false, "Top and bottom")], true, v => sideBySide = v), 36));
        stack.Children.Add(UI.Row("First label", before, 36));
        stack.Children.Add(UI.Row("Second label", after, 36));
        return Frame("Before and After", stack, "Make", () =>
        {
            List<string> pair = swap ? [ordered[1], ordered[0]] : [ordered[0], ordered[1]];
            // Give them modification order the core reads: write times are not touched, so pass the order explicitly.
            var first = before.Text.Trim();
            var second = after.Text.Trim();
            var layout = sideBySide;
            Runner.Run("Before and After", pair, (f, c) => SocialOps.BeforeAfterOrdered(f, layout, first, second, c));
        });
    }

    // MARK: Upscale

    public static Window? Upscale(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var factor = s.UpscaleFactor >= 4 ? 4 : 2;
        var stack = new StackPanel();
        stack.Children.Add(FileLine(files));
        stack.Children.Add(UI.Row("Enlarge", UI.Segmented([(2, "×2"), (4, "×4")], factor, v => factor = v), 36));
        stack.Children.Add(UI.Caption("Uses the graphics card. Large pictures at ×4 can take a minute."));
        return Frame("Enlarge with AI", stack, "Enlarge", () =>
        {
            s.UpscaleFactor = factor;
            s.Save();
            var f2 = factor;
            Runner.Run("Enlarge with AI", files, (f, c) => Core.Ibra.Upscale.Run(f, f2, c));
        });
    }

    // MARK: Stories

    public static Window? Stories(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var seconds = s.StorySeconds is 15 or 30 or 60 or 90 ? s.StorySeconds : 60;
        var stack = new StackPanel();
        stack.Children.Add(FileLine(files));
        stack.Children.Add(UI.Row("Each part", UI.Segmented([(15, "15 s"), (30, "30 s"), (60, "60 s"), (90, "90 s")], seconds, v => seconds = v), 36));
        return Frame("Cut into Stories", stack, "Cut", () =>
        {
            s.StorySeconds = seconds;
            s.Save();
            var length = seconds;
            Runner.Run("Cut into Stories", files, (f, c) => SocialOps.StorySplit(f, length, c));
        });
    }

    // MARK: Slideshow

    public static Window? Slideshow(IReadOnlyList<string> files)
    {
        var s = Settings.Current;
        var seconds = s.SlideSeconds;
        var size = SocialOps.Sizes[0];
        var pictures = files.Count(f => FileKinds.Of(f) == FileKind.Image);
        var song = files.FirstOrDefault(f => FileKinds.Of(f) == FileKind.Audio);
        var stack = new StackPanel();
        stack.Children.Add(UI.Text(song != null ? $"{pictures} pictures with {Path.GetFileName(song)}" : $"{pictures} pictures, no song", 12, color: Theme.Muted).Pad(0, 4));
        stack.Children.Add(UI.Row("Each picture", UI.Slider(1, 8, seconds, v => seconds = v, v => L10n.T($"{v:0.#} s"), width: 180, step: 0.5)));
        stack.Children.Add(UI.Row("Size", UI.Picker(SocialOps.Sizes.Select(x => (x, x.Title)).ToList(), size, v => size = v), 36));
        stack.Children.Add(UI.Caption("Tip: drag a song together with the pictures to put music under them."));
        return Frame("Slideshow Video", stack, "Make Video", () =>
        {
            s.SlideSeconds = seconds;
            s.Save();
            var chosen = size;
            var each = seconds;
            Runner.Run("Slideshow Video", files, (f, c) => SocialOps.Slideshow(f, each, chosen, c));
        });
    }

    // MARK: Download from a link

    public static void DownloadLink()
    {
        var s = Settings.Current;
        var quality = s.DownloadQuality;
        var stack = new StackPanel();
        var link = new TextBox { Width = 380, FontSize = 13, Padding = new Thickness(6, 5, 6, 5), FlowDirection = FlowDirection.LeftToRight };
        try
        {
            var clip = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText().Trim() : "";
            if (clip.StartsWith("http", StringComparison.OrdinalIgnoreCase)) link.Text = clip;
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
        stack.Children.Add(UI.Text("Paste a link from YouTube, Instagram, TikTok, Facebook or X", 12, color: Theme.Muted).Pad(0, 4));
        stack.Children.Add(link);
        stack.Children.Add(UI.Row("Quality", UI.Picker(LinkDownload.Qualities.ToList(), quality, v => quality = v), 40));
        stack.Children.Add(UI.Caption("Saved in Downloads. Download only what you have the right to keep."));
        var window = Frame("Download from Link", stack, "Download", () =>
        {
            s.DownloadQuality = quality;
            s.Save();
            var url = link.Text.Trim();
            var q = quality;
            Runner.Run("Download from Link", [], (_, c) => LinkDownload.Run(url, q, c));
        }, 440);
        window.Loaded += (_, _) => { link.Focus(); link.SelectAll(); };
        window.Present();
    }
}
