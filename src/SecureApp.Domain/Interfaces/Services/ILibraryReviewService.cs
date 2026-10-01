using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Client-side gateway to the Document Library content-approval workflow (2026-10-01) — draft,
/// submit, review, approve/reject, with versioning and an audit trail. Layered alongside
/// <see cref="ISharedLibraryService"/>, which still does the actual encrypted upload/download of each
/// version's content; this interface only manages the review LIFECYCLE around those uploads.
/// Device-authenticated throughout, same as <see cref="ISharedLibraryService"/>.
/// </summary>
public interface ILibraryReviewService
{
    /// <summary>Creates a new Draft document whose first version is the already-uploaded (private, <c>listed:false</c>) library file <paramref name="libraryFileId"/>.</summary>
    Task<LibraryDocumentSummary> CreateDraftAsync(string title, string folderPath, Guid libraryFileId, string? changeNote, CancellationToken ct = default);

    /// <summary>Stages a new version on an existing document (e.g. a Rejected one being revised) from an already-uploaded private library file. Resets status to Draft.</summary>
    Task<LibraryDocumentSummary> AddVersionAsync(Guid documentId, Guid libraryFileId, string? changeNote, CancellationToken ct = default);

    /// <summary>Moves the document's current (Draft) version into the reviewer queue.</summary>
    Task<LibraryDocumentSummary> SubmitForReviewAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Every document this device created or submitted — the "Moje koncepty" list.</summary>
    Task<IReadOnlyList<LibraryDocumentSummary>> GetMyDocumentsAsync(CancellationToken ct = default);

    /// <summary>Every document currently awaiting review — only meaningful for a device with review capability; the relay itself also enforces this.</summary>
    Task<IReadOnlyList<LibraryDocumentSummary>> GetPendingReviewAsync(CancellationToken ct = default);

    /// <summary>Full detail (versions + review history) for one document.</summary>
    Task<LibraryDocumentDetail> GetDetailAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Approves (publishes) or rejects the document's current pending version. <paramref name="comment"/> is required when rejecting.</summary>
    Task ReviewAsync(Guid documentId, bool approve, string? comment, CancellationToken ct = default);
}
