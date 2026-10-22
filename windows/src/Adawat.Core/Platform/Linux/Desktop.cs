using System.Diagnostics;

namespace Adawat.Core;

/// <summary>Which Linux desktop this is, and the freedesktop.org ways to reach its folders, file manager and terminal.</summary>
public static class Desktop
{
    public static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                                    || Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";

    /// <summary>"GNOME", "KDE", "XFCE"… as the session reports it (ubuntu:GNOME on Ubuntu).</summary>
    public static string Name => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";

    public static bool IsGnome => Name.Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    public static bool IsKde => Name.Contains("KDE", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string?> Folders = new();

    /// <summary>An XDG user folder (DESKTOP, DOWNLOAD…), which has a translated name on an Arabic system.</summary>
    public static string UserFolder(string name, string fallback)
    {
        lock (Folders)
        {
            if (!Folders.TryGetValue(name, out var folder))
            {
                folder = Read(name);
                Folders[name] = folder;
            }
            return folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
        }
    }

    private static string? Read(string name)
    {
        // ~/.config/user-dirs.dirs holds lines like XDG_DOWNLOAD_DIR="$HOME/التنزيلات".
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config");
            var file = Path.Combine(config, "user-dirs.dirs");
            if (!File.Exists(file)) return null;
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.StartsWith($"XDG_{name}_DIR=")) continue;
                var value = line[(line.IndexOf('=') + 1)..].Trim().Trim('"').Replace("$HOME", home);
                return Directory.Exists(value) && value.TrimEnd('/') != home.TrimEnd('/') ? value : null;
            }
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>The command that installs a program with this distribution's package manager. Package names differ:
    /// <paramref name="apt"/> for Debian and Ubuntu, <paramref name="dnf"/> for Fedora, and so on (null means the same as apt).</summary>
    public static string Install(string apt, string? dnf = null, string? zypper = null, string? pacman = null)
    {
        if (ProcessRunner.Find("apt") != null) return $"sudo apt install {apt}";
        if (ProcessRunner.Find("dnf") != null) return $"sudo dnf install {dnf ?? apt}";
        if (ProcessRunner.Find("zypper") != null) return $"sudo zypper install {zypper ?? dnf ?? apt}";
        if (ProcessRunner.Find("pacman") != null) return $"sudo pacman -S {pacman ?? apt}";
        return apt;
    }

    /// <summary>Opens the file manager with <paramref name="path"/> selected, through the freedesktop FileManager1 interface.</summary>
    public static void ShowInFileManager(string path)
    {
        var uri = new Uri(path).AbsoluteUri;
        if (ProcessRunner.Find("gdbus") is { } gdbus)
        {
            var info = new ProcessStartInfo(gdbus) { UseShellExecute = false };
            foreach (var argument in new[] { "call", "--session", "--dest", "org.freedesktop.FileManager1", "--object-path", "/org/freedesktop/FileManager1",
                                             "--method", "org.freedesktop.FileManager1.ShowItems", $"['{uri.Replace("'", "%27")}']", "" })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            if (process.WaitForExit(3000) && process.ExitCode == 0) return;
        }
        Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
    }

    /// <summary>Opens a file or folder in its default app.</summary>
    public static void Open(string path)
    {
        var info = new ProcessStartInfo(ProcessRunner.Find("xdg-open") ?? "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(path);
        Process.Start(info)?.Dispose();
    }

    /// <summary>The desktop's own terminal at <paramref name="folder"/>.</summary>
    public static void OpenTerminal(string folder)
    {
        (string Tool, string[] Arguments)[] terminals =
        [
            ("kgx", ["--working-directory", folder]),
            ("gnome-terminal", ["--working-directory", folder]),
            ("ptyxis", ["--working-directory", folder]),
            ("konsole", ["--workdir", folder]),
            ("xfce4-terminal", ["--working-directory", folder]),
            ("mate-terminal", ["--working-directory", folder]),
            ("tilix", ["--working-directory", folder]),
            ("alacritty", ["--working-directory", folder]),
            ("kitty", ["--directory", folder]),
            ("x-terminal-emulator", []),
            ("xterm", []),
        ];
        // The desktop's own terminal first: Console on GNOME 46+, Konsole on KDE.
        var preferred = IsKde ? terminals.OrderBy(t => t.Tool != "konsole") : terminals.AsEnumerable();
        foreach (var (tool, arguments) in preferred)
        {
            if (ProcessRunner.Find(tool) is not { } path) continue;
            var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = folder };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            Process.Start(info)?.Dispose();
            return;
        }
        throw new AdawatException("No terminal app was found.");
    }
}
