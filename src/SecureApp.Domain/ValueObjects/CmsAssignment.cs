namespace SecureApp.Domain.ValueObjects;

/// <summary>One active editor grant. <see cref="DocumentStatus"/> is null until the first draft exists, then the library document status (Draft/PendingReview/Published).</summary>
public sealed record CmsAssignment(
    Guid DocumentId, Guid EditorDeviceId, string EditorName, string Title, string FolderPath,
    DateTimeOffset GrantedAtUtc, string? DocumentStatus);
