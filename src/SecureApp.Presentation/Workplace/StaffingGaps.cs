using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Events;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Notifications;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// One hole in the staffing found by <see cref="StaffingGapChecker"/> (2026-10-28, the user's ask: warn Admin/Modifier when
/// the afternoon shift is unstaffed a week ahead, and when a workplace has a doctor but no nurse (or the reverse) two days
/// ahead). <see cref="Key"/> is stable per hole so it is announced exactly once.
/// </summary>
public sealed record StaffingGap(DateOnly Date, StaffingGapKind Kind, string Key, string Text);

public enum StaffingGapKind
{
    /// <summary>No one on the "Odpolední 13-21 h" row of a day that is otherwise planned.</summary>
    AfternoonShift = 0,

    /// <summary>A workplace staffed by doctors or by nurses, but not by both.</summary>
    Position = 1,
}

/// <summary>
/// What the last check found, kept in <c>Preferences</c> so the home-screen widget (which has no network and no services
/// of its own) can paint the affected days, plus which holes were already announced. Pruned to the days still ahead.
/// </summary>
internal static class StaffingGapStore
{
    private const string GapsKey = "staffing_gaps_json";
    private const string NotifiedKey = "staffing_gaps_notified";

    private static string Pref(string key) => Profiles.ActiveProfile.PrefKey(key);

