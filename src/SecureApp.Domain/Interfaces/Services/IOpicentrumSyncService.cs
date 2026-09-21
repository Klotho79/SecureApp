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
}
