using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Adawat.Core;

namespace Adawat.Linux;

/// <summary>Linux has no system Share sheet, so Share offers what this computer can do: attach to an email, send over
/// Bluetooth, send to a phone with KDE Connect, or copy the files.</summary>
public static class ShareWindow
{
    public static void Present(IReadOnlyList<string> files)
    {
        var window = new ToolWindow("Share", 380);
        var stack = new StackPanel { Spacing = 8 };
        var name = files.Count == 1 ? Path.GetFileName(files[0]) : L10n.T($"{files.Count} files");
        stack.Children.Add(UI.Text(name, 13, FontWeight.SemiBold).Also(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        void Option(string icon, string title, string detail, Action action)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(UI.Icon(icon, 16, Look.Accent));
            var text = new StackPanel();
            text.Children.Add(UI.Text(title, 13, FontWeight.Medium));
            text.Children.Add(UI.Text(detail, 11, color: Look.Muted));
            row.Children.Add(text);
            var button = new SoftButton(false, round: false) { Child = row, Padding = new Avalonia.Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += () => { window.Close(); action(); };
            stack.Children.Add(button);
        }
        var files2 = files.Where(File.Exists).ToList();
        if (ProcessRunner.Find("xdg-email") is { } email && files2.Count > 0)
            Option("", "Email", "Attach to a new message", () => Start(email, files2.SelectMany(f => new[] { "--attach", f })));
        if (ProcessRunner.Find("bluetooth-sendto") is { } bluetooth && files2.Count > 0)
            Option("", "Bluetooth", "Send to a nearby device", () => Start(bluetooth, files2));
        if (ProcessRunner.Find("kdeconnect-cli") is { } kde && files2.Count > 0)
            Option("", "KDE Connect", "Send to your phone", () =>
            {
                var devices = Task.Run(() => ProcessRunner.Run(kde, ["--list-available", "--id-only"])).GetAwaiter().GetResult().Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (devices.Length == 0) { Hud.Shared.ShowMessage("No phone is connected to KDE Connect", Hud.Style.Failure); return; }
                foreach (var file in files2) Start(kde, ["-d", devices[0].Trim(), "--share", file]);
                Hud.Shared.ShowMessage("Sent to your phone", Hud.Style.Success);
            });
        Option("", "Copy", "Paste the files in any app", () =>
        {
            Core.Clipboard.CopyFiles(files);
            Hud.Shared.ShowMessage(files.Count == 1 ? "File copied" : $"{files.Count} files copied", Hud.Style.Success);
        });
        window.Body = stack;
        window.Trailing(UI.Button("Cancel", window.Close));
        window.Present();
    }

    private static void Start(string tool, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(tool) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Process.Start(info)?.Dispose();
    }
}
