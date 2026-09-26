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
/// pracoviste.php is parsed by <see cref="OpicentrumParsing.ParsePracovisteWeek"/>, matching people by
/// name (see its remarks for why not by the roster editor's person-id attributes). Its "NEPŘÍTOMNÍ"
/// (absent) row is skipped — spravavolna.php's own leave record is the source of truth for absence.
/// </remarks>
public sealed partial class OpicentrumSyncService : IOpicentrumSyncService
{
    private const string BaseUrl = "https://opicentrum.cz/ARO/";

    /// <summary>
    /// .NET's default HttpClient User-Agent on Android is "Dalvik/2.1.0 (...)" — the site sniffs this
    /// and serves a "Stránka vyžaduje Javascript, použijte Chrome" fallback page instead of real content
    /// (confirmed 2026-09-22 via a live full-response dump on pozadavky.php: it's a plain User-Agent
    /// substring check, not a real JS check). A normal desktop Chrome UA string sails through. Used on
    /// EVERY HttpClient this class creates, including <see cref="SyncAsync"/>'s — 2026-09-22 correction:
    /// this was briefly scoped to <see cref="PushNoteAsync"/> only, on the guess that SyncAsync's three
    /// read-only pages were fine under the plain Dalvik UA and broke under this one instead. That guess
    /// was wrong: reverting it made SyncAsync's own <see cref="LoginAsync"/> welcome-check (which GETs
    /// pracoviste.php looking for "Vítej ...") start failing under the reverted-to-Dalvik UA while
    /// PushNoteAsync's identical login kept succeeding under the Chrome UA — proving the UA gate isn't
    /// pozadavky.php-specific, it's site-wide (pracoviste.php included), so every request this class
    /// makes needs it.
    /// </summary>
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

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

    /// <summary>
    /// The site can only handle one active login per account at a time — two near-simultaneous logins
    /// (confirmed live 2026-09-22, in two different shapes: two concurrent <see cref="SyncAsync"/>
    /// range-syncs racing each other, AND a <see cref="SyncAsync"/> racing a <see cref="PushNoteAsync"/>)
    /// collide server-side, silently invalidating whichever login's own follow-up request lands second —
    /// that call then sees itself logged out and reports "login failed" even though nothing about it was
    /// actually wrong. A static, class-wide gate (not per-instance — DI lifetime doesn't matter here)
    /// serializes every login this class ever makes, app-wide, so this can't happen again regardless of
    /// which two entry points happen to race.
    /// </summary>
    private static readonly SemaphoreSlim LoginGate = new(1, 1);

