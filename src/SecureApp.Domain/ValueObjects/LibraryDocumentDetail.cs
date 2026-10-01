namespace SecureApp.Domain.ValueObjects;

/// <summary>Full detail view of one reviewable Document Library entry (2026-10-01) — its own summary plus its complete version and review history, for the review-queue detail screen and the admin audit view.</summary>
public sealed record LibraryDocumentDetail(
    LibraryDocumentSummary Document,
    IReadOnlyList<LibraryDocumentVersionSummary> Versions,
    IReadOnlyList<LibraryDocumentReviewEntry> Reviews);
