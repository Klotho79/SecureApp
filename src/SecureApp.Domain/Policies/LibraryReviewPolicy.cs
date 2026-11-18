using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free rule for who may review a Document Library submission (2026-10-01). A
/// separate method rather than folded into <see cref="RoleAccessPolicy.IsAllowed"/> because this
/// isn't a plain role→action check — it also depends on whether the reviewer is the same device that
/// submitted it (self-review is never allowed, regardless of capability) — same "depends on more
/// than role" shape as <see cref="RoleAccessPolicy.CanDeleteMessage"/>. Shared by the app and the relay.
/// </summary>
public static class LibraryReviewPolicy
{
    /// <summary>
    /// Admin, a <see cref="Role.Redaktor"/> (by role, 2026-11-18), or any member with the "Reviewer dokumentů" switch.
    /// <paramref name="role"/> null = no role assigned on the relay.
    /// </summary>
    public static bool MayReview(Role? role, bool isDocumentReviewer) => isDocumentReviewer || role is Role.Admin or Role.Redaktor;

    public static bool CanReview(Role? role, bool isDocumentReviewer, string reviewerDeviceId, string submitterDeviceId)
        => MayReview(role, isDocumentReviewer) && !string.Equals(reviewerDeviceId, submitterDeviceId, StringComparison.OrdinalIgnoreCase);
}