    /// <summary>Nominative Czech month names, matching pozadavky.php's own month dropdown exactly (0-based, so index Month-1) — used to reproduce its submit button's own label text (<c>"Změnit požadavky na {měsíc}"</c>) in <see cref="PushNoteAsync"/>.</summary>
    private static readonly string[] PozadavkyMonthNames =
    [
        "Leden", "Únor", "Březen", "Duben", "Květen", "Červen",
        "Červenec", "Srpen", "Září", "Říjen", "Listopad", "Prosinec"
    ];

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
        http.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);

        await LoginGate.WaitAsync(ct);
        try
        {
            var myName = await LoginAsync(http, username, password, ct);
            if (myName is null)
            {
                const string error = "Přihlášení do Opicentra se nezdařilo — zkontrolujte uživatelské jméno a heslo v Nastavení.";
                await NotificationPublisher.PublishSystemWarningAsync(_notificationRepository, "Synchronizace rozpisu selhala", error, ct: ct);
                return new OpicentrumSyncResult(false, 0, 0, error);
            }

            // Resolving WHICH person we are is the single point everything else hangs off — without an
            // id, pracoviste/sluzby/spravavolna all produce nothing. Two sources, because either can
            // legitimately miss someone: pracoviste.php only lists people ROSTERED in the week being
            // viewed (a new colleague, or anyone off that week, simply is not on it), while
            // spravavolna.php's monthly grid carries a row per person regardless of duty.
            // The id is only needed for spravavolna.php (its rows are keyed by id); pracoviste.php and
            // sluzby7.php are matched by name, so a missing id no longer blanks the whole schedule.
            var myId = await ResolvePersonIdAsync(http, rangeStart, myName, ct)
                       ?? await ResolvePersonIdFromLeavePageAsync(http, rangeStart, myName, ct);

            // date -> resolved (Type, WorkplaceName, OnCallWorkplaceName) — merge order matches the
            // class-level precedence. OnCallWorkplaceName is an overlay, not a fourth precedence tier —
            // see MergeSluzbyAsync's own remarks for how a day ends up with one.
            var resolved = new Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)>();

            // Everyone on duty per day, for the widget — collected from the same pages, see DutyRosterStore.
            var onDuty = new Dictionary<DateOnly, List<DutyEntry>>();
            var shifted = new Dictionary<DateOnly, List<DutyEntry>>();

            await MergePracovisteAsync(http, rangeStart, rangeEnd, myName, myId, resolved, shifted, ct);
            await MergeSluzbyAsync(http, rangeStart, rangeEnd, myName, resolved, onDuty, ct);
            try
            {
                // Posunutá služba listed after the regular duty slots.
                foreach (var (date, late) in shifted)
                {
                    foreach (var entry in late)
                        AddDuty(onDuty, date, entry.Position, entry.Name);
                }
                DutyRosterStore.Save(rangeStart, rangeEnd, onDuty);
                SecureApp.Domain.Events.WidgetRefreshSignal.Raise();
            }
            catch { /* best-effort — the widget line just stays stale */ }
            if (myId is { } id)
                await MergeSpravavolnaAsync(http, rangeStart, rangeEnd, id, resolved, ct);

            if (myId is null && resolved.Count == 0)
            {
                // Previously this fell through to an empty write-back, which reports itself as a
                // perfectly successful "synced, nothing changed" — so a user whose name simply did not
                // match saw a blank Rozpis with no error anywhere, forever (reported live 2026-09-24 by
                // a new member who WAS on the real roster). A failure to identify the user is a real
                // failure and has to say so, naming what it searched for so the mismatch is obvious.
                var error = $"V Opicentru se nepodařilo najít osobu „{myName}“ — rozpis proto zůstal prázdný. Zkontrolujte, zda jste na webu uveden pod stejným jménem.";
                await NotificationPublisher.PublishSystemWarningAsync(_notificationRepository, "Synchronizace rozpisu selhala", error, ct: ct);
                return new OpicentrumSyncResult(false, 0, 0, error);
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
        finally
        {
            LoginGate.Release();
        }
    }

    /// <summary>
    /// Writes to pozadavky.php — the Opicentrum leave-REQUEST form (2026-09-22, user's own ask: "chci
    /// aby se poznamka propsala do webu"). A real WRITE to the user's actual hospital scheduling
    /// system, not local-only — handled with real care:
    ///
    /// The form covers the WHOLE month in one POST (one &lt;form&gt;, ~4 fields × every day), so
    /// pushing a note means re-submitting every other day's fields completely unchanged, not just the
    /// target day's own. A genuine trap found reading the real page source: <c>volnehod[N]</c> (the
    /// leave HOURS select) is never marked with a static <c>selected</c> attribute — its real current
    /// value is set client-side by an inline <c>onload="plnvolhod(N,&lt;hours&gt;,...)"</c> call, which a
    /// plain HTTP scrape (no JS execution) would otherwise miss entirely, silently submitting an empty
    /// value and WIPING OUT real already-recorded leave hours for that day. Every field this method
    /// doesn't intend to change is read from its own true current source (the <c>plnvolhod</c> call for
    /// hours, the select's own <c>&lt;option selected&gt;</c> for the leave code, the checkbox's own
    /// <c>checked</c> attribute) and re-submitted byte-for-byte as found — only <c>specsal[date.Day]</c>
    /// (max 20 chars, the form's own limit) is ever replaced.
    ///
    /// A day with no matching "planakci"-class row at all (every non-worked/grayed-out "planakcisns" day
    /// — confirmed against the real page: those literally have no &lt;input&gt;/&lt;select&gt; for that day,
    /// nothing to submit) returns <see cref="OpicentrumNotePushStatus.DayNotEditable"/> without attempting
    /// a write. After posting, the page is re-fetched and the target day's own specsal value is checked
    /// against what was just sent — real confirmation the write landed, not just that the HTTP call didn't
    /// throw.
    /// </summary>
    public async Task<OpicentrumNotePushResult> PushNoteAsync(DateOnly date, string? note, CancellationToken ct = default)
    {
        var usernameBytes = await _vault.RetrieveSecretAsync(OpicentrumVaultKeys.Username, ct);
        var passwordBytes = await _vault.RetrieveSecretAsync(OpicentrumVaultKeys.Password, ct);
        if (usernameBytes is null || passwordBytes is null)
            return new OpicentrumNotePushResult(OpicentrumNotePushStatus.NotConfigured, null);

        var username = Encoding.UTF8.GetString(usernameBytes);
        var password = Encoding.UTF8.GetString(passwordBytes);
        var targetNote = Truncate(note ?? string.Empty, 20);

        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);

        await LoginGate.WaitAsync(ct);
        try
        {
            var myName = await LoginAsync(http, username, password, ct);
            if (myName is null)
                return new OpicentrumNotePushResult(OpicentrumNotePushStatus.Failed, "Přihlášení do Opicentra se nezdařilo — zkontrolujte uživatelské jméno a heslo v Nastavení.");

            var html = await http.GetStringAsync($"pozadavky.php?akce=ukaz&rok={date.Year}&mesic={date.Month}", ct);
            var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
            var formValues = new List<KeyValuePair<string, string>>();
            var targetDayEditable = false;

            for (var day = 1; day <= daysInMonth; day++)
            {
                var rowMatch = PozadavkyRowRegex(day, date.Month, date.Year).Match(html);
                if (!rowMatch.Success) continue; // "planakcisns" (non-worked) day — genuinely no fields to submit
                var row = rowMatch.Value;

                if (NpCheckedRegex(day).IsMatch(row))
                    formValues.Add(new($"np[{day}]", "1"));

                // True current hours value comes from the onload plnvolhod(day, hours, ...) call, NOT a
                // static <option selected> (there isn't one) — see this method's own remarks.
                var hoursMatch = PlnvolhodRegex(day).Match(row);
                formValues.Add(new($"volnehod[{day}]", hoursMatch.Success ? hoursMatch.Groups["hours"].Value : string.Empty));

                var codeMatch = KodvolnaSelectedRegex(day).Match(row);
                formValues.Add(new($"kodvolna[{day}]", codeMatch.Success ? WebUtility.HtmlDecode(codeMatch.Groups["code"].Value) : string.Empty));

                var isTargetDay = day == date.Day;
                if (isTargetDay) targetDayEditable = true;

                var specsalMatch = SpecsalValueRegex(day).Match(row);
                var currentNote = isTargetDay
                    ? targetNote
                    : specsalMatch.Success ? WebUtility.HtmlDecode(specsalMatch.Groups["value"].Value) : string.Empty;
                formValues.Add(new($"specsal[{day}]", currentNote));
            }

            if (!targetDayEditable)
                return new OpicentrumNotePushResult(OpicentrumNotePushStatus.DayNotEditable, "Tento den nemá na webu žádné políčko (nepracovní den) — poznámku sem nelze zapsat.");

            formValues.Add(new("ulozit", $"Změnit požadavky na {PozadavkyMonthNames[date.Month - 1]}"));

            using var content = new FormUrlEncodedContent(formValues);
            using var response = await http.PostAsync($"pozadavky.php?akce=uprav&mesic={date.Month:D2}&rok={date.Year}", content, ct);
            response.EnsureSuccessStatusCode();

            // Real confirmation, not just "the POST didn't throw" — re-fetch and check the target day's
            // own specsal actually reflects what was just sent (see this method's own remarks). Same
            // GET query-string form as the initial fetch above (matches the page's own <a href> links).
            var confirmHtml = await http.GetStringAsync($"pozadavky.php?akce=ukaz&rok={date.Year}&mesic={date.Month}", ct);
            var confirmRow = PozadavkyRowRegex(date.Day, date.Month, date.Year).Match(confirmHtml);
            var confirmedNote = confirmRow.Success ? SpecsalValueRegex(date.Day).Match(confirmRow.Value) : Match.Empty;
            var landed = confirmedNote.Success && WebUtility.HtmlDecode(confirmedNote.Groups["value"].Value) == targetNote;

            return landed
                ? new OpicentrumNotePushResult(OpicentrumNotePushStatus.Success, null)
                : new OpicentrumNotePushResult(OpicentrumNotePushStatus.Failed, "Odesláno, ale poznámka se na webu neobjevila — zkontrolujte to prosím ručně.");
        }
        catch (Exception ex)
        {
            return new OpicentrumNotePushResult(OpicentrumNotePushStatus.Failed, ex.Message);
        }
        finally
        {
            LoginGate.Release();
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

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

    /// <summary>Only yields an id for accounts that see the roster editor's attributes — see <see cref="OpicentrumParsing"/>'s remarks; everyone else is identified via <see cref="ResolvePersonIdFromLeavePageAsync"/>.</summary>
    private static async Task<int?> ResolvePersonIdAsync(HttpClient http, DateOnly anyDateInRange, string myName, CancellationToken ct)
    {
        var monday = StartOfWeek(anyDateInRange);
        var html = await http.GetStringAsync($"pracoviste.php?akce=ukazpracoviste&datum={monday:yyyyMMdd}", ct);
        return OpicentrumParsing.ParsePracovisteWeek(html)
            .FirstOrDefault(e => e.PersonId is not null && NamesMatch(e.PersonName, myName))
            ?.PersonId;
    }

    /// <summary>
    /// Fallback identification against spravavolna.php's monthly grid, which carries one row per
    /// person whether or not they are on duty — unlike pracoviste.php, which only lists that week's
    /// roster. The anchor this reads (<c>idlekuprvolna=&lt;id&gt;"&gt;Name&lt;/a&gt;</c>) is the exact
    /// shape <see cref="SpravavolnaRowRegex"/> already relies on to find the leave row itself, so this
    /// introduces no new assumption about the page.
    /// </summary>
    private static async Task<int?> ResolvePersonIdFromLeavePageAsync(HttpClient http, DateOnly anyDateInRange, string myName, CancellationToken ct)
    {
        string html;
        try { html = await http.GetStringAsync($"spravavolna.php?akce=ukazvolno&rok={anyDateInRange.Year}&mesic={anyDateInRange.Month}", ct); }
        catch { return null; }

        foreach (Match m in LeavePersonLinkRegex().Matches(html))
        {
            var name = WebUtility.HtmlDecode(m.Groups["name"].Value);
            if (NamesMatch(name, myName))
                return int.Parse(m.Groups["osoba"].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static bool NamesMatch(string a, string b) => OpicentrumParsing.NamesMatch(a, b);

    /// <summary>
    /// Matches by name, and additionally by person id when both are known — see
    /// <see cref="OpicentrumParsing"/>'s remarks for why the id alone (what this used to rely on) left
    /// every non-editor account without workplaces.
    /// </summary>
    private static async Task MergePracovisteAsync(HttpClient http, DateOnly rangeStart, DateOnly rangeEnd, string myName, int? myId, Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, Dictionary<DateOnly, List<DutyEntry>> shifted, CancellationToken ct)
    {
        foreach (var monday in DistinctMondaysInRange(rangeStart, rangeEnd))
        {
            string html;
            try { html = await http.GetStringAsync($"pracoviste.php?akce=ukazpracoviste&datum={monday:yyyyMMdd}", ct); }
            catch { continue; } // best-effort per week — one bad week must not abort the whole sync

            foreach (var entry in OpicentrumParsing.ParsePracovisteWeek(html))
            {
                if (entry.Date < rangeStart || entry.Date > rangeEnd) continue;
                // "Odpolední 13-21 h", the last workplace row, is the posunutá služba.
                if (entry.WorkplaceName.StartsWith("Odpolední", StringComparison.OrdinalIgnoreCase))
                    AddDuty(shifted, entry.Date, entry.WorkplaceName, entry.PersonName);
                var isMe =(myId is not null && entry.PersonId == myId) || NamesMatch(entry.PersonName, myName);
                if (isMe)
                    resolved[entry.Date] = (AssignmentType.Work, entry.WorkplaceName, null);
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
    private static void AddDuty(Dictionary<DateOnly, List<DutyEntry>> byDate, DateOnly date, string position, string name)
    {
        if (name.Length == 0) return;
        if (!byDate.TryGetValue(date, out var list)) byDate[date] = list = [];
        if (!list.Any(e => e.Position == position && NamesMatch(e.Name, name)))
            list.Add(new DutyEntry(position, name));
    }

    private static async Task MergeSluzbyAsync(HttpClient http, DateOnly rangeStart, DateOnly rangeEnd, string myName, Dictionary<DateOnly, (AssignmentType Type, string? WorkplaceName, string? OnCallWorkplaceName)> resolved, Dictionary<DateOnly, List<DutyEntry>> onDuty, CancellationToken ct)
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
                    var name = WebUtility.HtmlDecode(StripTags(cellMatch.Groups["name"].Value)).Replace(' ', ' ').Trim();
                    if (name.Any(char.IsLetter))
                        AddDuty(onDuty, date, columns[cellIndex], name);
                    if (NamesMatch(name, myName))
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
                //
                // VV/NV/PS all map to DayOff (see KnownLeaveCodes' own remarks) but the user wants the
                // raw code visible, not just the generic "Volno" label (2026-09-22: "pokud je na webu
                // PS dej PS ne volno") — reusing WorkplaceName as free-text label for a non-Work day,
                // the same trick the AssignmentType.Other fallback right below already relies on.
                resolved[date] = KnownLeaveCodes.TryGetValue(code, out var type)
                    ? (type, type == AssignmentType.DayOff ? code : null, null)
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

    private static string StripTags(string html) => OpicentrumParsing.StripTags(html);

    private static Regex SpravavolnaRowRegex(int myId) => new(
        $@"idlekuprvolna={myId}"">[^<]*</a>(?<cells>.*?)</tr>",
        RegexOptions.Singleline);

    // --- pozadavky.php (PushNoteAsync) — see that method's own remarks for why each of these reads
    // what it reads (particularly PlnvolhodRegex, not a static <option selected>, for the true hours value). ---

    private static Regex PozadavkyRowRegex(int day, int month, int year) => new(
        $@"<tr class=""planakci"" align=""center"">\s*<td>{day:D2}\.{month:D2}\.{year}</td>.*?</tr>",
        RegexOptions.Singleline);

    private static Regex NpCheckedRegex(int day) => new($@"name=""np\[{day}\]""[^>]*\schecked");

    private static Regex PlnvolhodRegex(int day) => new($@"plnvolhod\({day},(?<hours>[^,]*),");

    private static Regex KodvolnaSelectedRegex(int day) => new(
        $@"name=""kodvolna\[{day}\]""[^>]*>.*?<option selected>(?<code>[^<]*)</option>",
        RegexOptions.Singleline);

    private static Regex SpecsalValueRegex(int day) => new($@"name=""specsal\[{day}\]""[^>]*value=""(?<value>[^""]*)""");

    [GeneratedRegex(@"Vítej\s+(?<name>[^<]+)")]
    private static partial Regex WelcomeRegex();

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

    /// <summary>Same anchor <see cref="SpravavolnaRowRegex"/> keys off, but capturing the id and name of EVERY person on the monthly leave grid so an unknown name can be resolved to an id.</summary>
    [GeneratedRegex(@"idlekuprvolna=(?<osoba>\d+)"">(?<name>[^<]*)</a>")]
    private static partial Regex LeavePersonLinkRegex();

}
