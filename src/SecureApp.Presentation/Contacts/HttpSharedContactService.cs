using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Contacts;

/// <inheritdoc cref="ISharedContactService"/>
/// <remarks>Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpSharedContactService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : RelayServiceBase(vault, transportSettingsRepository), ISharedContactService
{
    public async Task<IReadOnlyList<SharedContact>> FetchAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<SharedContactDto>>("contacts", ct) ?? [];
        return dtos.Select(d => new SharedContact(d.Id, d.DisplayName, d.Phone, d.Note, d.SortOrder, d.CreatedAtUtc, d.Email)).ToList();
    }

    public Task<bool> PublishAsync(SharedContact contact, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "contacts",
            new SharedContactDto(contact.Id, contact.DisplayName, contact.Phone, contact.Note, contact.SortOrder, contact.CreatedAtUtc, contact.Email), ct);

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Delete, $"contacts/{id}", null, ct);

    private sealed record SharedContactDto(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc, string? Email = null);
}
