using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Relay-backed cache of the Opicentrum volno (leave) board (2026-10-21) — same singleton-snapshot
/// contract as <see cref="IOpicentrumRosterBoardService"/>, just for spravavolna.php's monthly leave
/// grid instead of pracoviste.php's weekly roster. Relay-enforced Admin+Modifier only on both read
/// and write, matching the roster board's own scope decision.
/// </summary>
public interface IOpicentrumVolnoBoardService
{
    Task<OpicentrumVolnoSnapshot?> GetCachedAsync(CancellationToken ct = default);

    Task PublishAsync(OpicentrumVolnoSnapshot snapshot, CancellationToken ct = default);
}
