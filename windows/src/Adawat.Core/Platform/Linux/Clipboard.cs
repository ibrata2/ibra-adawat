using System.Diagnostics;

namespace Adawat.Core;

/// <summary>What the clipboard needs from the app on Linux: the Avalonia app owns the clipboard while it runs.</summary>
public interface IClipboardHost
{
    void SetText(string text);
    void SetFiles(IReadOnlyList<string> paths);
    void SetPng(byte[] png);
    string? GetText();
}

/// <summary>The clipboard. The app sets <see cref="Host"/>; without it (the command line), wl-copy on Wayland or xclip on X11.</summary>
public static class Clipboard
{
    public static IClipboardHost? Host { get; set; }

    public static void CopyText(string text)
    {
        if (Host != null) { Host.SetText(text); return; }
        Put("text/plain;charset=utf-8", System.Text.Encoding.UTF8.GetBytes(text));
    }

    public static void CopyFiles(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (Host != null) { Host.SetFiles(list); return; }
        var uris = list.Select(path => new Uri(path).AbsoluteUri).ToList();
        // GNOME Files pastes its own list; other file managers read a plain URI list.
        if (Desktop.IsGnome) Put("x-special/gnome-copied-files", System.Text.Encoding.UTF8.GetBytes("copy\n" + string.Join("\n", uris)));
        else Put("text/uri-list", System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
    }

    public static void CopyPng(byte[] png)
    {
        if (Host != null) { Host.SetPng(png); return; }
        Put("image/png", png);
    }

    public static string? Text()
    {
        if (Host != null) return Host.GetText();
        var tool = Desktop.IsWayland ? ProcessRunner.Find("wl-paste") : ProcessRunner.Find("xclip");
        if (tool == null) return null;
        string[] arguments = Desktop.IsWayland ? ["--no-newline"] : ["-selection", "clipboard", "-o"];
        var result = Task.Run(() => ProcessRunner.Run(tool, arguments)).GetAwaiter().GetResult();
        return result.ExitCode == 0 ? result.Output : null;
    }

    /// <summary>wl-copy and xclip stay running to serve the clipboard, so they are started and left, not awaited. With no
    /// display (a terminal over SSH) there is no clipboard, and nothing is copied.</summary>
    private static void Put(string type, byte[] data)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return;
        var wayland = Desktop.IsWayland && ProcessRunner.Find("wl-copy") != null;
        var tool = (wayland ? ProcessRunner.Find("wl-copy") : ProcessRunner.Find("xclip"))
            ?? throw new AdawatException($"Copying needs xclip or wl-clipboard. Install it with: {Desktop.Install("wl-clipboard xclip", "wl-clipboard xclip")}");
        // Its own output pipes: inherited ones would stay open as long as it serves the clipboard.
        var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in wayland ? ["--type", type] : new[] { "-selection", "clipboard", "-t", type, "-i" }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        process.StandardInput.BaseStream.Write(data);
        process.StandardInput.Close();
    }
}
