using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One logged instance of performing (or observing) a <see cref="LogbookProcedureType"/>, at a
/// given <see cref="LogbookCompetenceLevel"/> (2026-09-09) — this is what actually accumulates into
/// statistics ("kolikrát jsem dělal CŽK samostatně"), unlike the reference paper logbook's single
/// checkbox-plus-signature per competency-table row. Local to this device only, same as every other
/// per-user record in this app (no multi-user sync model — see <c>RoleAccessPolicy</c>'s own remarks).
/// </summary>
public sealed class LogbookProcedureEntry : Entity
{
    public Guid ProcedureTypeId { get; private set; }
    public LogbookCompetenceLevel Level { get; private set; }
    public DateTimeOffset PerformedAtUtc { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Where it was performed (2026-09-10, "seznam: datum, poznámka, místo") — free text, e.g. a ward/OR name; optional, same shape as <see cref="Note"/>.</summary>
    public string? Place { get; private set; }

    private LogbookProcedureEntry()
    {
        // Reserved for materialization by persistence infrastructure.
    }

    public LogbookProcedureEntry(Guid procedureTypeId, LogbookCompetenceLevel level, DateTimeOffset performedAtUtc, string? note = null, string? place = null)
    {
        if (procedureTypeId == Guid.Empty)
            throw new ArgumentException("Procedure type id cannot be empty.", nameof(procedureTypeId));

        ProcedureTypeId = procedureTypeId;
        Level = level;
        PerformedAtUtc = performedAtUtc;
        Note = string.IsNullOrWhiteSpace(note) ? null : note;
        Place = string.IsNullOrWhiteSpace(place) ? null : place;
    }
}
