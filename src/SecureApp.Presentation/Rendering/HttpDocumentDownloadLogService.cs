using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Rendering;

/// <inheritdoc cref="IDocumentDownloadLogService"/>
/// <remarks>
/// Lives in Presentation, same established split every other Http*Service here already follows —
/// device-auth header helper and ws-&gt;http rewrite duplicated from <c>HttpSharedContactService</c>
/// rather than shared, same "duplicated, stays independently readable" call this codebase already made.
/// </remarks>
public sealed class HttpDocumentDownloadLogService : IDocumentDownloadLogService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly HttpClient _httpClient = new();

    public HttpDocumentDownloadLogService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository, ICurrentUserService currentUserService)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task LogAsync(string documentTitle, Guid? sourceLibraryFileId, CancellationToken ct = default)
    {
        // Best-effort by design (see interface remarks) — a relay hiccup must never block the user
        // from actually getting their file, so every failure here is swallowed, not surfaced.
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "document-downloads"))
            {
                Content = JsonContent.Create(new
                {
                    DisplayName = _currentUserService.Current.DisplayName,
                    DocumentTitle = documentTitle,
                    SourceLibraryFileId = sourceLibraryFileId,
                }, options: HttpJsonOptions),
            };
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                Infrastructure.AppLog.Error(nameof(HttpDocumentDownloadLogService), $"document-download log rejected: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error(nameof(HttpDocumentDownloadLogService), "document-download log failed", ex);
        }
    }

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri ?? new Uri(RelayDefaults.DefaultEndpoint);
        var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port, Path = "/" }.Uri;
    }

    private async Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Zařízení ještě není zaregistrované u relay serveru.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }
}
