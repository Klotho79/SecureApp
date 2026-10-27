using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using SecureApp.Data;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Workplace;

/// <summary>What <see cref="OpicentrumHistoryService"/> keeps on disk — keyed "yyyyMMdd" (week Monday) / "yyyyMM".</summary>
public sealed class OpicentrumHistoryData
{
    public Dictionary<string, OpicentrumRosterBoardSnapshot> Weeks { get; set; } = [];
    public Dictionary<string, OpicentrumVolnoSnapshot> VolnoMonths { get; set; } = [];
    public Dictionary<string, OpicentrumDutySnapshot> DutyMonths { get; set; } = [];
}

/// <summary>
/// Six months of Opicentrum history on this device (2026-10-25, POZICE_NAVRH_PLAN.md phase 2: "vem vše 6 měsíců") —
/// every week of Pozice, every month of Volno and Služby — for the position statistics and, later, the suggestions.
/// Downloaded slowly in the background (one page, then a pause — gentle on the site), resumable: each item is saved as
/// soon as it arrives, and a finished past week or month is never fetched again; only the current/upcoming ones are
/// refreshed once their copy is older than <see cref="RefreshAfter"/>. Nothing is ever deleted (user, 2026-10-25: "stáhne
/// jen jednou, uloží a data se budou doplňovat") — weeks that slide out of the window stay stored, the statistics just
/// read the window. Kept as one small gzipped file per week/month in this profile's data directory (same trust level as
/// the Preferences-backed board caches — see SaveItem), never on the relay.
/// </summary>
public sealed class OpicentrumHistoryService(IOpicentrumSyncService opicentrumSyncService, DataStorageOptions storage)
{
    public const int HistoryMonths = 6;

    private static readonly TimeSpan PauseBetweenPages = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private OpicentrumHistoryData? _data;
    private Task? _running;

    private const string FileExtension = ".json.gz";

    private string DirectoryPath => Path.Combine(storage.AppDataDirectory, "opicentrum_history");

    /// <summary>Raised (on any thread) whenever progress or data changes.</summary>
    public event Action? Changed;

    /// <summary>False already when the final <see cref="Changed"/> is raised (the task itself completes just after).</summary>
    public bool IsRunning => _isRunning;

    private volatile bool _isRunning;

    public string? ProgressText { get; private set; }

    public string? ErrorText { get; private set; }

    /// <summary>The first day of the history window (six months back, from a Monday).</summary>
    public static DateOnly WindowStart(DateOnly today) => WeekMath.StartOfWeek(today.AddMonths(-HistoryMonths));

    public OpicentrumHistoryData Data
    {
        get
        {
            lock (_gate)
                return _data ??= Load();
        }
    }

    /// <summary>How many of the window's weeks/months are stored, for the status line.</summary>
    public (int Have, int Total) Coverage(DateOnly today)
    {
        var data = Data;
        var weeks = NeededWeeks(today).ToList();
        var months = NeededMonths(today).ToList();
        var have = weeks.Count(w => data.Weeks.ContainsKey(WeekKey(w)))
                   + months.Count(m => data.VolnoMonths.ContainsKey(MonthKey(m)))
                   + months.Count(m => data.DutyMonths.ContainsKey(MonthKey(m)));
        return (have, weeks.Count + 2 * months.Count);
    }

    /// <summary>Starts (or joins) the background download of whatever is missing or stale. Never throws.</summary>
    public Task UpdateAsync()
    {
        lock (_gate)
        {
            if (_running is { IsCompleted: false }) return _running;
            _isRunning = true;
            _running = Task.Run(RunAsync);
            return _running;
        }
    }

