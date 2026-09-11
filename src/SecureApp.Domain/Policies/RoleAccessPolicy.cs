using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free permission matrix for <see cref="Role"/>. A static policy rather
/// than an injected service on purpose — it's pure calculation with no I/O.
///
/// Current matrix: <see cref="Role.Viewer"/> is denied every <see cref="RbacAction"/> (read-only —
/// browsing and opening documents is never gated in the first place, so nothing else is needed
/// to make Viewer read-only) EXCEPT the two "just use the Logbook day-to-day" actions
/// <see cref="RbacAction.RecordLogbookProcedure"/> and <see cref="RbacAction.ViewLogbookStatistics"/>
/// (2026-09-10, the user's own explicit corrections: "každý uživatel má právo zadávat výkony... ale
/// přidávat typy výkonů do výběrového menu jen modifer a admin", then "statistiku by měl vidět i
/// viewer" — every role may log a procedure entry by picking an EXISTING type from the catalog, and
/// see the statistics rollup; only Modifier/Admin may edit either catalog). <see cref="Role.Modifier"/>
/// and <see cref="Role.Admin"/> are otherwise fully equivalent — including
/// <see cref="RbacAction.ManageLogbookProcedureCatalog"/>, which briefly was Admin-only (2026-09-09,
/// "položky zadá admin") before the user's own same-day follow-up walked that split back: "modifer a
/// admin mohou přidávat typy výkonů i check listy" — Modifier manages both Logbook catalogs exactly
/// like Admin, no special carve-out anywhere in this app's RBAC left.
/// </summary>
public static class RoleAccessPolicy
{
    public static bool IsAllowed(Role role, RbacAction action) => (role, action) switch
    {
        (Role.Viewer, RbacAction.RecordLogbookProcedure) => true,
        (Role.Viewer, RbacAction.ViewLogbookStatistics) => true,
        (Role.Viewer, _) => false,
        (Role.Admin, _) => true,
        (Role.Modifier, _) => true,
        _ => false
    };

    /// <summary>
    /// Chat message deletion (2026-09-11, the user's own spec): "jen admin muze mazat jakoukoli.
    /// Modifer muze mazat sve a vieweru a viewer muze mazat jen sve zpravy" — Admin may delete any
    /// message; a Modifier may delete their own and any Viewer's; a Viewer may delete only their own.
    /// A separate method rather than an <see cref="RbacAction"/> because this isn't a plain role→action
    /// yes/no — it also depends on whether the message is the actor's own and on who sent it.
    /// </summary>
    /// <param name="actorRole">The role of the user attempting the deletion (this device's current role).</param>
    /// <param name="isOwnMessage">Whether the message being deleted was sent by the actor.</param>
    /// <param name="senderRole">The role the message's sender held when they sent it — may be null for messages sent before this was tracked, in which case a non-own deletion is denied for everyone except Admin (safe degradation).</param>
    public static bool CanDeleteMessage(Role actorRole, bool isOwnMessage, Role? senderRole)
    {
        if (actorRole == Role.Admin) return true;
        if (isOwnMessage) return true;
        // Non-own message: only a Modifier may delete it, and only if a Viewer sent it.
        return actorRole == Role.Modifier && senderRole == Role.Viewer;
    }
}
