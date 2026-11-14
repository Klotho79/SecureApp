using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Shared client for the relay's singleton "opaque JSON blob" board caches (<c>/opicentrum-roster-board</c>,
/// <c>/opicentrum-volno-board</c>): the snapshot is serialized client-side and sent as a
/// <c>ContentBlob</c> string, deserialized back on read — the relay never interprets it. Endpoint and
/// device auth come from <see cref="RelayServiceBase"/>.
/// </summary>
public abstract class HttpBoardCacheServiceBase<TSnapshot>(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository, string route)
    : RelayServiceBase(vault, transportSettingsRepository) where TSnapshot : class
{
    /// <summary>The cached snapshot, or null when the relay has none yet (404).</summary>
    public async Task<TSnapshot?> GetCachedAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<BoardBlobDto>(route, ct, notFoundAsDefault: true);
        return dto is null ? null : JsonSerializer.Deserialize<TSnapshot>(dto.ContentBlob, HttpJsonOptions);
    }

    public Task PublishAsync(TSnapshot snapshot, CancellationToken ct = default)
        => SendOrThrowAsync(HttpMethod.Post, route, new { ContentBlob = JsonSerializer.Serialize(snapshot, HttpJsonOptions) }, ct);

    private sealed record BoardBlobDto(string ContentBlob, DateTimeOffset UpdatedAtUtc);
}
