using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Pulls the logged-in user's own schedule from the external Opicentrum/ARO staff portal
/// (opicentrum.cz/ARO — a hospital-run phpRS site, not SecureApp's own infrastructure) into the local
/// <c>WorkAssignment</c> table (2026-09-21, user's own ask — "zdroj by mela byt stranka
/// opicentrum.cz"). The portal's own login (username/password) is a THIRD-PARTY credential, stored
/// only in this device's own encrypted vault, entered directly into Settings — never routed through
/// chat/dev tooling. See <c>OpicentrumSyncService</c>'s own remarks for the page-by-page scrape.
/// </summary>
public interface IOpicentrumSyncService
{
    Task<bool> HasCredentialsAsync(CancellationToken ct = default);
    Task SetCredentialsAsync(string username, string password, CancellationToken ct = default);
    Task ClearCredentialsAsync(CancellationToken ct = default);

    /// <summary>
    /// Best-effort: fetches and writes every day's status in [<paramref name="rangeStart"/>,
    /// <paramref name="rangeEnd"/>], and returns <see cref="OpicentrumSyncResult.NotConfigured"/>
    /// (success, zero counts) when no credentials are set yet rather than treating that as an error —
    /// see that property's own remarks.
    /// </summary>
    Task<OpicentrumSyncResult> SyncAsync(DateOnly rangeStart, DateOnly rangeEnd, CancellationToken ct = default);

    /// <summary>
    /// Writes <paramref name="note"/> to the "Poznámka" field on pozadavky.php for <paramref name="date"/>
    /// — the actual Opicentrum leave-REQUEST form (a different page/concept than the read-only sync
    /// above), 2026-09-22, the user's own explicit ask: "chci aby se poznamka propsala do webu". This
    /// is a genuine WRITE to the user's real hospital scheduling system, not a local-only change — see
    /// <c>OpicentrumSyncService.PushNoteAsync</c>'s own remarks for the care taken to never disturb any
    /// other field on that day's row (hours/leave-code/checkbox) while doing it. Truncated to the web
    /// form's own 20-character limit before sending; a day with no editable row on the web at all (a
    /// non-worked/grayed-out day) returns <see cref="OpicentrumNotePushStatus.DayNotEditable"/> rather
    /// than attempting anything.
    /// </summary>
    Task<OpicentrumNotePushResult> PushNoteAsync(DateOnly date, string? note, CancellationToken ct = default);

    /// <summary>
    /// Every person name the portal shows for <paramref name="month"/> (2026-10-10, user's ask: check
    /// the ARIM contact list from WhatsApp against the names on Opicentrum, logging in through the app
    /// so the credential never leaves the device). Read-only, same login as <see cref="SyncAsync"/>.
    /// Throws <see cref="InvalidOperationException"/> with a user-facing message when there are no
    /// credentials or the login fails.
    /// </summary>
    Task<IReadOnlyList<string>> FetchStaffNamesAsync(DateOnly month, CancellationToken ct = default);

    /// <summary>
    /// Every distinct workplace/room label pracoviste.php's weekly roster lists for the week
    /// containing <paramref name="weekOf"/> — across EVERY person rostered that week, not just the
    /// logged-in user (2026-10-14, user's own ask: "stáhni kompletní sálové rozlišení... všechny
    /// pozice"). Same read-only login as <see cref="FetchStaffNamesAsync"/>; throws
    /// <see cref="InvalidOperationException"/> with a user-facing message when there are no
    /// credentials or the login fails.
    /// </summary>
    Task<IReadOnlyList<string>> FetchWorkplaceNamesAsync(DateOnly weekOf, CancellationToken ct = default);

    /// <summary>
    /// Every distinct workplace/room label pracoviste.php's roster lists for exactly <paramref name="date"/>
    /// (2026-10-14, "Stav sálů" full-board bugfix) — across EVERY person rostered that day, not just
    /// the logged-in user. This is the real "kompletní rozvržení salu" row source for
    /// <c>RoomStatusViewModel</c>'s full board; the admin-curated shared Workplace catalog it used
    /// before was sparse (manually added, 3 entries) and not what the user meant by "complete". Same
    /// read-only login as <see cref="FetchWorkplaceNamesAsync"/>; throws
    /// <see cref="InvalidOperationException"/> with a user-facing message when there are no
    /// credentials or the login fails.
    /// </summary>
    Task<IReadOnlyList<string>> FetchWorkplaceNamesForDateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// The full pracoviste.php roster for the week containing <paramref name="weekOf"/> — every
    /// workplace, every person, including the NEZAŘAZENÍ (unassigned) bucket as an ordinary workplace
    /// row with id 999 — shaped for <c>IOpicentrumRosterBoardService</c>'s relay cache rather than for
    /// local <c>WorkAssignment</c> storage (2026-10-21, the admin roster-board ask: "nahled na
    /// kompletni obsazeni pozic vcetne nezarazenych"). Pure fetch — does not touch the relay itself,
    /// same division of responsibility as <see cref="FetchWorkplaceNamesAsync"/>. Throws
    /// <see cref="InvalidOperationException"/> with a user-facing message when there are no
    /// credentials or the login fails.
    /// </summary>
    Task<OpicentrumRosterBoardSnapshot> FetchFullRosterBoardAsync(DateOnly weekOf, CancellationToken ct = default);

