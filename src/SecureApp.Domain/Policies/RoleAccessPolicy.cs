using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free permission matrix for <see cref="Role"/>. A static policy rather
/// than an injected service on purpose — it's pure calculation with no I/O.
///
/// Current matrix: <see cref="Role.Viewer"/> is denied every <see cref="RbacAction"/> (read-only —
/// browsing and opening documents is never gated in the first place, so nothing else is needed
/// to make Viewer read-only). <see cref="Role.Modifier"/> and <see cref="Role.Admin"/> are
/// equivalent today; the split is reserved for future admin-only features (e.g. managing other
/// users' roles) that don't exist yet since there's no multi-user model — see
/// DEVELOPMENT_PLAN.md's Milestone 3 note.
/// </summary>
public static class RoleAccessPolicy
{
    public static bool IsAllowed(Role role, RbacAction action) => role switch
    {
        Role.Viewer => false,
        Role.Modifier => true,
        Role.Admin => true,
        _ => false
    };
}
