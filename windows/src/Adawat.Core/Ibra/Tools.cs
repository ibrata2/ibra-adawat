using System.Diagnostics;
using System.Text;

namespace Adawat.Core.Ibra;

/// <summary>Downloads (speech and image models) and running bundled helper programs with live output.</summary>
public static class IbraTools
{
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IBRA-Adawat/1.0");
        return client;
    }

    public static HttpClient Client => Http;

    /// <summary>Folder for downloaded models: %APPDATA%\IbraAdawat\models.</summary>
    public static string Models
    {
        get
        {
            var folder = Path.Combine(Settings.Folder, "models");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Downloads <paramref name="url"/> to <paramref name="destination"/> through a .part file, so a broken download is never mistaken for a finished one.</summary>
    public static async Task Fetch(string url, string destination, Action<double>? progress, CancellationToken cancellation)
    {
        var part = destination + ".part";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode) throw new AdawatException($"Download failed ({(int)response.StatusCode}). Check the internet connection.");
            var total = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var target = File.Create(part))
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    if (total > 0) progress?.Invoke((double)done / total);
                }
            }
            File.Move(part, destination, true);
        }
        catch (HttpRequestException)
        {
            TryDelete(part);
            throw new AdawatException("Download failed. Check the internet connection.");
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public sealed record RunResult(int ExitCode, string Output, string Error);

    /// <summary>Runs a tool and passes every line it prints (standard output and error) to <paramref name="onLine"/>.</summary>
    public static async Task<RunResult> Run(string tool, IEnumerable<string> arguments, Action<string>? onLine, CancellationToken cancellation,
                                            string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(tool) ?? "",
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (output) output.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (error) error.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
        process.WaitForExit();
        return new RunResult(process.ExitCode, output.ToString(), error.ToString());
    }

    /// <summary>The last meaningful line a failed tool printed, for the error toast.</summary>
    public static string LastLine(RunResult result) =>
        (result.Error + "\n" + result.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0) ?? $"exit code {result.ExitCode}";

    /// <summary>A path written for ffmpeg's filter syntax: forward slashes, the drive colon escaped, inside single quotes.</summary>
    public static string FilterPath(string path) =>
        "'" + Path.GetFullPath(path).Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'") + "'";
}
