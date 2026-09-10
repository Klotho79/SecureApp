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

    /// <summary>
    /// A short code (2026-09-10, "statistiku je třeba lépe formátovat, ideálně zkratka, ta se zadá
    /// při zadávání typu výkonu") — e.g. "CŽK" for "Centrální žilní katetr". Entered explicitly at
    /// creation time rather than derived from <see cref="Name"/> (a real clinical abbreviation isn't
    /// reliably the first letters of the full name), and is what the statistics rollup leads with —
    /// see <c>LogbookStatItem</c>'s own remarks.
    /// </summary>
    public string Abbreviation { get; private set; }

    public LogbookProcedureCategory Category { get; private set; }

    private LogbookProcedureType()
    {
        // Reserved for materialization by persistence infrastructure.
        Name = string.Empty;
        Abbreviation = string.Empty;
    }

    public LogbookProcedureType(string name, string abbreviation, LogbookProcedureCategory category)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(abbreviation))
            throw new ArgumentException("Procedure type abbreviation cannot be empty.", nameof(abbreviation));

        Name = name;
        Abbreviation = abbreviation;
        Category = category;
    }

    /// <summary>Reconstructs a procedure type with an EXISTING id (2026-09-10) — same reasoning as <see cref="LogbookChecklistTemplate"/>'s own <c>Guid id</c> constructor, for the same catalog-sync purpose.</summary>
    public LogbookProcedureType(Guid id, string name, string abbreviation, LogbookProcedureCategory category) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(abbreviation))
            throw new ArgumentException("Procedure type abbreviation cannot be empty.", nameof(abbreviation));

        Name = name;
        Abbreviation = abbreviation;
        Category = category;
    }

    public void Rename(string name, string abbreviation)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(abbreviation))
            throw new ArgumentException("Procedure type abbreviation cannot be empty.", nameof(abbreviation));

        Name = name;
        Abbreviation = abbreviation;
        Touch();
    }
}
