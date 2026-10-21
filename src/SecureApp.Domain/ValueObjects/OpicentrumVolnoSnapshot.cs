using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>One day's leave entry for one person, from spravavolna.php's monthly grid.</summary>
public sealed record OpicentrumVolnoEntry(DateOnly Date, string RawCode, AssignmentType MappedType);

public sealed record OpicentrumVolnoPerson(int PersonId, string PersonName, IReadOnlyList<OpicentrumVolnoEntry> Entries);

/// <summary>
/// 2026-10-21 — a cached, relay-shared snapshot of spravavolna.php's monthly leave grid for every
/// staff member, not just the logged-in device's own person. Read-only this round (see
/// <c>IOpicentrumSyncService.FetchVolnoForMonthAsync</c>'s own remarks on why writing another
/// person's entry isn't built yet) — see <c>IOpicentrumVolnoBoardService</c> for the cache contract.
/// </summary>
public sealed record OpicentrumVolnoSnapshot(int Year, int Month, DateTimeOffset UpdatedAtUtc, IReadOnlyList<OpicentrumVolnoPerson> People);
