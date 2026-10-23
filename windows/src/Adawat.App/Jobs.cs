using System.Collections.ObjectModel;
using System.Windows;
using Adawat.Core;

namespace Adawat.App;

public enum JobState { Waiting, Running, Done, Failed, Stopped }

/// <summary>One task in the main window's list: an operation on one or more files, with its progress and results.</summary>
public sealed class Job
{
    public AdawatOperation Operation { get; }
    public IReadOnlyList<string> Files { get; }
    public JobState State { get; internal set; } = JobState.Waiting;
    public double Progress { get; internal set; }
    public string Status { get; internal set; } = "";
    public IReadOnlyList<string> Outputs { get; internal set; } = [];
    /// <summary>Runs as soon as it is added (a tool window's Run, the right-click menu), not when the queue reaches it.</summary>
    public bool Immediate { get; init; }
    internal CancellationTokenSource Cancel { get; set; } = new();

    public event Action? Changed;

    public Job(AdawatOperation operation, IReadOnlyList<string> files)
    {
        Operation = operation;
        Files = files;
    }

    public string SourceText => Files.Count switch
    {
        0 => "—",
        1 => Path.GetFileName(Files[0].TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Files[0],
        _ => L10n.T($"{Files.Count} files"),
    };

    public long Size
    {
        get
        {
            long total = 0;
            foreach (var file in Files) try { if (File.Exists(file)) total += new FileInfo(file).Length; } catch (IOException) { }
            return total;
        }
    }

    internal void Notify() => Changed?.Invoke();
}

/// <summary>
/// Every run goes through here, from the main window, a tool window or the right-click menu, so each one shows in the main
/// window's list. Queued tasks run one after another; immediate ones start at once.
/// </summary>
public static class Jobs
{
    public static ObservableCollection<Job> All { get; } = [];

    private static bool _pumping;
    private static bool _stopped;

    public static event Action? QueueChanged;

    public static Job Add(AdawatOperation operation, IReadOnlyList<string> files, bool immediate)
    {
        var job = new Job(operation, files) { Immediate = immediate };
        job.Status = L10n.T(immediate ? "Starting…" : "Waiting");
        All.Add(job);
        QueueChanged?.Invoke();
        if (immediate) _ = Execute(job);
        else if (Settings.Current.AutoStart) Start();
        return job;
    }

    public static bool AnyWaiting => All.Any(j => j.State == JobState.Waiting && !j.Immediate);
    public static bool AnyRunning => All.Any(j => j.State == JobState.Running);

    /// <summary>Runs waiting tasks one by one until none are left or Stop is pressed.</summary>
    public static async void Start()
    {
        _stopped = false;
        if (_pumping) return;
        _pumping = true;
        QueueChanged?.Invoke();
        try
        {
            while (!_stopped && All.FirstOrDefault(j => j.State == JobState.Waiting && !j.Immediate) is { } next)
                await Execute(next);
        }
        finally
        {
            _pumping = false;
            QueueChanged?.Invoke();
        }
    }

    /// <summary>Cancels what is running and keeps the rest waiting.</summary>
    public static void Stop()
    {
        _stopped = true;
        foreach (var job in All.Where(j => j.State == JobState.Running)) job.Cancel.Cancel();
        QueueChanged?.Invoke();
    }

    public static void Remove(Job job)
    {
        if (job.State == JobState.Running) job.Cancel.Cancel();
        All.Remove(job);
        QueueChanged?.Invoke();
    }

    public static void ClearFinished()
    {
        foreach (var job in All.Where(j => j.State != JobState.Running && j.State != JobState.Waiting).ToList()) All.Remove(job);
        QueueChanged?.Invoke();
    }

    public static void ClearAll()
    {
        foreach (var job in All.ToList()) Remove(job);
    }

    public static void Retry(Job job)
    {
        if (job.State is JobState.Running or JobState.Waiting) return;
        var copy = new Job(job.Operation, job.Files) { Status = L10n.T("Waiting") };
        var at = All.IndexOf(job);
        All[at] = copy;
        QueueChanged?.Invoke();
        Start();
    }

    private static async Task Execute(Job job)
    {
        var hud = Hud.Shared;
        var dispatcher = Application.Current.Dispatcher;
        var quiet = HomeWindow.IsShowing;
        job.State = JobState.Running;
        job.Progress = 0;
        job.Status = L10n.T("Working…");
        job.Notify();
        QueueChanged?.Invoke();
        if (!quiet) hud.ShowMessage($"{job.Operation.Title}…", Hud.Style.Working);
        var context = new OperationContext(
            text => dispatcher.BeginInvoke(() =>
            {
                job.Status = text;
                job.Notify();
                if (!HomeWindow.IsShowing) hud.Update(text);
            }),
            fraction => dispatcher.BeginInvoke(() =>
            {
                job.Progress = fraction;
                job.Notify();
                if (!HomeWindow.IsShowing) hud.SetProgress(fraction);
            }),
            job.Cancel.Token);
        try
        {
            var outputs = await Task.Run(() => job.Operation.Run(job.Files, context), job.Cancel.Token);
            job.Outputs = outputs;
            job.State = JobState.Done;
            job.Progress = 1;
            job.Status = context.Message ?? outputs.Count switch
            {
                0 => L10n.T("Done"),
                1 => Path.GetFileName(outputs[0]),
                _ => L10n.T($"Saved {outputs.Count} files"),
            };
            if (!HomeWindow.IsShowing) hud.ShowMessage(context.Message ?? (outputs.Count == 1 ? $"Saved {Path.GetFileName(outputs[0])}" : outputs.Count == 0 ? $"{job.Operation.Title} done" : $"Saved {outputs.Count} files"), Hud.Style.Success);
            Sounds.Done();
            if (job.Operation.ProducesFiles && outputs.Count > 0 && Settings.Current.ShowResultsInFloat) Floats.ShowResults(outputs);
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Stopped;
            job.Status = L10n.T("Stopped");
            if (!HomeWindow.IsShowing) hud.ShowMessage(L10n.T("Stopped"), Hud.Style.Failure);
        }
        catch (Exception e)
        {
            Log.Write($"{job.Operation.Id} failed: {e}");
            job.State = JobState.Failed;
            job.Status = e is AdawatException ? e.Message : L10n.T("Something went wrong: ") + e.Message;
            if (!HomeWindow.IsShowing) hud.ShowMessage(job.Status, Hud.Style.Failure);
            Sounds.Failed();
        }
        job.Notify();
        QueueChanged?.Invoke();
    }
}
