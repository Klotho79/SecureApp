using System.Diagnostics;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Cold-start timing (2026-10-24): until now nothing measured how long the app takes from launch to a
/// usable screen, so any startup tuning would have been a guess. <see cref="Begin"/> is the first line of
/// <c>MauiProgram.CreateMauiApp</c>; each <see cref="Mark"/> logs the elapsed time since then as a metric
/// (once per name per process) together with the process age — an Android process that has been alive for
/// hours (the foreground service keeps it) shows up as a large age and is not a cold start.
/// </summary>
public static class AppStartup
{
    private static readonly Stopwatch Clock = new();
    private static readonly HashSet<string> Logged = [];

    public static void Begin() => Clock.Restart();

    public static void Mark(string name)
    {
        if (!Clock.IsRunning) return;
        lock (Logged)
        {
            if (!Logged.Add(name)) return;
        }

        long processAgeMs = -1;
        try { processAgeMs = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
        catch { /* not available everywhere — the elapsed time is still logged */ }

        AppLog.Metric("app.startup." + name, Clock.Elapsed.TotalMilliseconds, "ms", ("processAgeMs", processAgeMs));
    }
}
