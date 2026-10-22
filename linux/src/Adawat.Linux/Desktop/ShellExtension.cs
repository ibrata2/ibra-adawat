using Adawat.Core;

namespace Adawat.Linux.Desktop;

/// <summary>Puts the Adawat GNOME Shell extension in place and turns it on. GNOME on Wayland loads a newly installed
/// extension only after the next sign-in, which the welcome guide and Settings mention.</summary>
public static class ShellExtension
{
    public const string Uuid = "adawat@motalim.github.io";

    public static bool Needed => Core.Desktop.IsWayland && Core.Desktop.IsGnome;

    private static string Bundled => Path.Combine(AppContext.BaseDirectory, "gnome-extension", Uuid);

    private static string UserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "gnome-shell", "extensions", Uuid);

    private static string SystemFolder => Path.Combine("/usr/share/gnome-shell/extensions", Uuid);

    public enum State { NotNeeded, Active, NeedsSignIn, Missing }

    public static State Current
    {
        get
        {
            if (!Needed) return State.NotNeeded;
            var info = Run("info", Uuid);
            if (info == null) return Directory.Exists(UserFolder) || Directory.Exists(SystemFolder) ? State.NeedsSignIn : State.Missing;
            return info.Contains("ACTIVE") || info.Contains("ENABLED") && !info.Contains("INITIALIZED") ? State.Active : State.NeedsSignIn;
        }
    }

    /// <summary>Copies the extension for this user when no package installed it system-wide, and enables it.</summary>
    public static void EnsureInstalled()
    {
        if (!Needed) return;
        try
        {
            // A copy made for the user before the package installed one would hide later versions of it.
            if (Directory.Exists(SystemFolder) && Directory.Exists(UserFolder)) Directory.Delete(UserFolder, recursive: true);
            if (!Directory.Exists(SystemFolder) && Directory.Exists(Bundled))
            {
                var bundledMeta = File.ReadAllText(Path.Combine(Bundled, "metadata.json"));
                var installedMeta = Path.Combine(UserFolder, "metadata.json");
                if (!File.Exists(installedMeta) || File.ReadAllText(installedMeta) != bundledMeta)
                {
                    Directory.CreateDirectory(UserFolder);
                    foreach (var file in Directory.GetFiles(Bundled)) File.Copy(file, Path.Combine(UserFolder, Path.GetFileName(file)), true);
                }
            }
            Run("enable", Uuid);
            Log.Write($"GNOME Shell extension: {Current}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write($"GNOME Shell extension: {e.Message}"); }
    }

    private static string? Run(params string[] arguments)
    {
        if (ProcessRunner.Find("gnome-extensions") is not { } tool) return null;
        // Off the UI thread: awaiting there while it is blocked would never finish.
        var result = Task.Run(() => ProcessRunner.Run(tool, arguments)).GetAwaiter().GetResult();
        return result.ExitCode == 0 ? result.Output : null;
    }
}
