using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free permission matrix for <see cref="Role"/>. A static policy rather
/// than an injected service on purpose — it's pure calculation with no I/O.
///
/// Current matrix: <see cref="Role.Viewer"/> is denied every <see cref="RbacAction"/> (read-only —
/// browsing and opening documents is never gated in the first place, so nothing else is needed
/// to make Viewer read-only) EXCEPT <see cref="RbacAction.RecordLogbookProcedure"/> (2026-09-10,
/// the user's own explicit correction after an earlier miscommunication: "každý uživatel má právo
/// zadávat výkony... ale přidávat typy výkonů do výběrového menu jen modifer a admin" — every role,
/// Viewer included, may log a procedure entry by picking an EXISTING type from the catalog).
/// <see cref="Role.Modifier"/> and <see cref="Role.Admin"/> are otherwise fully equivalent —
/// including <see cref="RbacAction.ManageLogbookProcedureCatalog"/>, which briefly was Admin-only
/// (2026-09-09, "položky zadá admin") before the user's own same-day-as-the-above follow-up walked
/// that split back: "modifer a admin mohou přidávat typy výkonů i check listy" — Modifier manages
/// both Logbook catalogs (checklists AND procedure types) exactly like Admin, no special carve-out
/// anywhere in this app's RBAC left.
/// </summary>
public static class RoleAccessPolicy
{
    public static bool IsAllowed(Role role, RbacAction action) => (role, action) switch
    {
        (Role.Viewer, RbacAction.RecordLogbookProcedure) => true,
        (Role.Viewer, _) => false,
        (Role.Admin, _) => true,
        (Role.Modifier, _) => true,
        _ => false
    };
}
