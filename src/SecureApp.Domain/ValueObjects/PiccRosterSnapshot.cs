namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// What the PICC roster xlsx says for one specific day, after parsing (2026-10-12) — see
/// <c>IPiccRosterService</c>'s own remarks. <see cref="DoctorName"/> is null when the sheet has no
/// row for that date (never guessed/carried over from a previous day). <see cref="NurseNames"/> is
/// the "sestry" cell split on the user's own documented dual format — "mohou být samy nebo ve dvou
/// a jména oddělená plus nebo čárkou" — so it holds 0, 1 or 2 names.
/// </summary>
public sealed record PiccRosterDay(DateOnly Date, string? DoctorName, IReadOnlyList<string> NurseNames);

/// <summary>
/// The full parsed roster, as the picc_team uploader's own device sees it right after parsing the
/// file they just picked (for the upload confirmation) and as any picc_team device sees it after
/// downloading the shared pointer — <see cref="Today"/> is the only day the widget/UI actually needs
/// day-to-day, the rest of <see cref="AllDays"/> is kept only so the upload confirmation can show a
/// short preview of surrounding days as a sanity check.
/// </summary>
public sealed record PiccRosterSnapshot(PiccRosterDay? Today, IReadOnlyList<PiccRosterDay> AllDays, DateTimeOffset ParsedAtUtc);

/// <summary>Just the public half of a <see cref="PiccRosterSnapshot"/> — the on-call doctor's name, which every device (not only picc_team) learns via <c>/picc/status</c>. <see cref="DoctorName"/> null means no roster has ever been published, or no row matched today.</summary>
public sealed record PiccStatus(string? DoctorName, DateOnly? AsOfDate, DateTimeOffset UpdatedAtUtc);
