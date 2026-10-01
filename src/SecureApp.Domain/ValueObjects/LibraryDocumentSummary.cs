using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// A reviewable Document Library entry (2026-10-01, the content-approval workflow) — the stable
/// identity that persists across versions. Layered ALONGSIDE the existing <c>library_files</c>
/// metadata (one row per encrypted blob/version), never replacing it: <see cref="CurrentLibraryFileId"/>
/// points at whichever <c>library_files</c> row is the currently-listed one. An existing pre-workflow
/// <c>library_files</c> row with no matching row here simply has no approval history — it is still an
/// ordinary published file, found the same way it always was via <c>GET /library/files</c>.
/// </summary>
public sealed record LibraryDocumentSummary(
    Guid Id,
    string Title,
    string FolderPath,
    LibraryDocumentStatus Status,
    Guid? CurrentVersionId,
    Guid? CurrentLibraryFileId,
    string CreatedByDeviceId,
    string? SubmittedByDeviceId,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
