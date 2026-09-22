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
}
