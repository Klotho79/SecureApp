namespace SecureApp.Domain.ValueObjects;

/// <summary>One person occupying one workplace slot on one day of the Opicentrum roster board.</summary>
public sealed record OpicentrumRosterPerson(int PersonId, string PersonName);

/// <summary>
/// One workplace row on one day — <paramref name="WorkplaceId"/> is the site's own numeric "sal" id
/// (999 for "NEZAŘAZENÍ"/unassigned, which is modeled as an ordinary row, not a special case — see
/// <c>OpicentrumSyncService.FetchFullRosterBoardAsync</c>'s own remarks).
/// </summary>
public sealed record OpicentrumRosterWorkplace(int WorkplaceId, string WorkplaceName, IReadOnlyList<OpicentrumRosterPerson> People);

public sealed record OpicentrumRosterDay(DateOnly Date, IReadOnlyList<OpicentrumRosterWorkplace> Workplaces);

/// <summary>
/// 2026-10-21 — a cached, relay-shared snapshot of pracoviste.php's full weekly roster (every person,
/// every workplace, including NEZAŘAZENÍ), so every device reads this instead of each one scraping
/// opicentrum.cz itself. See <c>IOpicentrumRosterBoardService</c>'s own remarks for the cache contract.
/// </summary>
public sealed record OpicentrumRosterBoardSnapshot(DateOnly WeekOf, DateTimeOffset UpdatedAtUtc, IReadOnlyList<OpicentrumRosterDay> Days);
