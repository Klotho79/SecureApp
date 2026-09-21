using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Notifications;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IOpicentrumSyncService"/>
/// <remarks>
/// opicentrum.cz/ARO is a plain phpRS site (no API) — this scrapes three of its pages with regex, not
/// a proper HTML parser: the markup is old, hand-templated, and structurally regular enough (and
/// adding an HTML-parsing NuGet dependency for three page shapes felt like the wrong tradeoff). Login
/// is a simple POST (readers.php, fields rjmeno/rheslo/akce=quicklog, no CSRF token) — see
/// IOpicentrumSyncService's own remarks for why the credential itself never passes through chat/dev
/// tooling. A fresh HttpClient+CookieContainer is used per sync call rather than a persistent session,
/// since sync only runs once per Rozpis page open (the user's own explicit choice) — the extra login
/// round-trip is cheap relative to that.
///
/// Three sources, in precedence order (later overwrites earlier for the same date):
/// 1. pracoviste.php (weekly, per-room) → AssignmentType.Work, WorkplaceName = the room's own label.
/// 2. sluzby7.php (monthly, 7 on-call slots) → AssignmentType.OnCall, WorkplaceName = the slot's own label.
/// 3. spravavolna.php (monthly, per-person leave grid) → confidently mapped codes (ŘD→Vacation,
///    PN/PL→SickLeave, SC→BusinessTrip, VV/NV/PS→DayOff — see <see cref="KnownLeaveCodes"/> for the
///    user-confirmed meaning of each) or AssignmentType.Other with the raw code kept in the Note (any
///    code this class doesn't confidently recognize — safe/transparent/correctable rather than
///    guessing wrong). "--" and empty cells are skipped — read as "not in the on-call rotation that
///    day", not an absence.
///
/// 2026-09-21 correction: "PS" used to be skipped outright here on the assumption it was just a
/// same-day on-call-pool marker sluzby7.php already covers more precisely. Live diagnostic logging
/// against the real account proved that wrong — "PS" actually lands on the day AFTER a duty, not the
/// duty day itself (confirmed: 2026-09-08 had the real on-call shift per sluzby7.php, but the raw
/// spravavolna.php code was empty that day and "PS" on 2026-09-09 instead), and the user confirmed it
/// stands for "po službě" (the mandatory rest day after on-call) — so it's now mapped to DayOff like
/// VV/NV, not skipped.
///
/// Known gap, not handled this first pass: pracoviste.php's own "NEPŘÍTOMNÍ" (absent) row is a plain
/// semicolon-separated name list per day, not the per-person div shape every other row uses — skipped
/// rather than parsed with an unverified second regex shape. spravavolna.php's own leave record is the
/// actual source of truth for absence anyway.
/// </remarks>
public sealed partial class OpicentrumSyncService : IOpicentrumSyncService
{
    private const string BaseUrl = "https://opicentrum.cz/ARO/";

