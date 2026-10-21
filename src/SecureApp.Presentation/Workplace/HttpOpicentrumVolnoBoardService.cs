using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IOpicentrumVolnoBoardService"/>
/// <remarks>Same shape as <see cref="HttpOpicentrumRosterBoardService"/>, just for the volno snapshot.</remarks>
public sealed class HttpOpicentrumVolnoBoardService : IOpicentrumVolnoBoardService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpOpicentrumVolnoBoardService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task<OpicentrumVolnoSnapshot?> GetCachedAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "opicentrum-volno-board"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<BoardBlobDto>(HttpJsonOptions, ct);
        return dto is null ? null : JsonSerializer.Deserialize<OpicentrumVolnoSnapshot>(dto.ContentBlob, HttpJsonOptions);
    }

    public async Task PublishAsync(OpicentrumVolnoSnapshot snapshot, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var blob = JsonSerializer.Serialize(snapshot, HttpJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "opicentrum-volno-board"))
        {
            Content = JsonContent.Create(new { ContentBlob = blob }, options: HttpJsonOptions)
        };
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");

        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private async Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private sealed record BoardBlobDto(string ContentBlob, DateTimeOffset UpdatedAtUtc);
}