    private async Task RunAsync()
    {
        ErrorText = null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var data = Data;
        try
        {
            if (!await opicentrumSyncService.HasCredentialsAsync())
            {
                ErrorText = OpicentrumCredentialsRequest.MissingText;
                _isRunning = false;
                Report(null);
                return;
            }

            var weeks = NeededWeeks(today).Where(w => IsDue(data.Weeks.GetValueOrDefault(WeekKey(w))?.UpdatedAtUtc, w.AddDays(6), today)).ToList();
            var months = NeededMonths(today).ToList();
            var volno = months.Where(m => IsDue(data.VolnoMonths.GetValueOrDefault(MonthKey(m))?.UpdatedAtUtc, MonthEnd(m), today)).ToList();
            var duty = months.Where(m => IsDue(data.DutyMonths.GetValueOrDefault(MonthKey(m))?.UpdatedAtUtc, MonthEnd(m), today)).ToList();
            var total = weeks.Count + volno.Count + duty.Count;
            var done = 0;

            // Newest first: the recent weeks matter most and are there soonest.
            foreach (var week in weeks.OrderByDescending(w => w))
            {
                Report($"Stahuji historii: týden {week.ToString("d. M. yyyy", CultureInfo.InvariantCulture)} ({++done}/{total})");
                var snapshot = await opicentrumSyncService.FetchFullRosterBoardAsync(week);
                lock (_gate) data.Weeks[WeekKey(week)] = snapshot;
                SaveItem("roster", WeekKey(week), snapshot);
                await Task.Delay(PauseBetweenPages);
            }
            foreach (var month in volno.OrderByDescending(m => m))
            {
                Report($"Stahuji historii: volno {month.Month}/{month.Year} ({++done}/{total})");
                var snapshot = await opicentrumSyncService.FetchVolnoForMonthAsync(month);
                lock (_gate) data.VolnoMonths[MonthKey(month)] = snapshot;
                SaveItem("volno", MonthKey(month), snapshot);
                await Task.Delay(PauseBetweenPages);
            }
            foreach (var month in duty.OrderByDescending(m => m))
            {
                Report($"Stahuji historii: služby {month.Month}/{month.Year} ({++done}/{total})");
                var snapshot = await opicentrumSyncService.FetchDutiesForMonthAsync(month);
                lock (_gate) data.DutyMonths[MonthKey(month)] = snapshot;
                SaveItem("duty", MonthKey(month), snapshot);
                await Task.Delay(PauseBetweenPages);
            }

            AppLog.Event("opicentrum.history.updated", ("fetched", total));
        }
        catch (Exception ex)
        {
            AppLog.Warning(nameof(OpicentrumHistoryService), "downloading the Opicentrum history failed", ex);
            ErrorText = ex is InvalidOperationException ? ex.Message : "Stažení historie z Opicentra se přerušilo — zkuste to znovu, stažené části zůstanou.";
        }
        _isRunning = false;
        Report(null);
    }

    private void Report(string? progress)
    {
        ProgressText = progress;
        Changed?.Invoke();
    }

    /// <summary>A finished period fetched after it ended never changes again; anything else is refreshed once its copy is old.</summary>
    private static bool IsDue(DateTimeOffset? fetchedAtUtc, DateOnly periodEnd, DateOnly today)
    {
        if (fetchedAtUtc is not { } fetched) return true;
        var fetchedDay = DateOnly.FromDateTime(fetched.ToLocalTime().DateTime);
        if (periodEnd < today && fetchedDay > periodEnd) return false;
        return DateTimeOffset.UtcNow - fetched > RefreshAfter;
    }

    private static IEnumerable<DateOnly> NeededWeeks(DateOnly today)
    {
        var last = WeekMath.DefaultViewWeek(today);
        for (var week = WindowStart(today); week <= last; week = week.AddDays(7))
            yield return week;
    }

    private static IEnumerable<DateOnly> NeededMonths(DateOnly today)
    {
        var start = WindowStart(today);
        for (var month = new DateOnly(start.Year, start.Month, 1); month <= today; month = month.AddMonths(1))
            yield return month;
    }

    private static DateOnly MonthEnd(DateOnly month) => month.AddMonths(1).AddDays(-1);

    private static string WeekKey(DateOnly week) => week.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string MonthKey(DateOnly month) => month.ToString("yyyyMM", CultureInfo.InvariantCulture);

    /// <summary>Reads every stored period; one that cannot be read is skipped (and simply fetched again).</summary>
    private OpicentrumHistoryData Load()
    {
        var data = new OpicentrumHistoryData();
        if (!Directory.Exists(DirectoryPath)) return data;

        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*" + FileExtension))
        {
            var name = Path.GetFileName(path)[..^FileExtension.Length]; // "roster-20261020"
            var dash = name.IndexOf('-');
            if (dash < 0) continue;
            var (kind, key) = (name[..dash], name[(dash + 1)..]);
            try
            {
                using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
                switch (kind)
                {
                    case "roster" when JsonSerializer.Deserialize<OpicentrumRosterBoardSnapshot>(stream, JsonOptions) is { } week: data.Weeks[key] = week; break;
                    case "volno" when JsonSerializer.Deserialize<OpicentrumVolnoSnapshot>(stream, JsonOptions) is { } volno: data.VolnoMonths[key] = volno; break;
                    case "duty" when JsonSerializer.Deserialize<OpicentrumDutySnapshot>(stream, JsonOptions) is { } duty: data.DutyMonths[key] = duty; break;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(nameof(OpicentrumHistoryService), $"stored history file {name} is unreadable — it will be fetched again", ex);
            }
        }
        return data;
    }

    /// <summary>
    /// One gzipped JSON file per week/month (2026-10-25, user: "aby z toho nebyl mega soubor"): a download writes only
    /// its own few kilobytes, never the whole history again. Roster/leave JSON repeats the same names and keys over and
    /// over, so gzip shrinks it to roughly a tenth.
    /// </summary>
    private void SaveItem<T>(string kind, string key, T snapshot)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, $"{kind}-{key}{FileExtension}");
        var temp = path + ".tmp";
        using (var stream = new GZipStream(File.Create(temp), CompressionLevel.SmallestSize))
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
        File.Move(temp, path, overwrite: true);
    }
}
