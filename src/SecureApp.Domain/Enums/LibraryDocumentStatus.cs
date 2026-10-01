namespace SecureApp.Domain.Enums;

/// <summary>
/// Lifecycle status of a reviewable Document Library entry (2026-10-01, the content-approval
/// workflow). Approve and publish are the same step — there is no separate "approved but not yet
/// released" state, matching this app's existing single-step <c>TryPublishLibraryFile</c> rather
/// than adding a release step nothing else here has. Rejected is not terminal: the author stages a
/// new version (back to Draft) and resubmits.
/// </summary>
public enum LibraryDocumentStatus
{
    Draft = 0,
    PendingReview = 1,
    Published = 2,
    Rejected = 3
}
