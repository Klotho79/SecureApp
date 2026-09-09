using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free permission matrix for <see cref="Role"/>. A static policy rather
/// than an injected service on purpose — it's pure calculation with no I/O.
///
/// Current matrix: <see cref="Role.Viewer"/> is denied every <see cref="RbacAction"/> (read-only —
/// browsing and opening documents is never gated in the first place, so nothing else is needed
/// to make Viewer read-only). <see cref="Role.Modifier"/> and <see cref="Role.Admin"/> were fully
/// equivalent until <see cref="RbacAction.ManageLogbookCatalog"/> (2026-09-09) — the FIRST actual
/// use of the split this class's own earlier remarks had flagged as "reserved for future
/// admin-only features": the user's own explicit call, "položky zadá admin", makes editing the
/// Logbook's catalog Admin-only while every other action stays Modifier-equivalent-to-Admin as before.
/// </summary>
public static class RoleAccessPolicy
{
    public static bool IsAllowed(Role role, RbacAction action) => (role, action) switch
    {
        (Role.Viewer, _) => false,
        (Role.Admin, _) => true,
        (Role.Modifier, RbacAction.ManageLogbookCatalog) => false,
        (Role.Modifier, _) => true,
        _ => false
    };
}
