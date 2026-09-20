using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Contacts;

/// <inheritdoc cref="ISharedContactService"/>
/// <remarks>
/// Lives in Presentation, same reasoning as every other Http*Service here — needs <c>HttpClient</c>
/// + relay endpoint/device-identity configuration. Device-auth header helper and ws-&gt;http rewrite
/// duplicated from <c>HttpLogbookCatalogSyncService</c> rather than shared, same established
/// "duplicated, stays independently readable" call.
/// </remarks>
public sealed class HttpSharedContactService : ISharedContactService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpSharedContactService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task<IReadOnlyList<SharedContact>> FetchAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "contacts"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<SharedContactDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new SharedContact(d.Id, d.DisplayName, d.Phone, d.Note, d.SortOrder, d.CreatedAtUtc)).ToList();
    }

    public async Task<bool> PublishAsync(SharedContact contact, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "contacts"))
            {
                Content = JsonContent.Create(new SharedContactDto(contact.Id, contact.DisplayName, contact.Phone, contact.Note, contact.SortOrder, contact.CreatedAtUtc), options: HttpJsonOptions)
            };
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, $"contacts/{id}"));
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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

    private sealed record SharedContactDto(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc);
}
