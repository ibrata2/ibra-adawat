using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Adawat.Core;

namespace Adawat.App;

/// <summary>
/// Updates from the GitHub releases of ibrata2/ibra-adawat: compares the latest release's version with this copy, downloads
/// IBMC-Setup-x64.exe and runs it quietly; the installer closes IBMC, installs over it and starts it again.
/// </summary>
public static class Updater
{
    private const string LatestUrl = "https://api.github.com/repos/ibrata2/ibra-adawat/releases/latest";
    private const string AssetName = "IBMC-Setup-x64.exe";

    public sealed record Release(Version Version, string Tag, string Notes, string DownloadUrl, long Size);

    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    private static string T(string english, string arabic) => L10n.IsArabic ? arabic : english;

    /// <summary>The latest release, or null when there is none newer than this copy. Throws when GitHub can't be reached.</summary>
    public static async Task<Release?> FindNewer(CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Core.Ibra.IbraTools.Client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= Current) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            return new Release(version, tag, root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                               asset.GetProperty("browser_download_url").GetString() ?? "", asset.GetProperty("size").GetInt64());
        }
        return null;
    }

    /// <summary>At start: a quiet check a little after launch; a window only when there is something new not skipped before.</summary>
    public static void CheckOnStart()
    {
        if (!Settings.Current.AutoCheckUpdates) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try
            {
                var release = await FindNewer();
                if (release != null && release.Tag != Settings.Current.SkippedUpdate) Offer(release, automatic: true);
            }
            catch (Exception e) { Log.Write($"update check: {e.Message}"); }
        };
        timer.Start();
    }

    /// <summary>From the Settings button: always answers, even when up to date or offline.</summary>
    public static async Task CheckNow(Action<string> status)
    {
        status(T("Checking…", "جاري البحث…"));
        try
        {
            var release = await FindNewer();
            if (release == null) { status(T($"You have the latest version ({CurrentText})", $"عندك آخر نسخة ({CurrentText})")); return; }
            status(T($"Version {release.Version} is available", $"نسخة {release.Version} متوفرة"));
            Offer(release, automatic: false);
        }
        catch (Exception e)
        {
            Log.Write($"update check: {e.Message}");
            status(T("Couldn't reach GitHub. Check the internet connection.", "ما گدرت أوصل لـ GitHub. تأكد من الإنترنت."));
        }
    }

    private static ToolWindow? _offer;

    private static void Offer(Release release, bool automatic)
    {
        if (_offer != null) { _offer.Activate(); return; }
        var window = new ToolWindow(T("Update available", "يوجد تحديث"), 440);
        var stack = new StackPanel();
        stack.Children.Add(UI.Text(T($"IBMC {release.Version} is available", $"نسخة IBMC {release.Version} متوفرة"), 15, FontWeights.SemiBold));
        stack.Children.Add(UI.Caption(T($"You have {CurrentText}. Download size {Format.Bytes(release.Size)}.",
                                        $"نسختك {CurrentText}. حجم التحميل {Format.Bytes(release.Size)}.")).Pad(0, 4));
        var progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 12, 0, 4), Foreground = Theme.Accent, Background = Theme.ButtonFill, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed };
        var state = UI.Caption("");
        stack.Children.Add(progress);
        stack.Children.Add(state);
        stack.Children.Add(UI.Caption(T("IBMC closes while it updates and opens again when done.", "البرنامج يتسكّر أثناء التحديث ويرجع يفتح وحده.")).Pad(0, 8));
        window.Body = stack;
        var cancel = new CancellationTokenSource();
        Button? update = null;
        var later = UI.Button(T("Later", "لاحقاً"), window.Close);
        if (automatic)
        {
            window.Leading(UI.Button(T("Skip this version", "تخطي هذه النسخة"), () =>
            {
                Settings.Current.SkippedUpdate = release.Tag;
                Settings.Current.Save();
                window.Close();
            }));
        }
        update = UI.Button(T("Update Now", "تحديث الآن"), async () =>
        {
            update!.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            try
            {
                var installer = await Download(release, f => window.Dispatcher.BeginInvoke(() =>
                {
                    progress.Value = f;
                    state.Text = T($"Downloading… {Math.Round(f * 100)}%", $"جاري التحميل… {Math.Round(f * 100)}%");
                }), cancel.Token);
                state.Text = T("Installing…", "جاري التثبيت…");
                Process.Start(new ProcessStartInfo(installer, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
                HomeWindow.Quit();
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Write($"update download: {e}");
                state.Text = T("The download failed. Try again later.", "فشل التحميل. جرّب بعدين.");
                update.IsEnabled = true;
            }
        }, primary: true, icon: "");
        window.Trailing(later, update);
        window.Closed += (_, _) => { cancel.Cancel(); _offer = null; };
        _offer = window;
        window.Present();
    }

    private static async Task<string> Download(Release release, Action<double> progress, CancellationToken cancellation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "IBMC-update");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"IBMC-Setup-{release.Version}.exe");
        if (File.Exists(path) && new FileInfo(path).Length == release.Size) return path;
        await Core.Ibra.IbraTools.Fetch(release.DownloadUrl, path, progress, cancellation);
        if (release.Size > 0 && new FileInfo(path).Length != release.Size)
        {
            Core.Ibra.IbraTools.TryDelete(path);
            throw new IOException("Incomplete download");
        }
        return path;
    }
}
