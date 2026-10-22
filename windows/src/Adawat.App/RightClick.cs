using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Adawat.Core;
using Microsoft.Win32;

namespace Adawat.App;

/// <summary>
/// "IBRA أدوات" in Explorer's right-click menu, for files and folders. Explorer starts IbraAdawat.exe --open "file" once per
/// selected item; the first copy runs the app, the others pass their file through a named pipe, and the app gathers what
/// arrives within a moment into one selection.
/// </summary>
public static partial class RightClick
{
    private const string PipeName = "IbraAdawat.Open";
    /// <summary>Sent instead of a file when the app is started again from its shortcut: show the main window.</summary>
    public const string ShowWindowSignal = "::show::";
    private const string Verb = "IbraAdawat";
    private const string Win11ClassicKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    // MARK: Registry

    /// <summary>Writes the menu entries for this user, pointing at this copy of the app (so they follow an update or a move).</summary>
    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "IbraAdawat.exe");
            foreach (var target in new[] { "*", "Directory" })
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{target}\shell\{Verb}");
                key.SetValue("MUIVerb", L10n.IsArabic ? "IBRA أدوات" : "IBRA Adawat");
                key.SetValue("Icon", $"\"{exe}\",0");
                key.SetValue("MultiSelectModel", "Player");
                key.SetValue("Position", "Top");
                using var command = key.CreateSubKey("command");
                command.SetValue("", $"\"{exe}\" --open \"%1\"");
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Write($"right-click register failed: {e.Message}");
        }
    }

    public static void Unregister()
    {
        foreach (var target in new[] { "*", "Directory" })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{target}\shell\{Verb}", false); } catch (Exception) { }
        }
    }

    /// <summary>Windows 11 hides older menu entries under "Show more options". With the classic menu on, everything,
    /// IBRA included, shows on the first right-click. A per-user setting; turning it off restores the Windows 11 menu.</summary>
    public static bool ClassicMenu
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Win11ClassicKey + @"\InprocServer32");
            return key != null;
        }
        set
        {
            if (value)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Win11ClassicKey + @"\InprocServer32");
                key.SetValue("", "");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(Win11ClassicKey, false);
            }
            RestartExplorer();
        }
    }

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    private static void RestartExplorer()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("explorer")) process.Kill();
            // Windows restarts the shell on its own; start it if it has not come back.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            };
            timer.Start();
        }
        catch (Exception e) { Log.Write($"explorer restart failed: {e.Message}"); }
    }

    // MARK: Passing files between copies

    /// <summary>The files after --open in a command line, or none.</summary>
    public static string[] FilesIn(string[] args)
    {
        var at = Array.IndexOf(args, "--open");
        return at < 0 ? [] : args.Skip(at + 1).Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
    }

    /// <summary>In a second copy: hands the files to the running app. Returns false when no app answered.</summary>
    public static bool Forward(string[] files)
    {
        AllowSetForegroundWindow(-1);
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(200);
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", files) + "\n");
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                return true;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Thread.Sleep(200);
            }
        }
        return false;
    }

    private static readonly List<string> Pending = [];
    private static DispatcherTimer? _gather;

    /// <summary>In the running app: listens for files from other copies and opens the menu for each burst.</summary>
    public static void Listen(Dispatcher dispatcher)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    var files = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (files.Contains(ShowWindowSignal)) dispatcher.BeginInvoke(() => HomeWindow.Present());
                    files = files.Where(f => f != ShowWindowSignal).ToArray();
                    if (files.Length > 0) dispatcher.BeginInvoke(() => Add(files));
                }
                catch (Exception e)
                {
                    Log.Write($"pipe: {e.Message}");
                    Thread.Sleep(300);
                }
            }
        }) { IsBackground = true, Name = "IBRA open pipe" };
        thread.Start();
    }

    /// <summary>Collects files arriving close together (one per selected item) and shows one menu for all of them.</summary>
    public static void Add(IEnumerable<string> files)
    {
        foreach (var file in files)
            if (!Pending.Contains(file, StringComparer.OrdinalIgnoreCase)) Pending.Add(file);
        if (_gather == null)
        {
            _gather = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _gather.Tick += (_, _) =>
            {
                _gather!.Stop();
                var selection = Pending.ToList();
                Pending.Clear();
                if (selection.Count > 0) FileMenuWindow.Open(selection);
            };
        }
        _gather.Stop();
        _gather.Start();
    }
}
