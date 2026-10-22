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

    private Task<Uri> GetHttpEndpointAsync(CancellationToken ct) => RelayHttp.GetHttpEndpointAsync(_transportSettingsRepository, ct);

    private Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct) => RelayHttp.AddDeviceAuthAsync(request, _transportSettingsRepository, _vault, ct);

    private sealed record BoardBlobDto(string ContentBlob, DateTimeOffset UpdatedAtUtc);
}
