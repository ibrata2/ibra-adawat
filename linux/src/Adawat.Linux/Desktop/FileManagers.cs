using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>"Open with Adawat" in the right-click menu of GNOME Files (Scripts), Dolphin, Nemo and Caja. It runs
/// "adawat --wheel FILE…", which opens the wheel for those files, so the wheel works on every desktop, drag or not.</summary>
public static class FileManagers
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d ? d : Path.Combine(Home, ".local", "share");

    private static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");

    /// <summary>The command that starts this copy of Adawat: the AppImage when running from one.</summary>
    public static string Command => Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image ? image : Environment.ProcessPath ?? "adawat";

    public static void Install()
    {
        var title = L10n.T("Open with Adawat");
        var command = Command;
        var script = $"""
            #!/bin/sh
            # Opens the Adawat wheel for the selected files. Written by Adawat; it is rewritten when Adawat moves.
            exec "{command}" --wheel "$@"

            """;
        try
        {
            // GNOME Files and Caja list executable files in their scripts folders under Scripts in the menu.
            // Only file managers that are installed get an entry, so no stray folders appear.
            foreach (var (manager, folder) in new[] { ("nautilus", Path.Combine(DataHome, "nautilus", "scripts")), ("caja", Path.Combine(ConfigHome, "caja", "scripts")) })
            {
                if (ProcessRunner.Find(manager) == null) continue;
                Write(Path.Combine(folder, title), script, executable: true);
                // An older name from the other language is removed so only one entry shows.
                foreach (var other in new[] { "Open with Adawat", "فتح بأدوات" })
                    if (other != title && File.Exists(Path.Combine(folder, other)) && File.ReadAllText(Path.Combine(folder, other)).Contains("--wheel"))
                        File.Delete(Path.Combine(folder, other));
            }
            // Dolphin (KDE Plasma 6 reads kio/servicemenus; Plasma 5 read kservices5/ServiceMenus).
            var service = $"""
                [Desktop Entry]
                Type=Service
                MimeType=application/octet-stream;inode/directory;
                X-KDE-ServiceTypes=KonqPopupMenu/Plugin
                Actions=adawat

                [Desktop Action adawat]
                Name={title}
                Icon=adawat
                Exec="{command}" --wheel %F

                """;
            if (ProcessRunner.Find("dolphin") != null)
            {
                Write(Path.Combine(DataHome, "kio", "servicemenus", "adawat.desktop"), service, executable: true);
                Write(Path.Combine(DataHome, "kservices5", "ServiceMenus", "adawat.desktop"), service, executable: false);
            }
            // Nemo (Cinnamon).
            if (ProcessRunner.Find("nemo") != null) Write(Path.Combine(DataHome, "nemo", "actions", "adawat.nemo_action"), $"""
                [Nemo Action]
                Name={title}
                Icon-Name=adawat
                Exec="{command}" --wheel %F
                Selection=notnone
                Extensions=any;

                """, executable: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"file manager menus: {e.Message}"); }
    }

    private static void Write(string path, string content, bool executable)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (executable) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
}