    /// <summary>
    /// Moves (or additionally assigns) <paramref name="personId"/> on <paramref name="date"/> from
    /// workplace <paramref name="sourceWorkplaceId"/> to <paramref name="targetWorkplaceId"/> — the
    /// real pracoviste.php roster-editor action, reverse-engineered live 2026-10-21 from the user's
    /// own browser session (see <c>OpicentrumSyncService.MoveRosterPersonAsync</c>'s own remarks for
    /// the exact URL shape and how <paramref name="keepSourceAssignment"/> maps to the site's
    /// left-click-vs-right-click distinction). This is a genuine write to the real hospital schedule,
    /// confirmed by re-fetching before returning <see cref="OpicentrumMoveStatus.Success"/> — same
    /// discipline as <see cref="PushNoteAsync"/>. <paramref name="targetWorkplaceId"/> of 999 means
    /// "move to NEZAŘAZENÍ" and uses the site's own hardcoded action code for that case regardless of
    /// <paramref name="keepSourceAssignment"/>.
    /// </summary>
    Task<OpicentrumMoveResult> MoveRosterPersonAsync(DateOnly date, int personId, int sourceWorkplaceId, int targetWorkplaceId, bool keepSourceAssignment, CancellationToken ct = default);

    /// <summary>
    /// Every staff member's leave entries for <paramref name="month"/> from spravavolna.php's monthly
    /// grid — generalizes the per-person row/cell regexes <see cref="SyncAsync"/> already uses for
    /// just the logged-in user's own leave into a loop over every person id the page lists (2026-10-21,
    /// the admin roster-board ask: "nahled... obsluhu volna" — this covers the "nahled" half; writing
    /// another person's entry is a tracked follow-up, see <c>IOpicentrumVolnoBoardService</c>'s own
    /// remarks on why). Read-only; throws <see cref="InvalidOperationException"/> with a user-facing
    /// message when there are no credentials or the login fails.
    /// </summary>
    Task<OpicentrumVolnoSnapshot> FetchVolnoForMonthAsync(DateOnly month, CancellationToken ct = default);

    /// <summary>
    /// Everyone's duty slots for <paramref name="month"/> from sluzby7.php (2026-10-25, the position-suggestion
    /// history — POZICE_NAVRH_PLAN.md phase 2). The page names people, not ids. Read-only; throws
    /// <see cref="InvalidOperationException"/> with a user-facing message when there are no credentials or the login fails.
    /// </summary>
    Task<OpicentrumDutySnapshot> FetchDutiesForMonthAsync(DateOnly month, CancellationToken ct = default);

    /// <summary>
    /// The separate read-only login to the nurses' roster at opicentrum.cz/AROS (2026-10-28, the unstaffed-position
    /// alerts: "zkontroluješ zda je obsazeno pracoviště lékařem i sestrou"). A shared viewer account, stored in the
    /// device's encrypted vault like the main one and never sent anywhere but the portal.
    /// </summary>
    Task<bool> HasNurseCredentialsAsync(CancellationToken ct = default);
    Task SetNurseCredentialsAsync(string username, string password, CancellationToken ct = default);
    Task ClearNurseCredentialsAsync(CancellationToken ct = default);

    /// <summary>
    /// The full roster grid of the week containing <paramref name="weekOf"/> — every workplace row, empty cells included —
    /// from the doctors' portal, or from the nurses' one when <paramref name="nurses"/> is set. Read-only; throws
    /// <see cref="InvalidOperationException"/> with a user-facing message when there are no credentials or the login fails.
    /// </summary>
    Task<OpicentrumGridWeek> FetchRosterGridAsync(DateOnly weekOf, bool nurses, CancellationToken ct = default);
}
