using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// A stored document's metadata without its (possibly very large) encrypted content (2026-11-16) — for
/// listings and the library mirror, which must not pull every PDF's ciphertext into memory just to show names.
/// </summary>
public sealed record DocumentInfo(
    Guid Id,
    string Title,
    string FileName,
    DocumentType DocumentType,
    Guid? FolderId,
    Guid? SourceLibraryFileId,
    bool IsFavorite);
