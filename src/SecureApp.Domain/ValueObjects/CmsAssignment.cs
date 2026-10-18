namespace SecureApp.Domain.ValueObjects;

/// <summary>An assignable member. <see cref="DeviceId"/> is one device of theirs; the grant covers every device with the same <see cref="Email"/>.</summary>
public sealed record CmsMember(Guid DeviceId, string DisplayName, string? Email)
{
    public string Label => string.IsNullOrWhiteSpace(Email) ? DisplayName : $"{DisplayName} ({Email})";
}

/// <summary>One active editor grant. <see cref="DocumentStatus"/> is null until the first draft exists, then the library document status (Draft/PendingReview/Published).</summary>
public sealed record CmsAssignment(
    Guid DocumentId, Guid EditorDeviceId, string EditorName, string Title, string FolderPath,
    DateTimeOffset GrantedAtUtc, string? DocumentStatus);
