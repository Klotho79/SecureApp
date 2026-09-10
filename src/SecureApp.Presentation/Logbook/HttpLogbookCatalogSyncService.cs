using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Logbook;

/// <inheritdoc cref="ILogbookCatalogSyncService"/>
/// <remarks>
/// Lives in Presentation, same reasoning as every other Http*Service here — needs <c>HttpClient</c>
/// + relay endpoint/device-identity configuration. Device-auth header helper and ws-&gt;http rewrite
/// duplicated from <c>HttpContactDirectoryService</c> rather than shared, same established
/// "duplicated, stays independently readable" call.
/// </remarks>
public sealed class HttpLogbookCatalogSyncService : ILogbookCatalogSyncService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpLogbookCatalogSyncService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task<bool> PublishChecklistAsync(LogbookChecklistTemplate template, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "logbook/checklists"))
            {
                Content = JsonContent.Create(new LogbookChecklistDto(template.Id, template.Name, template.Items, template.CreatedAtUtc), options: HttpJsonOptions)
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

    public async Task<bool> PublishProcedureTypeAsync(LogbookProcedureType type, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "logbook/procedure-types"))
            {
                Content = JsonContent.Create(new LogbookProcedureTypeDto(type.Id, type.Name, type.Abbreviation, type.Category.ToString(), type.CreatedAtUtc), options: HttpJsonOptions)
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

    public async Task<bool> DeleteChecklistAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, $"logbook/checklists/{id}"));
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteProcedureTypeAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, $"logbook/procedure-types/{id}"));
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LogbookChecklistTemplate>> FetchChecklistsAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "logbook/checklists"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<LogbookChecklistDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new LogbookChecklistTemplate(d.Id, d.Name, d.Items)).ToList();
    }

    public async Task<IReadOnlyList<LogbookProcedureType>> FetchProcedureTypesAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "logbook/procedure-types"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<LogbookProcedureTypeDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new LogbookProcedureType(d.Id, d.Name, d.Abbreviation, Enum.Parse<LogbookProcedureCategory>(d.Category))).ToList();
    }

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("No relay endpoint is configured yet — set one up in Settings first.");

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
            ?? throw new InvalidOperationException("Register with a relay in Settings first.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("Relay device secret is missing from the vault.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private sealed record LogbookChecklistDto(Guid Id, string Name, IReadOnlyList<string> Items, DateTimeOffset CreatedAtUtc);
    private sealed record LogbookProcedureTypeDto(Guid Id, string Name, string Abbreviation, string Category, DateTimeOffset CreatedAtUtc);
}
