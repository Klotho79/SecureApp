namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One row of the document-download audit log (2026-09-30) — see <c>IRelayAdminService.SearchDocumentDownloadsAsync</c>
/// (the read side) and <c>IDocumentDownloadLogService.LogAsync</c> (the write side, from
/// DocumentViewerPage's own "Stáhnout" button). <see cref="SourceLibraryFileId"/> is set when the
/// downloaded document originated from the shared community library (see <c>Document.SourceLibraryFileId</c>'s
/// own remarks) — the same id across every device that ever downloaded that same shared file, so an
/// admin searching by document title finds every device/person who downloaded it, not just one.
/// </summary>
public sealed record DocumentDownloadEntry(Guid Id, Guid DeviceId, string DisplayName, string DocumentTitle, Guid? SourceLibraryFileId, DateTimeOffset DownloadedAtUtc);
