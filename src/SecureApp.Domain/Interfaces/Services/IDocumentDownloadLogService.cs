namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Document download audit log (2026-09-30, user's own ask: "bude log kdo co kdy stahl podle
/// dokumentu vyhledatelny"). One entry per "Stáhnout" tap in DocumentViewerPage — plaintext,
/// admin-searchable (see <c>IRelayAdminService.SearchDocumentDownloadsAsync</c> for the read side;
/// this interface is only the WRITE side, device-authed like <c>ISharedContactService</c>, not
/// admin-authed).
/// </summary>
public interface IDocumentDownloadLogService
{
    /// <summary>Best-effort: a logging failure (relay unreachable, ...) must never prevent the user from actually getting their file — callers should fire this and not let its exception stop the download.</summary>
    Task LogAsync(string documentTitle, Guid? sourceLibraryFileId, CancellationToken ct = default);
}
