using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Durable, on-device, structured logging (2026-09-13, the user's explicit requirement: an error log
/// AND a metrics log must exist, because tuning without them is just random code changes). Writes
/// plain-text, line-oriented, timestamped entries to rotating files under
/// <c>&lt;AppData&gt;/logs/</c> — <c>errors.log</c> and <c>metrics.log</c> — so they survive restarts,
/// can be shown in-app (Settings → Diagnostika) and pulled with <c>adb</c>. Deliberately a static,
/// dependency-free, best-effort sink: logging must never throw or slow the app, and it must work from
/// anywhere (global exception hook, view models, page code-behind) without DI plumbing. The existing
/// relay-backed <c>IDiagnosticsReporter</c> stays for opt-in centralized reporting; this is the local
/// source of truth that also works offline.
/// </summary>
public static class AppLog
{
    private const long MaxBytesPerFile = 512 * 1024; // rotate at ~512 KB, keep one .1 backup
    private static readonly object _errorLock = new();
    private static readonly object _metricLock = new();
    private static string? _dir;

    private static string Dir => _dir ??= EnsureDir();

    private static string EnsureDir()
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, "logs");
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }

    /// <summary>Records an error/exception. Safe to call from catch blocks and the global exception hook.</summary>
    public static void Error(string source, string message, Exception? exception = null)
    {
        var sb = new StringBuilder();
        sb.Append(Timestamp()).Append('\t').Append("ERROR").Append('\t')
          .Append(Sanitize(source)).Append('\t').Append(Sanitize(message));
        if (exception is not null)
            sb.Append('\t').Append(Sanitize(exception.GetType().Name)).Append(": ")
              .Append(Sanitize(exception.Message)).Append(" | ").Append(Sanitize(exception.StackTrace ?? ""));
        Write("errors.log", _errorLock, sb.ToString());
        Debug.WriteLine("APPLOG-ERROR " + sb);
    }

    /// <summary>Records a numeric metric (typically a duration in ms) with optional tags — the data tuning relies on.</summary>
    public static void Metric(string name, double value, string unit = "ms", params (string Key, object? Value)[] tags)
    {
        var sb = new StringBuilder();
        sb.Append(Timestamp()).Append('\t').Append(Sanitize(name)).Append('\t')
          .Append(value.ToString("0.#", CultureInfo.InvariantCulture)).Append(Sanitize(unit));
        foreach (var (key, val) in tags)
            sb.Append('\t').Append(Sanitize(key)).Append('=').Append(Sanitize(val?.ToString() ?? ""));
        Write("metrics.log", _metricLock, sb.ToString());
        Debug.WriteLine("APPLOG-METRIC " + sb);
    }

    /// <summary>Records a discrete named event with tags (no numeric value) — e.g. a lifecycle milestone.</summary>
    public static void Event(string name, params (string Key, object? Value)[] tags)
        => Metric(name, 0, "", tags);

    /// <summary>Times <paramref name="action"/> and logs its duration as a metric. Returns the elapsed ms.</summary>
    public static double Time(string name, Action action, params (string Key, object? Value)[] tags)
    {
        var sw = Stopwatch.StartNew();
        try { action(); }
        finally { }
        var ms = sw.Elapsed.TotalMilliseconds;
        Metric(name, ms, "ms", tags);
        return ms;
    }

    /// <summary>Returns the tail of one log ("errors" or "metrics") for the in-app viewer / export, newest last.</summary>
    public static string ReadRecent(string which, int maxBytes = 64 * 1024)
    {
        var name = which.Equals("errors", StringComparison.OrdinalIgnoreCase) ? "errors.log" : "metrics.log";
        var path = Path.Combine(Dir, name);
        try
        {
            if (!File.Exists(path)) return "";
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length <= maxBytes) return Encoding.UTF8.GetString(bytes);
            return "…\n" + Encoding.UTF8.GetString(bytes, bytes.Length - maxBytes, maxBytes);
        }
        catch (Exception ex) { return "(nelze přečíst log: " + ex.Message + ")"; }
    }

    /// <summary>Absolute path of a log file, for export/share.</summary>
    public static string PathOf(string which)
        => Path.Combine(Dir, which.Equals("errors", StringComparison.OrdinalIgnoreCase) ? "errors.log" : "metrics.log");

    private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    // Tabs are the field separator and newlines the record separator, so strip both from values.
    private static string Sanitize(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static void Write(string fileName, object gate, string line)
    {
        try
        {
            var path = Path.Combine(Dir, fileName);
            lock (gate)
            {
                Rotate(path);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* logging must never break or slow the app */ }
    }

    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytesPerFile) return;
            var backup = path + ".1";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(path, backup);
        }
        catch { /* best-effort */ }
    }
}
