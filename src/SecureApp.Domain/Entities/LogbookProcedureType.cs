using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One entry in the admin-managed catalog of loggable items (2026-09-09) — e.g. "CŽK", "Spinální
/// anestezie", "Laryngospasmus", mirroring a row of one of the reference logbook's three
/// "Kompetence dle…" tables (see <see cref="LogbookProcedureCategory"/>). Only an Admin-role device
/// can create/edit these (<c>RbacAction.ManageLogbookCatalog</c> — the user's own explicit split:
/// "položky zadá admin"); every other role just picks from the resulting list when logging a
/// <see cref="LogbookProcedureEntry"/>.
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

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Procedure type name cannot be empty.", nameof(name));

        Name = name;
        Touch();
    }
}
