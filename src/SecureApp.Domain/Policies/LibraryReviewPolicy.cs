namespace SecureApp.Domain.Policies;

/// <summary>
/// Pure, dependency-free rule for who may review a Document Library submission (2026-10-01). A
/// separate method rather than folded into <see cref="RoleAccessPolicy.IsAllowed"/> because this
/// isn't a plain role→action check — it also depends on whether the reviewer is the same device that
/// submitted it (self-review is never allowed, regardless of capability) — same "depends on more
/// than role" shape as <see cref="RoleAccessPolicy.CanDeleteMessage"/>.
/// </summary>
public static class LibraryReviewPolicy
{
    public static bool CanReview(bool isDocumentReviewer, bool isAdmin, string reviewerDeviceId, string submitterDeviceId)
        => (isDocumentReviewer || isAdmin) && !string.Equals(reviewerDeviceId, submitterDeviceId, StringComparison.OrdinalIgnoreCase);
}
