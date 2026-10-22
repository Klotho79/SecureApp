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
/// + relay endpoint/device-identity configuration. Endpoint lookup and device-auth headers come from
/// <see cref="RelayHttp"/>, shared by every Http*Service here.
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

    private Task<Uri> GetHttpEndpointAsync(CancellationToken ct) => RelayHttp.GetHttpEndpointAsync(_transportSettingsRepository, ct);

    private Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct) => RelayHttp.AddDeviceAuthAsync(request, _transportSettingsRepository, _vault, ct);

    private sealed record LogbookChecklistDto(Guid Id, string Name, IReadOnlyList<string> Items, DateTimeOffset CreatedAtUtc);
    private sealed record LogbookProcedureTypeDto(Guid Id, string Name, string Abbreviation, string Category, DateTimeOffset CreatedAtUtc);
}
