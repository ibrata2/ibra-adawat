using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Adawat.Core;
using Adawat.Core.Images;

namespace Adawat.Linux.Windows;

/// <summary>Compress: a preset, an optional size limit, and a live estimate of the result.</summary>
public static class CompressWindow
{
    private static readonly int[] ImageSizes = [3840, 2560, 2048, 1920, 1600, 1280, 1024, 800, 640];
    private static readonly int[] VideoHeights = [2160, 1440, 1080, 720, 480];

    public static Window? Make(IReadOnlyList<string> files)
    {
        var kind = files.CommonKind();
        if (kind is not (FileKind.Image or FileKind.Video or FileKind.Audio)) return null;
        var app = Settings.Current;
        var preset = app.CompressPreset;
        var imageMax = app.CompressImageMaxPixels;
        var videoMax = app.CompressVideoMaxHeight;
        var useTarget = false;
        var bySize = true;
        var original = files.Sum(f => new FileInfo(f).Length);
        var half = original / Math.Max(1, files.Count) / 2.0;
        var unit = half >= 1e9 ? "GB" : half >= 1e6 ? "MB" : "KB";
        double UnitBytes(string u) => u switch { "GB" => 1e9, "MB" => 1e6, _ => 1e3 };
        var targetValue = Math.Round(half / UnitBytes(unit) * 10) / 10;
        var reduction = 50.0;

        var window = new ToolWindow("Compress", 400);
        var stack = new StackPanel();
        var name = UI.Text(files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files", 12, color: Look.Muted);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(name);
        stack.Children.Add(UI.Text($"Original size: {Format.Bytes(original)}", 14, FontWeight.SemiBold).Pad(0, 2));

        var presetBlock = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        presetBlock.Children.Add(UI.Text("Compression preset", 14, FontWeight.SemiBold));
        var detail = UI.Caption(preset.Detail());
        var estimateText = UI.Text("—", 13, FontWeight.SemiBold);
        var savedText = UI.Text("", 12, color: Look.Muted);
        var message = UI.Text("", 11.5, color: Look.Accent, wrap: true);
        var compressButton = UI.Button("Compress", () => { }, primary: true);

        CompressSettings Current()
        {
            CompressTarget? target = useTarget ? (bySize ? new CompressTarget.Bytes((long)(targetValue * UnitBytes(unit))) : new CompressTarget.Reduction(reduction / 100)) : null;
            return new CompressSettings(preset, kind == FileKind.Image && imageMax > 0 ? imageMax : null, kind == FileKind.Video && videoMax > 0 ? videoMax : null, target);
        }
        string? Problem()
        {
            if (!useTarget) return null;
            if (bySize)
            {
                var bytes = targetValue * UnitBytes(unit);
                if (bytes < 1000) return "Enter a target size of at least 1 KB.";
                if (files.Count == 1 && bytes >= original) return "Target size cannot exceed the original file size.";
            }
            return null;
        }
        CancellationTokenSource? pending = null;
        async void Refresh()
        {
            var problem = Problem();
            message.Text = problem != null ? L10n.T(problem) : "";
            message.IsVisible = problem != null;
            compressButton.IsEnabled = problem == null;
            pending?.Cancel();
            var cts = pending = new CancellationTokenSource();
            estimateText.Text = "…";
            savedText.Text = "";
            try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
            var estimate = await Compression.Estimate(files, kind!.Value, Current());
            if (cts.IsCancellationRequested) return;
            estimateText.Text = estimate is { } e ? Format.Bytes(e) : "—";
            if (estimate is { } value && original > 0)
            {
                var saved = 100 - (int)Math.Round((double)value / original * 100);
                savedText.Text = L10n.T(saved > 0 ? $"about {saved}% smaller" : "little to gain");
            }
        }

        var presets = UI.Segmented([(CompressPreset.Light, "Light"), (CompressPreset.Balanced, "Balanced"), (CompressPreset.Strong, "Strong")], preset, p =>
        {
            preset = p;
            detail.Text = L10n.T(p.Detail());
            Refresh();
        });
        presets.HorizontalAlignment = HorizontalAlignment.Left;
        presets.Margin = new Thickness(0, 6, 0, 4);
        presetBlock.Children.Add(presets);
        presetBlock.Children.Add(detail);
        stack.Children.Add(presetBlock);

        if (kind == FileKind.Image)
        {
            var options = new List<(int, string)> { (0, "Keep original dimensions") };
            options.AddRange(ImageSizes.Select(s => (s, $"{s} px longest edge")));
            stack.Children.Add(UI.Row("Image size", UI.Picker(options, imageMax, v => { imageMax = v; Refresh(); })).Pad(0, 8));
        }
        else if (kind == FileKind.Video)
        {
            var options = new List<(int, string)> { (0, "Keep original") };
            options.AddRange(VideoHeights.Select(h => (h, $"{h}p")));
            stack.Children.Add(UI.Row("Maximum video height", UI.Picker(options, videoMax, v => { videoMax = v; Refresh(); })).Pad(0, 8));
        }
        stack.Children.Add(UI.Divider());

        var targetPanel = new StackPanel { Margin = new Thickness(20, 6, 0, 0), IsVisible = false };
        stack.Children.Add(UI.Check("Compress to target file size", false, v =>
        {
            useTarget = v;
            targetPanel.IsVisible = v;
            presetBlock.Opacity = v ? 0.45 : 1;
            presetBlock.IsEnabled = !v;
            Refresh();
        }));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var valueBox = new TextBox { Text = targetValue.ToString(CultureInfo.CurrentCulture), Width = 80, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 26 };
        valueBox.TextChanged += (_, _) => { if (double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) { targetValue = v; Refresh(); } };
        sizeRow.Children.Add(valueBox);
        sizeRow.Children.Add(UI.Picker([("KB", "KB"), ("MB", "MB"), ("GB", "GB")], unit, u => { unit = u; Refresh(); }).Pad(6, 0));
        var sizeLine = UI.Row(files.Count > 1 ? "Size of each file" : "Target file size", sizeRow);
        var reduceLine = UI.Row("Reduce by", UI.Slider(1, 99, reduction, v => { reduction = Math.Round(v); Refresh(); }, v => L10n.T($"{Math.Round(v)}%"), step: 1));
        reduceLine.IsVisible = false;
        targetPanel.Children.Add(UI.Row("Compression mode", UI.Picker([(true, "Target file size"), (false, "File size reduction")], true, v =>
        {
            bySize = v;
            sizeLine.IsVisible = v;
            reduceLine.IsVisible = !v;
            Refresh();
        })));
        targetPanel.Children.Add(sizeLine);
        targetPanel.Children.Add(reduceLine);
        stack.Children.Add(targetPanel);
        stack.Children.Add(message.Pad(0, 6));
        var estimateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        estimateRow.Children.Add(UI.Text("Expected size:", 12, color: Look.Muted));
        estimateRow.Children.Add(estimateText.Pad(6, 0));
        estimateRow.Children.Add(savedText);
        stack.Children.Add(estimateRow);

        window.Body = stack;
        window.Leading(UI.Button("Restore Defaults", () => { window.Close(); app.CompressPreset = CompressPreset.Balanced; app.CompressImageMaxPixels = 0; app.CompressVideoMaxHeight = 0; app.Save(); ToolWindows.Open("compress", files); }));
        compressButton.Click += () =>
        {
            app.CompressPreset = preset;
            if (kind == FileKind.Image) app.CompressImageMaxPixels = imageMax;
            if (kind == FileKind.Video) app.CompressVideoMaxHeight = videoMax;
            app.Save();
            var settings = Current();
            window.Close();
            Runner.Run("Compress", files, (f, c) => kind switch
            {
                FileKind.Video => Compression.Videos(f, settings, c),
                FileKind.Audio => Compression.Audio(f, settings, c),
                _ => Compression.Images(f, settings, c),
            });
        };
        window.Trailing(compressButton);
        Refresh();
        return window;
    }
}
