namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One reviewer decision on a <see cref="LibraryDocumentVersionSummary"/> (2026-10-01) — the
/// content-lifecycle audit trail, the direct twin of <see cref="DocumentDownloadEntry"/> (which only
/// tracks downloads, not approval decisions). <see cref="Decision"/> is "Approved" or "Rejected".
/// Written by <c>POST /library-documents/{id}/review</c>, admin-searchable via
/// <c>GET /admin/library-documents/audit</c> — see <c>IRelayAdminService.SearchDocumentDownloadsAsync</c>'s
/// sibling for the exact read-side precedent this follows.
/// </summary>
public sealed record LibraryDocumentReviewEntry(
    Guid Id,
    Guid LibraryDocumentId,
    Guid VersionId,
    string ReviewerDeviceId,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAtUtc);
