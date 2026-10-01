namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One content revision of a <see cref="LibraryDocumentSummary"/> (2026-10-01). <see cref="LibraryFileId"/>
/// points at an ordinary <c>library_files</c> row — staging a version reuses the existing
/// <c>ISharedLibraryService.UploadAsync</c> encrypted-upload machinery verbatim (same call a private
/// chat attachment already uses), so no new crypto code exists anywhere in this workflow.
/// </summary>
public sealed record LibraryDocumentVersionSummary(
    Guid Id,
    Guid LibraryDocumentId,
    int VersionNumber,
    Guid LibraryFileId,
    string AuthorDeviceId,
    DateTimeOffset CreatedAtUtc,
    string? ChangeNote);
