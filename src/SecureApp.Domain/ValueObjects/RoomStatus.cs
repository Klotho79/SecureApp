namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Live "how's this sál going" status for one <see cref="Workplace"/> (2026-10-13, user's own ask on
/// Nástěnka: a status board — "jestli anestezie probíhá dle plánu nebo se protahuje" — per sál, plus
/// an overview of every other sál). Relay-backed, visible to the whole community; see
/// <see cref="Interfaces.Services.IRoomStatusService"/> for who may write it.
///
/// <see cref="WorkplaceName"/> is a snapshot, not a live FK lookup — same reasoning as
/// <see cref="Entities.WorkAssignment.WorkplaceName"/>: the catalog entry could be renamed or deleted
/// later, and a status row should stay displayable regardless.
///
/// <see cref="NoteText"/> (2026-10-14, user's own ask) is a second, independently-curated field on
/// the same row — "poslední uzavření sálu, poslední výkon, sál pokračuje do služby" — picked from
/// its own admin/modifier-defined list (<see cref="Interfaces.Services.IRoomStatusService.GetNoteOptionsAsync"/>),
/// same editability as <see cref="StatusText"/> itself. Never null, defaults to "" for a status that
/// predates this field or has never had a note set.
/// </summary>
public sealed record RoomStatus(Guid WorkplaceId, string WorkplaceName, string StatusText, string UpdatedByDisplayName, DateTimeOffset UpdatedAtUtc, string NoteText = "");
