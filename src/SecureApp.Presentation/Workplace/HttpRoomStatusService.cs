using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IRoomStatusService"/>
/// <remarks>Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpRoomStatusService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : RelayServiceBase(vault, transportSettingsRepository), IRoomStatusService
{
    public async Task<IReadOnlyList<RoomStatus>> ListAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<RoomStatusDto>>("room-status", ct) ?? [];
        return dtos.Select(d => new RoomStatus(d.WorkplaceId, d.WorkplaceName, d.StatusText, d.UpdatedByDisplayName, d.UpdatedAtUtc, d.NoteText)).ToList();
    }

    public Task<bool> SetStatusAsync(Guid workplaceId, string workplaceName, string? statusText, string? noteText = null, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "room-status", new { WorkplaceId = workplaceId, WorkplaceName = workplaceName, StatusText = statusText, NoteText = noteText }, ct);

    public async Task<IReadOnlyList<string>> GetStatusOptionsAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<string>>("room-status/options", ct) ?? [];

    public Task<bool> SetStatusOptionsAsync(IReadOnlyList<string> options, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "room-status/options", new { Options = options }, ct);

    public async Task<IReadOnlyList<string>> GetNoteOptionsAsync(CancellationToken ct = default)
        => await GetJsonAsync<List<string>>("room-status/notes", ct) ?? [];

    public Task<bool> SetNoteOptionsAsync(IReadOnlyList<string> options, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Post, "room-status/notes", new { Options = options }, ct);

    private sealed record RoomStatusDto(Guid WorkplaceId, string WorkplaceName, string StatusText, string UpdatedByDisplayName, DateTimeOffset UpdatedAtUtc, string NoteText);
}