    public static IReadOnlyList<StaffingGap> Load()
    {
        try
        {
            var json = Microsoft.Maui.Storage.Preferences.Default.Get(Pref(GapsKey), string.Empty);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<StaffingGap>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Returns true when the stored set actually changed (so the widget needs a redraw).</summary>
    public static bool Save(IReadOnlyList<StaffingGap> gaps)
    {
        var json = JsonSerializer.Serialize(gaps);
        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        if (prefs.Get(Pref(GapsKey), string.Empty) == json) return false;
        prefs.Set(Pref(GapsKey), json);
        return true;
    }

    /// <summary>The days the widget calendar should flag — today onwards only, an old hole is no longer actionable.</summary>
    public static IReadOnlySet<DateOnly> GapDates(DateOnly today) =>
        Load().Where(g => g.Date >= today).Select(g => g.Date).ToHashSet();

    public static HashSet<string> LoadNotified()
    {
        var raw = Microsoft.Maui.Storage.Preferences.Default.Get(Pref(NotifiedKey), string.Empty);
        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    public static void SaveNotified(IEnumerable<string> keys, DateOnly today)
    {
        // Keys look like "kind|yyyyMMdd|…": drop the ones whose day is long gone so the list cannot grow forever.
        var keep = keys
            .Where(k => k.Split('|') is { Length: > 1 } parts
                && DateOnly.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && d >= today.AddDays(-14))
            .Distinct(StringComparer.Ordinal);
        Microsoft.Maui.Storage.Preferences.Default.Set(Pref(NotifiedKey), string.Join('\n', keep));
    }
}

/// <summary>Pure analysis of the doctors' and nurses' roster grids — no I/O, so it can be checked against saved pages.</summary>
internal static partial class StaffingGapAnalysis
{
    /// <summary>The afternoon shift is looked at this many days ahead (the user's "týden dopředu").</summary>
    public const int AfternoonLookAheadDays = 7;

    /// <summary>A workplace is looked at this many days ahead (the user's "2 dny dopředu").</summary>
    public const int PositionLookAheadDays = 2;

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    /// <param name="doctors">The doctors' grids covering today..today+7.</param>
    /// <param name="nurses">The nurses' grids for the same weeks, or empty when the nurses' login is not set up.</param>
    public static List<StaffingGap> Analyze(IReadOnlyList<OpicentrumGridWeek> doctors, IReadOnlyList<OpicentrumGridWeek> nurses, DateOnly today)
    {
        var gaps = new List<StaffingGap>();
        var doctorRows = doctors.SelectMany(w => w.Rows).ToList();
        var nurseRows = nurses.SelectMany(w => w.Rows).ToList();

        for (var offset = 1; offset <= AfternoonLookAheadDays; offset++)
        {
            var date = today.AddDays(offset);
            if (IsDayPlanned(doctorRows, date) && AfternoonShiftEmpty(doctorRows, date))
                gaps.Add(new StaffingGap(date, StaffingGapKind.AfternoonShift, $"A|{date:yyyyMMdd}|odpoledni",
                    $"{DayLabel(date)}: neobsazená odpolední směna."));
        }

        // Nurses not set up (or their week unavailable) → nothing to compare against, so no position alerts at all
        // rather than every staffed room being reported as "missing a nurse".
        if (nurseRows.Count == 0) return gaps;

        for (var offset = 1; offset <= PositionLookAheadDays; offset++)
        {
            var date = today.AddDays(offset);
            foreach (var doctorRow in doctorRows.Where(r => IsOperatingRoom(r.Label)))
            {
                if (!doctorRow.Cells.TryGetValue(date, out var doctorCell) || doctorCell.Closed) continue;
                var nurseRow = nurseRows.FirstOrDefault(r => SameRoom(r.Label, doctorRow.Label));
                if (nurseRow is null || !nurseRow.Cells.TryGetValue(date, out var nurseCell) || nurseCell.Closed) continue;

                var doctorsHere = doctorCell.Names.Count;
                var nursesHere = nurseCell.Names.Count;
                if (doctorsHere == 0 && nursesHere == 0) continue; // the room is simply not running that day

                if (doctorsHere == 0)
                    gaps.Add(new StaffingGap(date, StaffingGapKind.Position, $"P|{date:yyyyMMdd}|{RoomKey(doctorRow.Label)}|lekar",
                        $"{DayLabel(date)}: {doctorRow.Label} – chybí lékař."));
                else if (nursesHere == 0)
                    gaps.Add(new StaffingGap(date, StaffingGapKind.Position, $"P|{date:yyyyMMdd}|{RoomKey(doctorRow.Label)}|sestra",
                        $"{DayLabel(date)}: {doctorRow.Label} – chybí sestra."));
            }
        }

        return gaps;
    }

    private static string DayLabel(DateOnly date) => date.ToString("ddd d. M.", Czech);

    /// <summary>"Odpolední 13-21 h" is the doctors' shifted shift; only the doctors' page has it.</summary>
    private static bool AfternoonShiftEmpty(List<OpicentrumGridRow> doctorRows, DateOnly date)
    {
        var row = doctorRows.FirstOrDefault(r => r.Label.StartsWith("Odpolední", StringComparison.OrdinalIgnoreCase));
        return row is not null && row.Cells.TryGetValue(date, out var cell) && !cell.Closed && cell.Names.Count == 0;
    }

    /// <summary>A day counts as planned once at least one operating room has a doctor — an unplanned future day (or a holiday, shown as "--") is not a staffing hole.</summary>
    private static bool IsDayPlanned(List<OpicentrumGridRow> doctorRows, DateOnly date) =>
        doctorRows.Any(r => IsOperatingRoom(r.Label) && r.Cells.TryGetValue(date, out var cell) && !cell.Closed && cell.Names.Count > 0);

    /// <summary>The rooms that need both a doctor and a nurse: "Sál č. N …". PACU, ambulances and the like are legitimately staffed by one side only.</summary>
    private static bool IsOperatingRoom(string label) => RoomNumberRegex().IsMatch(label);

    private static bool SameRoom(string a, string b) => RoomKey(a) == RoomKey(b);

    private static string RoomKey(string label)
    {
        var m = RoomNumberRegex().Match(label);
        return m.Success ? "sal" + m.Groups["n"].Value : OpicentrumParsing.NormalizeName(label);
    }

    [GeneratedRegex(@"^\s*Sál\s*č\.?\s*(?<n>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RoomNumberRegex();
}

/// <summary>
/// The background check behind the unstaffed-position alerts. Runs only on an Admin/Modifier device, reads the doctors'
/// roster with the member's own Opicentrum login and the nurses' roster with the shared viewer login (see
/// <see cref="IOpicentrumSyncService.HasNurseCredentialsAsync"/>), saves what it found for the widget calendar and raises
/// ONE notification per newly found hole.
/// </summary>
public sealed class StaffingGapChecker(
    IOpicentrumSyncService opicentrum,
    INotificationRepository notifications,
    ICurrentUserService currentUser,
    INativeNotificationService nativeNotifications)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>How old a doctors' week from the regular sync may be and still count as current (the sync runs every 10 min by day).</summary>
    private static readonly TimeSpan DoctorCacheMaxAge = TimeSpan.FromMinutes(25);

    /// <summary>The caller throttles; this only guards against two overlapping runs.</summary>
    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return 0;
        try
        {
            await currentUser.InitializeAsync();
            if (currentUser.Current.Role is not (Role.Admin or Role.Modifier)) return 0;
            if (!await opicentrum.HasCredentialsAsync(ct)) return 0;

            var today = DateOnly.FromDateTime(DateTime.Today);
            var weekStarts = new[] { StartOfWeek(today), StartOfWeek(today).AddDays(7) };

            // The doctors' weeks are normally already here: the regular Opicentrum sync (every 10 min) downloads them for
            // the schedule, so asking the portal again would only load it twice. Only a week the sync has not delivered
            // recently is fetched — all missing weeks in one login.
            var doctorWeeks = weekStarts.Select(m => OpicentrumSyncService.TryGetCachedDoctorGrid(m, DoctorCacheMaxAge)).ToList();
            if (doctorWeeks.Any(w => w is null))
                doctorWeeks = (await opicentrum.FetchRosterGridsAsync(weekStarts, nurses: false, ct)).Select(w => (OpicentrumGridWeek?)w).ToList();

            var nurseWeeks = new List<OpicentrumGridWeek>();
            if (await opicentrum.HasNurseCredentialsAsync(ct))
            {
                try
                {
                    nurseWeeks.AddRange(await opicentrum.FetchRosterGridsAsync(weekStarts, nurses: true, ct));
                }
                catch (InvalidOperationException)
                {
                    // Wrong/expired viewer login: keep the afternoon-shift check running, just without the nurse comparison.
                    nurseWeeks.Clear();
                }
            }

            var gaps = StaffingGapAnalysis.Analyze(doctorWeeks.Select(w => w!).ToList(), nurseWeeks, today);
            if (StaffingGapStore.Save(gaps))
                WidgetRefreshSignal.Raise();

            var already = StaffingGapStore.LoadNotified();
            var fresh = gaps.Where(g => !already.Contains(g.Key)).ToList();
            if (fresh.Count > 0)
            {
                await AnnounceAsync(fresh.Where(g => g.Kind == StaffingGapKind.AfternoonShift).ToList(),
                    "Neobsazená odpolední směna", ct);
                await AnnounceAsync(fresh.Where(g => g.Kind == StaffingGapKind.Position).ToList(),
                    "Neobsazené pracoviště", ct);
                StaffingGapStore.SaveNotified(already.Concat(fresh.Select(g => g.Key)), today);
            }

            return gaps.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One notification per kind per run, listing every new hole — a week with five empty afternoons is one message, not five.</summary>
    private Task AnnounceAsync(List<StaffingGap> gaps, string title, CancellationToken ct)
    {
        if (gaps.Count == 0) return Task.CompletedTask;
        var body = string.Join("\n", gaps.OrderBy(g => g.Date).Select(g => g.Text));
        return NotificationPublisher.PublishScheduleChangeAsync(notifications, title, body, isImportant: true, nativeNotifications, ct);
    }

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
