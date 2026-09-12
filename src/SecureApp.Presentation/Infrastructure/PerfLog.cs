using System.Diagnostics;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Lightweight, always-on perf tracing for the chat-open path (2026-09-12, the user's ask: objectivize
/// the jank with real measurement instead of guessing). Writes each entry to the debugger/logcat
/// (prefix "PERF ") and appends it to <c>perf.log</c> in the app data dir so it can be pulled off a
/// device with <c>adb</c>. Deliberately cheap: a formatted string, a Debug/Console write, and a locked
/// append. Not for production telemetry — a diagnostic that can be removed once the jank is settled.
/// </summary>
public static class PerfLog
{
    private static readonly object _lock = new();
    private static string? _path;

    /// <summary>Logs a single labelled event with the current wall-clock time.</summary>
    public static void Mark(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.WriteLine("PERF " + line);
        Console.WriteLine("PERF " + line);
        try
        {
            _path ??= Path.Combine(FileSystem.AppDataDirectory, "perf.log");
            lock (_lock) File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* logging must never break the app */ }
    }

    /// <summary>
    /// Measures how long the UI thread stays busy AFTER <paramref name="afterAction"/> runs — the real
    /// cost of realizing the CollectionView cells. We record a start, run the action (e.g. assign
    /// Messages), then post a marker to the dispatcher; the delay before that marker actually runs
    /// approximates the UI-thread stall caused by cell layout, which is the suspected jank source.
    /// </summary>
    public static void MeasureUiStall(string label, Action afterAction)
    {
        var sw = Stopwatch.StartNew();
        afterAction();
        var applied = sw.ElapsedMilliseconds;
        // The dispatcher won't run this until the UI thread finishes the layout work the action queued.
        MainThread.BeginInvokeOnMainThread(() =>
            Mark($"{label}: assign={applied}ms uiBusyAfter={sw.ElapsedMilliseconds - applied}ms total={sw.ElapsedMilliseconds}ms"));
    }
}
