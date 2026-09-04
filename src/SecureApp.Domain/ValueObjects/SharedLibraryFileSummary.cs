namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Metadata for one file in the community's shared library, hosted centrally on the relay (never
/// replicated locally — always fetched live via <c>ISharedLibraryService</c>). Carries no
/// ciphertext; content is fetched separately via <c>ISharedLibraryService.DownloadAndImportAsync</c>.
/// </summary>
public sealed record SharedLibraryFileSummary(
    Guid Id,
    string FolderPath,
    string FileName,
    IReadOnlyList<string> Tags,
    long SizeBytes,
    string UploadedByDeviceId,
    DateTimeOffset UploadedAtUtc);
