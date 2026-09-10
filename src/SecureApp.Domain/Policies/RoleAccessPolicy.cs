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
/// Viewer included, may log a procedure entry by picking an EXISTING type from the catalog; only
/// Modifier/Admin may add a new type to that catalog in the first place, via the still-untouched
/// <see cref="RbacAction.ManageLogbookProcedureCatalog"/> gate below). <see cref="Role.Modifier"/>
/// and <see cref="Role.Admin"/> were fully equivalent until <see cref="RbacAction.ManageLogbookProcedureCatalog"/>
/// (2026-09-09) — the FIRST actual use of the split this class's own earlier remarks had flagged as
/// "reserved for future admin-only features": the user's own explicit call, "položky zadá admin",
/// makes editing the Logbook's procedure-type catalog specifically Admin-only, while every other
/// Logbook action (checklists, statistics) stays Modifier-equivalent-to-Admin, same as every other
/// action here.
/// </summary>
public static class RoleAccessPolicy
{
    public static bool IsAllowed(Role role, RbacAction action) => (role, action) switch
    {
        (Role.Viewer, RbacAction.RecordLogbookProcedure) => true,
        (Role.Viewer, _) => false,
        (Role.Admin, _) => true,
        (Role.Modifier, RbacAction.ManageLogbookProcedureCatalog) => false,
        (Role.Modifier, _) => true,
        _ => false
    };
}
