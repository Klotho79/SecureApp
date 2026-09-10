using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One entry in the Modifier/Admin-managed catalog of loggable items (2026-09-09) — e.g. "CŽK",
/// "Spinální anestezie", "Laryngospasmus", mirroring a row of one of the reference logbook's three
/// "Kompetence dle…" tables (see <see cref="LogbookProcedureCategory"/>). Gated behind
/// <c>RbacAction.ManageLogbookProcedureCatalog</c> — briefly Admin-only (2026-09-09, "položky zadá
/// admin"), walked back to Modifier-equivalent-to-Admin the same day ("modifer a admin mohou
/// přidávat typy výkonů i check listy") — every role, Viewer included, may pick from the resulting
/// list when logging a <see cref="LogbookProcedureEntry"/> (<c>RbacAction.RecordLogbookProcedure</c>).
/// </summary>
public sealed class LogbookProcedureType : Entity
{
    public string Name { get; private set; }
    public LogbookProcedureCategory Category { get; private set; }

    private LogbookProcedureType()
    {
        // Reserved for materialization by persistence infrastructure.
        Name = string.Empty;
    }

    public LogbookProcedureType(string name, LogbookProcedureCategory category)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));

        Name = name;
        Category = category;
    }

    /// <summary>Reconstructs a procedure type with an EXISTING id (2026-09-10) — same reasoning as <see cref="LogbookChecklistTemplate"/>'s own <c>Guid id</c> constructor, for the same catalog-sync purpose.</summary>
    public LogbookProcedureType(Guid id, string name, LogbookProcedureCategory category) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));

        Name = name;
        Category = category;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));

        Name = name;
        Touch();
    }
}
