using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IWorkplaceCatalogService"/>
/// <remarks>
/// Lives in Presentation, same reasoning as every other Http*Service here — needs <c>HttpClient</c>
/// + relay endpoint/device-identity configuration. Endpoint lookup and device-auth headers come from
/// <see cref="RelayHttp"/>, shared by every Http*Service here.
/// </remarks>
public sealed class HttpWorkplaceCatalogService : IWorkplaceCatalogService
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpWorkplaceCatalogService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task<IReadOnlyList<Domain.ValueObjects.Workplace>> FetchAsync(CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "workplaces"));
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<WorkplaceDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new Domain.ValueObjects.Workplace(d.Id, d.Name, d.Description, d.CreatedAtUtc)).ToList();
    }

    public async Task<bool> PublishAsync(Domain.ValueObjects.Workplace workplace, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "workplaces"))
            {
                Content = JsonContent.Create(new WorkplaceDto(workplace.Id, workplace.Name, workplace.Description, workplace.CreatedAtUtc), options: HttpJsonOptions)
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
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, $"workplaces/{id}"));
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private Task<Uri> GetHttpEndpointAsync(CancellationToken ct) => RelayHttp.GetHttpEndpointAsync(_transportSettingsRepository, ct);

    private Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct) => RelayHttp.AddDeviceAuthAsync(request, _transportSettingsRepository, _vault, ct);

    private sealed record WorkplaceDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc);
}