    /// <summary>User-confirmed meaning of each code, 2026-09-21 (their own hospital's ARO instance — not a generic Czech labor-law standard, don't assume these transfer to another Opicentrum deployment): ŘD = řádná dovolená (regular vacation), PN = pracovní neschopnost (sick leave), SC = služební cesta (business trip), PL = lékař (doctor's appointment during a shift — mapped to SickLeave, the user's own call), VV = volno po službě (mandatory rest day after an on-call shift), NV = náhradní volno (compensatory time off), PS = po službě (the rest day the day AFTER an on-call duty — confirmed via live diagnostic logging that it lands on the following day, not the duty day itself; see this class's own remarks) — VV/NV/PS all map to DayOff, the closest existing type; none is distinguished from plain DayOff today.</summary>
    private static readonly Dictionary<string, AssignmentType> KnownLeaveCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ŘD"] = AssignmentType.Vacation,
        ["PN"] = AssignmentType.SickLeave,
        ["SC"] = AssignmentType.BusinessTrip,
        ["PL"] = AssignmentType.SickLeave,
        ["VV"] = AssignmentType.DayOff,
        ["NV"] = AssignmentType.DayOff,
        ["PS"] = AssignmentType.DayOff,
    };

    private readonly ISecureVaultKeyStore _vault;
    private readonly IWorkAssignmentRepository _assignmentRepository;
    private readonly INotificationRepository _notificationRepository;

    public OpicentrumSyncService(ISecureVaultKeyStore vault, IWorkAssignmentRepository assignmentRepository, INotificationRepository notificationRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
    }

    public async Task<bool> HasCredentialsAsync(CancellationToken ct = default)
        => await _vault.RetrieveSecretAsync(OpicentrumVaultKeys.Username, ct) is not null;

    public async Task SetCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        await _vault.StoreSecretAsync(OpicentrumVaultKeys.Username, Encoding.UTF8.GetBytes(username), ct);
        await _vault.StoreSecretAsync(OpicentrumVaultKeys.Password, Encoding.UTF8.GetBytes(password), ct);
    }

    public async Task ClearCredentialsAsync(CancellationToken ct = default)
    {
        await _vault.RemoveSecretAsync(OpicentrumVaultKeys.Username, ct);
        await _vault.RemoveSecretAsync(OpicentrumVaultKeys.Password, ct);
    }

    public async Task<OpicentrumSyncResult> SyncAsync(DateOnly rangeStart, DateOnly rangeEnd, CancellationToken ct = default)
    {
        var usernameBytes = await _vault.RetrieveSecretAsync(OpicentrumVaultKeys.Username, ct);
        var passwordBytes = await _vault.RetrieveSecretAsync(OpicentrumVaultKeys.Password, ct);
        if (usernameBytes is null || passwordBytes is null)
            return OpicentrumSyncResult.NotConfigured;

        var username = Encoding.UTF8.GetString(usernameBytes);
        var password = Encoding.UTF8.GetString(passwordBytes);

        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        try
        {
            var myName = await LoginAsync(http, username, password, ct);
            if (myName is null)
            {
                const string error = "Přihlášení do Opicentra se nezdařilo — zkontrolujte uživatelské jméno a heslo v Nastavení.";
                await NotificationPublisher.PublishSystemWarningAsync(_notificationRepository, "Synchronizace rozpisu selhala", error, ct: ct);
                return new OpicentrumSyncResult(false, 0, 0, error);
            }

            var myId = await ResolvePersonIdAsync(http, rangeStart, myName, ct);

            // date -> resolved (Type, WorkplaceName, OnCallWorkplaceName) — merge order matches the
            // class-level precedence. OnCallWorkplaceName is an overlay, not a fourth precedence tier —
            // see MergeSluzbyAsync's own remarks for how a day ends up with one.
            var resolved = new Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)>();

            if (myId is { } id)
            {
                await MergePracovisteAsync(http, rangeStart, rangeEnd, id, resolved, ct);
                await MergeSluzbyAsync(http, rangeStart, rangeEnd, myName, resolved, ct);
                await MergeSpravavolnaAsync(http, rangeStart, rangeEnd, id, resolved, ct);
            }

            return await WriteBackAsync(resolved, ct);
        }
        catch (Exception ex)
        {
            var error = $"Synchronizace s Opicentrem selhala: {ex.Message}";
            try { await NotificationPublisher.PublishSystemWarningAsync(_notificationRepository, "Synchronizace rozpisu selhala", error, ct: ct); }
            catch { /* best-effort — the sync failure itself is already the thing being reported */ }
            return new OpicentrumSyncResult(false, 0, 0, error);
        }
    }

    private static async Task<string?> LoginAsync(HttpClient http, string username, string password, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rjmeno"] = username,
            ["rheslo"] = password,
            ["akce"] = "quicklog",
        });
        using var response = await http.PostAsync("readers.php", form, ct);
        response.EnsureSuccessStatusCode();

        // The login POST itself always returns 200 regardless of success — the only reliable signal
        // is whether a subsequent page shows the logged-in sidebar ("Vítej <jméno>").
        var check = await http.GetStringAsync("pracoviste.php?akce=ukazpracoviste", ct);
        var match = WelcomeRegex().Match(check);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["name"].Value).Trim() : null;
    }

    private static async Task<int?> ResolvePersonIdAsync(HttpClient http, DateOnly anyDateInRange, string myName, CancellationToken ct)
    {
        var monday = StartOfWeek(anyDateInRange);
        var html = await http.GetStringAsync($"pracoviste.php?akce=ukazpracoviste&datum={monday:yyyyMMdd}", ct);
        foreach (Match m in PersonCellRegex().Matches(html))
        {
            var name = WebUtility.HtmlDecode(StripTags(m.Groups["inner"].Value)).Trim();
            if (string.Equals(name, myName, StringComparison.OrdinalIgnoreCase))
                return int.Parse(m.Groups["osoba"].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static async Task MergePracovisteAsync(HttpClient http, DateOnly rangeStart, DateOnly rangeEnd, int myId, Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, CancellationToken ct)
    {
        foreach (var monday in DistinctMondaysInRange(rangeStart, rangeEnd))
        {
            string html;
            try { html = await http.GetStringAsync($"pracoviste.php?akce=ukazpracoviste&datum={monday:yyyyMMdd}", ct); }
            catch { continue; } // best-effort per week — one bad week must not abort the whole sync

            foreach (Match rowMatch in TableRowRegex().Matches(html))
            {
                var rowLabelMatch = RowLabelRegex().Match(rowMatch.Value);
                if (!rowLabelMatch.Success) continue;
                var workplaceName = WebUtility.HtmlDecode(rowLabelMatch.Groups["label"].Value).Trim();
                if (workplaceName is "NEPŘÍTOMNÍ" or "Plánované AKCE") continue; // different cell shape — see class remarks

                foreach (Match personMatch in PersonCellRegex().Matches(rowMatch.Value))
                {
                    if (!int.TryParse(personMatch.Groups["osoba"].Value, out var osoba) || osoba != myId) continue;
                    if (!DateOnly.TryParseExact(personMatch.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                    if (date < rangeStart || date > rangeEnd) continue;
                    resolved[date] = (AssignmentType.Work, workplaceName == "NEZAŘAZENÍ" ? "Nezařazeno" : workplaceName, null);
                }
            }
        }
    }

    /// <summary>
    /// 2026-09-21 correction — this used to unconditionally overwrite whatever pracoviste.php had
    /// already resolved for the same date, which silently destroyed a real, common pattern in the raw
    /// data: a normal weekday shift followed later the same day by an on-call duty ("v jeden den může
    /// být i směna a po ní může pokračovat služba", the user's own description, confirmed against the
    /// actual source data — not a rare edge case). Now: if pracoviste.php already resolved this date
    /// to a Work shift, the duty becomes an OVERLAY on top of it (<see cref="WorkAssignment.OnCallWorkplaceName"/>,
    /// <see cref="AssignmentType"/> stays Work) instead of replacing it. Only when there's no shift
    /// underneath — every weekend, since pracoviste.php never has one to begin with — does the duty
    /// become the day's own primary Type = OnCall, exactly as before this change.
    /// </summary>
    private static async Task MergeSluzbyAsync(HttpClient http, DateOnly rangeStart, DateOnly rangeEnd, string myName, Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, CancellationToken ct)
    {
        foreach (var (year, month) in DistinctMonthsInRange(rangeStart, rangeEnd))
        {
            string html;
            try
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["mesic"] = month.ToString(CultureInfo.InvariantCulture),
                    ["rok"] = year.ToString(CultureInfo.InvariantCulture),
                });
                using var response = await http.PostAsync("sluzby7.php?akce=ukazsluzby", form, ct);
                html = await response.Content.ReadAsStringAsync(ct);
            }
            catch { continue; }

            var columns = SluzbyColumnRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["name"].Value).Trim()).ToList();
            if (columns.Count == 0) continue;

            foreach (Match rowMatch in SluzbyRowRegex().Matches(html))
            {
                if (!DateOnly.TryParseExact(rowMatch.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (date < rangeStart || date > rangeEnd) continue;

                var cellIndex = 0;
                foreach (Match cellMatch in SluzbyCellRegex().Matches(rowMatch.Value))
                {
                    if (cellIndex >= columns.Count) break;
                    var name = WebUtility.HtmlDecode(StripTags(cellMatch.Groups["name"].Value)).Trim();
                    if (string.Equals(name, myName, StringComparison.OrdinalIgnoreCase))
                    {
                        resolved[date] = resolved.TryGetValue(date, out var current) && current.Type == AssignmentType.Work
                            ? (current.Type, current.WorkplaceName, columns[cellIndex]) // overlay on top of the shift already found
                            : (AssignmentType.OnCall, columns[cellIndex], null); // no shift underneath — duty is the whole day
                    }
                    cellIndex++;
                }
            }
        }
    }

    private static async Task MergeSpravavolnaAsync(HttpClient http, DateOnly rangeStart, DateOnly rangeEnd, int myId, Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, CancellationToken ct)
    {
        foreach (var (year, month) in DistinctMonthsInRange(rangeStart, rangeEnd))
        {
            string html;
            try { html = await http.GetStringAsync($"spravavolna.php?akce=ukazvolno&rok={year}&mesic={month}", ct); }
            catch { continue; }

            var rowMatch = SpravavolnaRowRegex(myId).Match(html);
            if (!rowMatch.Success) continue;

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var day = 1;
            foreach (Match cellMatch in SpravavolnaCellRegex().Matches(rowMatch.Groups["cells"].Value))
            {
                if (day > daysInMonth) break;
                var date = new DateOnly(year, month, day);
                day++;

                var rawCode = WebUtility.HtmlDecode(cellMatch.Groups["code"].Value).Trim();
                if (date < rangeStart || date > rangeEnd) continue;
                if (string.IsNullOrEmpty(rawCode) || rawCode == "--") continue; // no data / "not in rotation" — see class remarks

                var lettersMatch = LeadingLettersRegex().Match(rawCode);
                var code = lettersMatch.Success ? lettersMatch.Value : rawCode;

                // Full overwrite, not a merge (unlike MergeSluzbyAsync above) — real leave/vacation
                // always wins outright, including clearing any on-call overlay a stale prior sync
                // might have left on this date; a confirmed vacation day must never still show a duty.
                resolved[date] = KnownLeaveCodes.TryGetValue(code, out var type)
                    ? (type, null, null)
                    : (AssignmentType.Other, $"Volno (kód {rawCode}, import z Opicentra)", null);
            }
        }
    }

    private async Task<OpicentrumSyncResult> WriteBackAsync(Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, CancellationToken ct)
    {
        if (resolved.Count == 0)
            return new OpicentrumSyncResult(true, 0, 0, null);

        var minDate = resolved.Keys.Min();
        var maxDate = resolved.Keys.Max();
        var existing = await _assignmentRepository.GetByDateRangeAsync(minDate, maxDate, ct);
        var existingByDate = existing.ToDictionary(a => a.Date);

        var created = 0;
        var updated = 0;

        foreach (var (date, (type, workplaceName, onCallWorkplaceName)) in resolved)
        {
            if (existingByDate.TryGetValue(date, out var current))
            {
                if (current.Type == type && current.WorkplaceName == workplaceName && current.OnCallWorkplaceName == onCallWorkplaceName)
                    continue; // already matches — no write, no noise

                var previousLabel = DescribeAssignment(current.Type, current.WorkplaceName, current.OnCallWorkplaceName);
                var newLabel = DescribeAssignment(type, workplaceName, onCallWorkplaceName);
                // WorkplaceId intentionally cleared (null) — the imported name is Opicentrum's own
                // label, which isn't guaranteed to correspond to this device's shared Workplace
                // catalog entry; Note is deliberately preserved (the user's own instruction: "vytvoří
                // se poznamka do zalozky system" — the overwrite is recorded as a Notification, not by
                // clobbering whatever the user wrote here themselves).
                current.Update(type, current.StartTime, current.EndTime, null, workplaceName, current.Note, onCallWorkplaceName);
                await _assignmentRepository.UpdateAsync(current, ct);
                updated++;

                await NotificationPublisher.PublishSystemWarningAsync(
                    _notificationRepository,
                    "Rozpis aktualizován z Opicentra",
                    $"{date:d. M. yyyy}: bylo „{previousLabel}“, nyní „{newLabel}“.",
                    ct: ct);
            }
            else
            {
                var assignment = new WorkAssignment(date, type, workplaceName: workplaceName, onCallWorkplaceName: onCallWorkplaceName);
                await _assignmentRepository.AddAsync(assignment, ct);
                created++;
            }
        }

        return new OpicentrumSyncResult(true, created, updated, null);
    }

    private static string DescribeAssignment(AssignmentType type, string? workplaceName, string? onCallWorkplaceName)
    {
        var baseLabel = string.IsNullOrEmpty(workplaceName) ? AssignmentTypeCatalog.Label(type) : $"{AssignmentTypeCatalog.Label(type)} · {workplaceName}";
        return string.IsNullOrEmpty(onCallWorkplaceName) ? baseLabel : $"{baseLabel} + {AssignmentTypeCatalog.Label(AssignmentType.OnCall)}: {onCallWorkplaceName}";
    }

    private static IEnumerable<DateOnly> DistinctMondaysInRange(DateOnly start, DateOnly end)
    {
        var monday = StartOfWeek(start);
        while (monday <= end)
        {
            yield return monday;
            monday = monday.AddDays(7);
        }
    }

    private static IEnumerable<(int Year, int Month)> DistinctMonthsInRange(DateOnly start, DateOnly end)
    {
        var cursor = new DateOnly(start.Year, start.Month, 1);
        var endMonth = new DateOnly(end.Year, end.Month, 1);
        while (cursor <= endMonth)
        {
            yield return (cursor.Year, cursor.Month);
            cursor = cursor.AddMonths(1);
        }
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = (int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1;
        return date.AddDays(-diff);
    }

    private static string StripTags(string html) => TagRegex().Replace(html, string.Empty);

    private static Regex SpravavolnaRowRegex(int myId) => new(
        $@"idlekuprvolna={myId}"">[^<]*</a>(?<cells>.*?)</tr>",
        RegexOptions.Singleline);

    [GeneratedRegex(@"Vítej\s+(?<name>[^<]+)")]
    private static partial Regex WelcomeRegex();

    [GeneratedRegex(@"<div onmousedown=""datumupravovany=(?<date>\d+); osoba=(?<osoba>\d+); sal1=\d+;?""[^>]*>(?<inner>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex PersonCellRegex();

    [GeneratedRegex(@"<tr class=""planakci(?:sns)?""[^>]*>.*?</tr>", RegexOptions.Singleline)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<td[^>]*><b>(?<label>[^<]+)</b></td>")]
    private static partial Regex RowLabelRegex();

    [GeneratedRegex(@"<td align=""center"" width=120><b>(?<name>[^<]+)</b></td>")]
    private static partial Regex SluzbyColumnRegex();

    [GeneratedRegex(@"<tr class=""planakci(?:sns)?"">\s*<td align=""center""><b>(?<date>\d{2}\.\d{2}\.\d{4})</b></td>(?<cells>.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex SluzbyRowRegex();

    [GeneratedRegex(@"<td align=""center"">(?<name>[^<]+)")]
    private static partial Regex SluzbyCellRegex();

    [GeneratedRegex(@"<td\s*[^>]*>(?<code>[^<]*)</td>")]
    private static partial Regex SpravavolnaCellRegex();

    [GeneratedRegex(@"^\p{L}+")]
    private static partial Regex LeadingLettersRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
