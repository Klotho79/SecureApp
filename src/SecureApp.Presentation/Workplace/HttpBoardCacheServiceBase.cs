using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Shared client for the relay's singleton "opaque JSON blob" board caches (<c>/opicentrum-roster-board</c>,
/// <c>/opicentrum-volno-board</c>): the snapshot is serialized client-side and sent as a
/// <c>ContentBlob</c> string, deserialized back on read — the relay never interprets it. Same
/// endpoint/device-auth plumbing as every other Http*Service here (needs <c>HttpClient</c> + relay
/// configuration, so it lives in Presentation).
/// </summary>
public abstract class HttpBoardCacheServiceBase<TSnapshot> where TSnapshot : class
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly string _route;
    private readonly HttpClient _httpClient = new();

    protected HttpBoardCacheServiceBase(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository, string route)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _route = route;
    }

    public async Task<TSnapshot?> GetCachedAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, _route));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<BoardBlobDto>(HttpJsonOptions, ct);
        return dto is null ? null : JsonSerializer.Deserialize<TSnapshot>(dto.ContentBlob, HttpJsonOptions);
    }

    public async Task PublishAsync(TSnapshot snapshot, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var blob = JsonSerializer.Serialize(snapshot, HttpJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, _route))
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
