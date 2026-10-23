using System.Windows;
using Adawat.Core;

namespace Adawat.App;

/// <summary>Runs a wheel choice or a tool window's job with the same toast, sound and Float.</summary>
public static class Runner
{
    /// <summary>A drop on the wheel: tools with options open their window, everything else runs right away.</summary>
    public static void Start(AdawatOperation operation, IReadOnlyList<string> files)
    {
        if (operation.Window is { } window && ToolWindows.Open(window, files)) return;
        Run(operation, files);
    }

    public static void Run(string title, IReadOnlyList<string> files, OperationWork work, bool producesFiles = true) =>
        Run(new AdawatOperation("window", title, "", work) { ProducesFiles = producesFiles }, files);

    /// <summary>Runs right away and shows as a task in the main window's list.</summary>
    public static void Run(AdawatOperation operation, IReadOnlyList<string> files) => Jobs.Add(operation, files, immediate: true);

    /// <summary>From the main window: tools with options open their window; the rest join the queue.</summary>
    public static void Queue(AdawatOperation operation, IReadOnlyList<string> files)
    {
        if (operation.Window is { } window && ToolWindows.Open(window, files)) return;
        Jobs.Add(operation, files, immediate: false);
    }
}

/// <summary>A small log in %APPDATA%\Adawat\adawat.log, for bug reports.</summary>
public static class Log
{
    private static readonly string FilePath = Path.Combine(Settings.Folder, "adawat.log");
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Folder);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
