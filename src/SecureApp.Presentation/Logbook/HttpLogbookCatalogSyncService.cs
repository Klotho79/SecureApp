using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Logbook;

/// <inheritdoc cref="ILogbookCatalogSyncService"/>
/// <remarks>Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpLogbookCatalogSyncService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : RelayServiceBase(vault, transportSettingsRepository), ILogbookCatalogSyncService
{
    public Task<bool> PublishChecklistAsync(LogbookChecklistTemplate template, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "logbook/checklists", new LogbookChecklistDto(template.Id, template.Name, template.Items, template.CreatedAtUtc), ct);

    public Task<bool> PublishProcedureTypeAsync(LogbookProcedureType type, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "logbook/procedure-types",
            new LogbookProcedureTypeDto(type.Id, type.Name, type.Abbreviation, type.Category.ToString(), type.CreatedAtUtc), ct);

    public Task<bool> DeleteChecklistAsync(Guid id, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Delete, $"logbook/checklists/{id}", null, ct);

    public Task<bool> DeleteProcedureTypeAsync(Guid id, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Delete, $"logbook/procedure-types/{id}", null, ct);

    public async Task<IReadOnlyList<LogbookChecklistTemplate>> FetchChecklistsAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<LogbookChecklistDto>>("logbook/checklists", ct) ?? [];
        return dtos.Select(d => new LogbookChecklistTemplate(d.Id, d.Name, d.Items)).ToList();
    }

    public async Task<IReadOnlyList<LogbookProcedureType>> FetchProcedureTypesAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<LogbookProcedureTypeDto>>("logbook/procedure-types", ct) ?? [];
        return dtos.Select(d => new LogbookProcedureType(d.Id, d.Name, d.Abbreviation, Enum.Parse<LogbookProcedureCategory>(d.Category))).ToList();
    }

    private sealed record LogbookChecklistDto(Guid Id, string Name, IReadOnlyList<string> Items, DateTimeOffset CreatedAtUtc);
    private sealed record LogbookProcedureTypeDto(Guid Id, string Name, string Abbreviation, string Category, DateTimeOffset CreatedAtUtc);
}
