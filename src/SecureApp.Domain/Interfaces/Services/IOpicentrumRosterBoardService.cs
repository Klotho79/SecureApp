using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Relay-backed cache of the Opicentrum roster board (2026-10-21) — a single shared snapshot per
/// week, so every device reads this instead of each one logging into opicentrum.cz and scraping it
/// itself (the user's own explicit ask: avoid needlessly hitting the external site). Only one
/// snapshot is kept at a time (the relay's own singleton-row contract, same shape as the PICC status
/// cache); <see cref="GetCachedAsync"/> returns whatever was last published regardless of which week
/// was asked for — callers compare <see cref="OpicentrumRosterBoardSnapshot.WeekOf"/> themselves to
/// decide whether it's stale for what they actually wanted. Relay-enforced Admin+Modifier only on
/// both read and write (the user's own scope decision — this board is not for every role to see).
/// </summary>
public interface IOpicentrumRosterBoardService
{
    Task<OpicentrumRosterBoardSnapshot?> GetCachedAsync(CancellationToken ct = default);

    Task PublishAsync(OpicentrumRosterBoardSnapshot snapshot, CancellationToken ct = default);
}
