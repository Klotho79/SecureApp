using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Rendering;

/// <inheritdoc cref="IDocumentDownloadLogService"/>
/// <remarks>Endpoint and device auth come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpDocumentDownloadLogService : RelayServiceBase, IDocumentDownloadLogService
{
    private readonly ICurrentUserService _currentUserService;

    public HttpDocumentDownloadLogService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository, ICurrentUserService currentUserService)
        : base(vault, transportSettingsRepository)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task LogAsync(string documentTitle, Guid? sourceLibraryFileId, CancellationToken ct = default)
    {
        // Best-effort by design (see interface remarks) — a relay hiccup must never block the user
        // from actually getting their file, so every failure here is logged, not surfaced.
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, "document-downloads", new
            {
                DisplayName = _currentUserService.Current.DisplayName,
                DocumentTitle = documentTitle,
                SourceLibraryFileId = sourceLibraryFileId,
            }, ct);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                Infrastructure.AppLog.Error(nameof(HttpDocumentDownloadLogService), $"document-download log rejected: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error(nameof(HttpDocumentDownloadLogService), "document-download log failed", ex);
        }
    }
}
