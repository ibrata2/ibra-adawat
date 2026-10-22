using System.Diagnostics;

namespace Adawat.Core;

/// <summary>The six actions. Copy and Copy Path work anywhere; Share, Float and Move need the app's windows, which the app
/// provides through the hooks below.</summary>
public static class FileActions
{
    public static Func<IReadOnlyList<string>, Task>? ShareHandler { get; set; }
    public static Func<IReadOnlyList<string>, Task>? FloatHandler { get; set; }
    /// <summary>Asks for a destination folder; null when the user cancels.</summary>
    public static Func<IReadOnlyList<string>, Task<string?>>? ChooseFolder { get; set; }

    public static async Task<IReadOnlyList<string>> Share(IReadOnlyList<string> files, OperationContext context)
    {
        if (ShareHandler == null) throw new AdawatException("This tool needs its window.");
        await ShareHandler(files);
        context.Finish("Sharing");
        return [];
    }

    public static async Task<IReadOnlyList<string>> NewFloat(IReadOnlyList<string> files, OperationContext context)
    {
        if (FloatHandler == null) throw new AdawatException("This tool needs its window.");
        await FloatHandler(files);
        context.Finish("Added to a new Float");
        return [];
    }

    public static Task<IReadOnlyList<string>> CopyFiles(IReadOnlyList<string> files, OperationContext context)
    {
        Clipboard.CopyFiles(files);
        context.Finish(files.Count == 1 ? "File copied" : $"{files.Count} files copied");
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public static Task<IReadOnlyList<string>> CopyPaths(IReadOnlyList<string> files, OperationContext context)
    {
        Clipboard.CopyText(string.Join(Environment.NewLine, files));
        context.Finish(files.Count == 1 ? "Path copied" : $"{files.Count} paths copied");
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public static async Task<IReadOnlyList<string>> Move(IReadOnlyList<string> files, OperationContext context)
    {
        if (ChooseFolder == null) throw new AdawatException("This tool needs its window.");
        var folder = await ChooseFolder(files);
        if (folder == null)
        {
            context.Finish("Move cancelled");
            return [];
        }
        var moved = new List<string>();
        foreach (var file in files)
        {
            var target = Output.Unique(Path.Combine(folder, Path.GetFileName(file.TrimEnd('\\', '/'))));
            if (Directory.Exists(file)) Directory.Move(file, target); else File.Move(file, target);
            moved.Add(target);
        }
        context.Finish(files.Count == 1 ? $"Moved 1 item to {Path.GetFileName(folder)}" : $"Moved {files.Count} items to {Path.GetFileName(folder)}");
        return moved;
    }

    /// <summary>Windows Terminal at the folder (or the file's folder), falling back to PowerShell; the desktop's terminal on Linux.</summary>
    public static Task<IReadOnlyList<string>> OpenTerminal(IReadOnlyList<string> files, OperationContext context)
    {
        var first = files[0];
        var folder = Directory.Exists(first) ? first : Path.GetDirectoryName(first) ?? first;
#if !WINDOWS
        Desktop.OpenTerminal(folder);
        context.Finish($"Opened Terminal in {Path.GetFileName(folder)}");
        return Task.FromResult<IReadOnlyList<string>>([]);
#else
        var terminal = ProcessRunner.Find("wt");
        var info = terminal != null
            ? new ProcessStartInfo(terminal) { ArgumentList = { "-d", folder }, UseShellExecute = false }
            : new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoExit", "-Command", $"Set-Location -LiteralPath '{folder.Replace("'", "''")}'" }, UseShellExecute = true };
        Process.Start(info);
        context.Finish($"Opened Terminal in {Path.GetFileName(folder)}");
        return Task.FromResult<IReadOnlyList<string>>([]);
#endif
    }

#if WINDOWS
    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
#else
    /// <summary>The file selected in the desktop's file manager.</summary>
    public static void ShowInExplorer(string path) => Desktop.ShowInFileManager(path);
#endif
}
