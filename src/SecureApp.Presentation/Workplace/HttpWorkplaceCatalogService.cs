using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IWorkplaceCatalogService"/>
/// <remarks>Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpWorkplaceCatalogService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : RelayServiceBase(vault, transportSettingsRepository), IWorkplaceCatalogService
{
    public async Task<IReadOnlyList<Domain.ValueObjects.Workplace>> FetchAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<WorkplaceDto>>("workplaces", ct) ?? [];
        return dtos.Select(d => new Domain.ValueObjects.Workplace(d.Id, d.Name, d.Description, d.CreatedAtUtc)).ToList();
    }

    public Task<bool> PublishAsync(Domain.ValueObjects.Workplace workplace, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "workplaces", new WorkplaceDto(workplace.Id, workplace.Name, workplace.Description, workplace.CreatedAtUtc), ct);

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Delete, $"workplaces/{id}", null, ct);

    private sealed record WorkplaceDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc);
}
