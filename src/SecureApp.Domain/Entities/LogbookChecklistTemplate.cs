using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One named checklist (2026-09-09) — e.g. the reference logbook's own "co dělat… den před anestezií",
/// "…před RSI" (SOAP-ME), "…před neuroaxiální blokádou". Admin-managed (see <c>RbacAction.ManageLogbookCatalog</c>):
/// the item TEXT is data the app treats as opaque, catalog-defined content, same as
/// <see cref="LogbookProcedureType"/>'s name — nothing in this app understands what "SOAP-ME" means,
/// it just stores and displays the list an admin typed in.
///
/// Deliberately NOT stored as a child table of individual checkable rows with their own ids: a
/// checklist is meant to be worked through fresh every time (a new anesthesia, a new shift), not
/// accumulate history the way <see cref="LogbookProcedureEntry"/> does — so its items are just an
/// ordered list of strings, and which ones are currently ticked is transient UI state
/// (<c>LogbookChecklistViewModel</c>), never persisted. Reordering/editing the item list itself
/// still goes through <see cref="SetItems"/>, replacing the whole list at once — same
/// full-replace-not-diff shape <c>IGroupMemberRepository.ReplaceAllAsync</c> already established
/// elsewhere in this codebase for "the admin edited a list" operations.
/// </summary>
public sealed class LogbookChecklistTemplate : Entity
{
    public string Name { get; private set; }

    /// <summary>A plain property (not a manually-backed list field) specifically so <c>EntityMaterializer.Set</c>'s reflection-based hydration can assign it directly like every other entity property in this codebase — see that class's own remarks.</summary>
    public IReadOnlyList<string> Items { get; private set; }

    private LogbookChecklistTemplate()
    {
        // Reserved for materialization by persistence infrastructure.
        Name = string.Empty;
        Items = [];
    }

    public LogbookChecklistTemplate(string name, IReadOnlyList<string> items)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Checklist name cannot be empty.", nameof(name));

        Name = name;
        Items = items?.Where(i => !string.IsNullOrWhiteSpace(i)).ToList() ?? [];
    }

    /// <summary>
    /// Reconstructs a checklist with an EXISTING id (2026-09-10) — same reasoning as
    /// <see cref="GroupChat"/>'s own <c>Guid id</c> constructor: syncing this catalog across every
    /// device via the relay (see <c>ILogbookCatalogSyncService</c>) needs a device that already has
    /// an item to recognize it as "already known" rather than minting a duplicate local copy with a
    /// fresh id every time it re-fetches the same shared catalog.
    /// </summary>
    public LogbookChecklistTemplate(Guid id, string name, IReadOnlyList<string> items) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Checklist name cannot be empty.", nameof(name));

        Name = name;
        Items = items?.Where(i => !string.IsNullOrWhiteSpace(i)).ToList() ?? [];
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Checklist name cannot be empty.", nameof(name));

        Name = name;
        Touch();
    }

    /// <summary>Full replace, not incremental — see the class-level remarks on why.</summary>
    public void SetItems(IReadOnlyList<string> items)
    {
        Items = items.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        Touch();
    }
}
